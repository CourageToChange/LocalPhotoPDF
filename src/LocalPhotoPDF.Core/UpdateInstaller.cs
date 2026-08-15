using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LocalPhotoPDF.Core;

/// <summary>
/// Checks for, downloads and verifies a published update.
/// </summary>
/// <remarks>
/// 🔒 This is the only part of LocalPhotoPDF that touches the network, and it only ever runs
/// because the person using the application asked it to. There is no timer, no startup check and
/// no background poll. If nobody presses the button, the application makes no request, ever.
/// That is the whole privacy promise of this app, so it is enforced here by simply not having any
/// code path that starts a check on its own.
/// </remarks>
public interface IUpdateService
{
    /// <summary>Asks GitHub whether a newer release exists. Performs exactly one request.</summary>
    Task<UpdateCheckResult> CheckAsync(ReleaseVersion currentVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the installer for <paramref name="update"/> and verifies it against the checksum
    /// file published in the same release.
    /// </summary>
    /// <exception cref="UpdateVerificationException">
    /// The download did not match its published checksum, or the checksum file did not list it.
    /// Nothing is left on disk and nothing is executed.
    /// </exception>
    Task<VerifiedInstaller> DownloadVerifiedInstallerAsync(
        AvailableUpdate update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A downloaded installer and the digest it was proven to have at the moment it was written.
/// </summary>
/// <remarks>
/// 🔒 The digest is carried, not discarded, on purpose. Verification happens once when the download
/// finishes, but the file is then executed later, when the person presses Install, which may be
/// minutes or hours afterwards. In between it sits in a temporary directory that every process
/// running as the same user can write to, and whose name is a fixed, published prefix. Without
/// re-checking, "verified" describes only the moment the bytes landed, not the moment they ran.
/// Call <see cref="UpdateService.MatchesAsync"/> immediately before launching.
/// </remarks>
public sealed record VerifiedInstaller(string Path, string Sha256);

/// <summary>
/// A download could not be proven to be the file that was published.
/// </summary>
/// <remarks>
/// This is deliberately a distinct type. It must never be swallowed by a general "download failed"
/// handler, because a checksum mismatch is not a transient network problem: it means the bytes on
/// disk are not the bytes that were released, and they must not be executed.
/// </remarks>
public sealed class UpdateVerificationException : Exception
{
    public UpdateVerificationException(string message) : base(message)
    {
    }
}

/// <inheritdoc cref="IUpdateService"/>
public sealed class UpdateService : IUpdateService, IDisposable
{
    private const int MaximumRedirects = 5;

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Uri _latestReleaseEndpoint;

    /// <param name="repositorySlug">Owner and repository, e.g. <c>CourageToChange/LocalPhotoPDF</c>.</param>
    /// <param name="handler">
    /// Supplied by tests so the whole flow, including checksum failure, can be exercised without a
    /// network. When null, a handler is created with automatic redirects OFF, because each redirect
    /// target is validated against the host allowlist by hand rather than followed blindly.
    /// </param>
    public UpdateService(string repositorySlug, HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySlug);

        _ownsClient = handler is null;
        handler ??= new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        };

        _client = new HttpClient(handler, disposeHandler: _ownsClient)
        {
            Timeout = TimeSpan.FromMinutes(10),
        };

        // GitHub rejects API requests without a User-Agent. It identifies the app and nothing else:
        // no machine name, no account, no install id. Nothing here distinguishes one user from another.
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LocalPhotoPDF-update-check", "1"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        _latestReleaseEndpoint = new Uri(
            $"https://api.github.com/repos/{repositorySlug}/releases/latest",
            UriKind.Absolute);

        if (!UpdateManifest.IsAllowed(_latestReleaseEndpoint))
        {
            throw new ArgumentException("The repository slug did not produce an allowed endpoint.", nameof(repositorySlug));
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(
        ReleaseVersion currentVersion,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendFollowingValidatedRedirectsAsync(
            _latestReleaseEndpoint, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // A repository with no releases answers 404 here. That is "nothing published yet",
            // not an error worth alarming anyone about.
            return UpdateCheckResult.None(UpdateCheckOutcome.NoReleasesPublished);
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return UpdateManifest.Parse(json, currentVersion);
    }

    /// <summary>
    /// Re-hashes a file on disk and reports whether it still matches <paramref name="expected"/>.
    /// </summary>
    /// <remarks>
    /// 🔒 This is the guard against the gap between verifying and running. The download is proven
    /// genuine once, then waits in a directory any same-user process can write to, under a fixed
    /// and publicly known name prefix, until someone presses Install. Re-reading the bytes at the
    /// moment of launch is what makes the earlier proof mean anything. It is local, synchronous,
    /// and adds no network activity.
    /// </remarks>
    public static async Task<bool> MatchesAsync(
        string path,
        string expected,
        CancellationToken cancellationToken = default)
    {
        var handle = await OpenIfMatchesAsync(path, expected, cancellationToken).ConfigureAwait(false);
        if (handle is null)
        {
            return false;
        }

        await handle.DisposeAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Re-hashes the file and, when it still matches, returns it with the handle <b>still open</b>.
    /// The caller disposes it only after the installer has been started.
    /// </summary>
    /// <remarks>
    /// 🔒 Verifying and then closing the file leaves a window, however small, between the check and
    /// the launch: the same-user process that could swap the download before can still swap it in
    /// that gap. Keeping the handle open across <c>Process.Start</c> removes the window rather than
    /// narrowing it. <see cref="FileShare.Read"/> lets Windows read the image to execute it while
    /// denying every writer, and because <c>FILE_SHARE_DELETE</c> is not granted the file cannot be
    /// renamed or deleted out from under us either.
    /// </remarks>
    /// <returns>The open, verified file, or null if it is missing or has changed.</returns>
    public static async Task<FileStream?> OpenIfMatchesAsync(
        string path,
        string expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expected);

        if (!File.Exists(path))
        {
            return null;
        }

        var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

        try
        {
            var actual = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
                .ToLowerInvariant();

            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actual), Convert.FromHexString(expected)))
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<VerifiedInstaller> DownloadVerifiedInstallerAsync(
        AvailableUpdate update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (!UpdateManifest.IsAllowed(update.InstallerUrl) || !UpdateManifest.IsAllowed(update.ChecksumsUrl))
        {
            throw new UpdateVerificationException("The update pointed somewhere this application will not download from.");
        }

        var expectedDigest = ChecksumFile.FindDigest(
            await ReadChecksumsAsync(update.ChecksumsUrl, cancellationToken).ConfigureAwait(false),
            update.InstallerFileName);

        if (expectedDigest is null)
        {
            throw new UpdateVerificationException(
                $"The release did not publish a checksum for {update.InstallerFileName}, so the download cannot be verified.");
        }

        // Clear anything a previous update left behind. The staging directory is deleted when a
        // download fails, but a successful one hands the file to the installer and exits, so the
        // installer is still sitting in the temporary folder afterwards. Roughly 45 MB is not
        // worth leaving there indefinitely, and the next check is the natural moment to remove it.
        RemovePreviousStagingDirectories();

        // A fresh directory per attempt. Writing into a shared temp path would let anything else
        // running as this user swap the file between verification and launch.
        var stagingDirectory = Directory.CreateTempSubdirectory("LocalPhotoPDF-update-").FullName;
        var destination = Path.Combine(stagingDirectory, update.InstallerFileName);

        try
        {
            var actualDigest = await DownloadAsync(
                update.InstallerUrl, destination, update.InstallerSizeBytes, progress, cancellationToken)
                .ConfigureAwait(false);

            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualDigest),
                    Convert.FromHexString(expectedDigest)))
            {
                throw new UpdateVerificationException(
                    "The downloaded file did not match the checksum published with the release. It has been deleted and nothing was run.");
            }

            return new VerifiedInstaller(destination, expectedDigest);
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    private async Task<string> ReadChecksumsAsync(Uri url, CancellationToken cancellationToken)
    {
        using var response = await SendFollowingValidatedRedirectsAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // The checksum file is a few hundred bytes, so it is capped at 64 KB. The cap is enforced
        // WHILE reading rather than after.
        //
        // This used to call ReadAsByteArrayAsync and then check the length, which enforces nothing:
        // the whole body is already in memory by the time the check runs. Worse, decompression is
        // on, so a small compressed response can expand far past the cap before anything looks at
        // it, bounded only by HttpClient's 2 GB default. The installer download had this right
        // already and this path did not.
        const int limit = 64 * 1024;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > limit)
            {
                throw new UpdateVerificationException("The published checksum file was implausibly large and was not read.");
            }

            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Streams to disk, capped at the size the release advertised, returning the SHA-256.</summary>
    private async Task<string> DownloadAsync(
        Uri url,
        string destination,
        long expectedSizeBytes,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await SendFollowingValidatedRedirectsAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var limit = Math.Min(expectedSizeBytes, UpdateManifest.MaximumInstallerSizeBytes);

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        using var hash = SHA256.Create();
        var buffer = new byte[81920];
        long written = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            written += read;
            if (written > limit)
            {
                throw new UpdateVerificationException(
                    "The download was larger than the release said it would be, so it was abandoned.");
            }

            hash.TransformBlock(buffer, 0, read, null, 0);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

            if (limit > 0)
            {
                progress?.Report(Math.Min(1.0, (double)written / limit));
            }
        }

        if (written != expectedSizeBytes)
        {
            throw new UpdateVerificationException(
                "The download ended at a different size from the one the release published, so it was abandoned.");
        }

        hash.TransformFinalBlock(buffer, 0, 0);
        return Convert.ToHexString(hash.Hash!).ToLowerInvariant();
    }

    /// <summary>
    /// Issues the request, following redirects only to hosts on the allowlist.
    /// </summary>
    /// <remarks>
    /// GitHub answers an asset URL with a 302 to githubusercontent.com. Automatic redirects are off
    /// so that every hop is checked here instead: a redirect is an instruction from a remote server
    /// about where to fetch code from, and it gets the same scrutiny as the original URL.
    /// </remarks>
    private async Task<HttpResponseMessage> SendFollowingValidatedRedirectsAsync(
        Uri url,
        CancellationToken cancellationToken)
    {
        var current = url;

        for (var hop = 0; hop <= MaximumRedirects; hop++)
        {
            if (!UpdateManifest.IsAllowed(current))
            {
                throw new UpdateVerificationException(
                    $"An update request was redirected to {current.Host}, which this application will not contact.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!IsRedirect(response.StatusCode) || response.Headers.Location is null)
            {
                return response;
            }

            var next = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(current, response.Headers.Location);

            response.Dispose();
            current = next;
        }

        throw new UpdateVerificationException("The update request was redirected too many times and was abandoned.");
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    /// <summary>Removes staging directories left by earlier update attempts. Best effort.</summary>
    /// <remarks>
    /// One that is still in use is skipped rather than fought over: an installer may legitimately
    /// be running out of it, and failing an update check over a leftover file would be absurd.
    /// </remarks>
    internal static int RemovePreviousStagingDirectories()
    {
        var removed = 0;
        string[] candidates;
        try
        {
            candidates = Directory.GetDirectories(Path.GetTempPath(), "LocalPhotoPDF-update-*");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        foreach (var directory in candidates)
        {
            if (TryDeleteDirectory(directory))
            {
                removed++;
            }
        }

        return removed;
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    /// <summary>The repository this application offers updates from.</summary>
    public const string DefaultRepositorySlug = "CourageToChange/LocalPhotoPDF";

    /// <summary>Formats a byte count the way the update prompt shows it.</summary>
    public static string DescribeSize(long bytes) =>
        (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.CurrentCulture) + " MB";
}
