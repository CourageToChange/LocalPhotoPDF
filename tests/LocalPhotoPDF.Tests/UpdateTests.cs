using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LocalPhotoPDF.Core;

namespace LocalPhotoPDF.Tests;

/// <summary>
/// The update check is the only part of this application that touches the network, and the only
/// part that can cause a downloaded executable to run. Every decision it makes is driven by JSON
/// fetched from a remote server, so these tests exist mainly to prove it refuses the hostile cases:
/// a redirect somewhere else, a URL on another host, a plain-HTTP link, and a file whose contents
/// do not match the checksum that was published with it.
/// </summary>
public sealed class UpdateVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V10.0.11", 10, 0, 11)]
    [InlineData("  1.0.0  ", 1, 0, 0)]
    public void TryParse_AcceptsThreePartTags(string text, int major, int minor, int patch)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(new ReleaseVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    [InlineData("-1.2.3")]
    [InlineData("1.+2.3")]
    [InlineData("1.2.3-beta")]
    [InlineData("99999999999.0.0")]
    public void TryParse_RejectsAnythingElse(string? text)
    {
        // A version that cannot be parsed must never come out as a usable value, because the
        // comparison that follows decides whether to offer someone a download.
        Assert.False(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(default, version);
    }

    [Fact]
    public void Comparison_OrdersByMajorThenMinorThenPatch()
    {
        Assert.True(new ReleaseVersion(1, 0, 0) < new ReleaseVersion(1, 0, 1));
        Assert.True(new ReleaseVersion(1, 0, 9) < new ReleaseVersion(1, 1, 0));
        Assert.True(new ReleaseVersion(1, 9, 9) < new ReleaseVersion(2, 0, 0));
        Assert.True(new ReleaseVersion(2, 0, 0) > new ReleaseVersion(1, 99, 99));
        Assert.Equal(new ReleaseVersion(1, 2, 3), new ReleaseVersion(1, 2, 3));
    }
}

public sealed class UpdateManifestTests
{
    private const string Current = "1.0.0";

    private static string Release(
        string tag = "v1.1.0",
        string installerHost = "github.com",
        string installerName = "LocalPhotoPDF-Setup-1.1.0-win-x64.exe",
        long installerSize = 43_000_000,
        bool includeChecksums = true,
        string scheme = "https",
        bool draft = false,
        bool prerelease = false)
    {
        var assets = new List<string>
        {
            $$"""
            {"name":"{{installerName}}","size":{{installerSize}},"browser_download_url":"{{scheme}}://{{installerHost}}/x/y/releases/download/{{tag}}/{{installerName}}"}
            """,
        };

        if (includeChecksums)
        {
            assets.Add("""
            {"name":"SHA256SUMS.txt","size":300,"browser_download_url":"https://github.com/x/y/releases/download/t/SHA256SUMS.txt"}
            """);
        }

        return $$"""
        {
          "tag_name": "{{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "https://github.com/CourageToChange/LocalPhotoPDF/releases/tag/{{tag}}",
          "assets": [{{string.Join(",", assets)}}]
        }
        """;
    }

    private static ReleaseVersion Version(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        return version;
    }

    [Fact]
    public void Parse_OffersANewerRelease()
    {
        var result = UpdateManifest.Parse(Release(), Version(Current));

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
        var update = Assert.IsType<AvailableUpdate>(result.Update);
        Assert.Equal(new ReleaseVersion(1, 1, 0), update.Version);
        Assert.Equal("LocalPhotoPDF-Setup-1.1.0-win-x64.exe", update.InstallerFileName);
        Assert.Equal("github.com", update.InstallerUrl.Host);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.9")]
    public void Parse_NeverOffersTheSameOrAnOlderVersion(string tag)
    {
        // Offering an older release would be a silent downgrade, which is how a fixed
        // vulnerability gets reintroduced on a user's machine by the update button.
        var result = UpdateManifest.Parse(Release(tag, installerName: $"LocalPhotoPDF-Setup-{tag[1..]}-win-x64.exe"), Version(Current));

        Assert.Equal(UpdateCheckOutcome.AlreadyUpToDate, result.Outcome);
        Assert.Null(result.Update);
    }

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("github.com.evil.example.com")]
    [InlineData("notgithub.com")]
    public void Parse_RefusesAnInstallerHostedAnywhereElse(string host)
    {
        var result = UpdateManifest.Parse(Release(installerHost: host), Version(Current));

        Assert.Equal(UpdateCheckOutcome.ReleaseIsNotUsable, result.Outcome);
        Assert.Null(result.Update);
    }

    [Fact]
    public void Parse_RefusesAPlainHttpInstaller()
    {
        var result = UpdateManifest.Parse(Release(scheme: "http"), Version(Current));

        Assert.Equal(UpdateCheckOutcome.ReleaseIsNotUsable, result.Outcome);
    }

    [Fact]
    public void Parse_RefusesAReleaseWithNoChecksumFile()
    {
        // Without the checksum file there is no way to prove the download is what was published,
        // so the update is not offered at all rather than offered unverified.
        var result = UpdateManifest.Parse(Release(includeChecksums: false), Version(Current));

        Assert.Equal(UpdateCheckOutcome.ReleaseIsNotUsable, result.Outcome);
    }

    [Fact]
    public void Parse_RefusesAnInstallerNamedForADifferentVersion()
    {
        var result = UpdateManifest.Parse(
            Release(installerName: "LocalPhotoPDF-Setup-9.9.9-win-x64.exe"), Version(Current));

        Assert.Equal(UpdateCheckOutcome.ReleaseIsNotUsable, result.Outcome);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(300L * 1024 * 1024)]
    public void Parse_RefusesAnImplausibleInstallerSize(long size)
    {
        var result = UpdateManifest.Parse(Release(installerSize: size), Version(Current));

        Assert.Equal(UpdateCheckOutcome.ReleaseIsNotUsable, result.Outcome);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Parse_RefusesDraftsAndPrereleases(bool draft, bool prerelease)
    {
        var result = UpdateManifest.Parse(Release(draft: draft, prerelease: prerelease), Version(Current));

        Assert.Equal(UpdateCheckOutcome.NoReleasesPublished, result.Outcome);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"tag_name":"not-a-version"}""")]
    public void Parse_SurvivesRubbish(string json)
    {
        var result = UpdateManifest.Parse(json, Version(Current));

        Assert.NotEqual(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
        Assert.Null(result.Update);
    }
}

public sealed class ChecksumFileTests
{
    private const string Sample = """
        3f786850e387550fdab836ed7e6dc881de23001b  LocalPhotoPDF-1.1.0-win-x64.zip
        89e6c98d92887913cadf06b2adb97f26cde4849b  LocalPhotoPDF-Setup-1.1.0-win-x64.exe
        """;

    [Fact]
    public void FindDigest_MatchesA64CharacterDigest()
    {
        var digest = new string('a', 64);
        var contents = $"{digest}  LocalPhotoPDF-Setup-1.1.0-win-x64.exe\n";

        Assert.Equal(digest, ChecksumFile.FindDigest(contents, "LocalPhotoPDF-Setup-1.1.0-win-x64.exe"));
    }

    [Fact]
    public void FindDigest_IgnoresShortDigests()
    {
        // The sample above uses 40-character SHA-1 style digests deliberately: anything that is not
        // a 64-character SHA-256 must be ignored rather than compared against a SHA-256 hash.
        Assert.Null(ChecksumFile.FindDigest(Sample, "LocalPhotoPDF-Setup-1.1.0-win-x64.exe"));
    }

    [Fact]
    public void FindDigest_AcceptsTheBinaryStarPrefix()
    {
        var digest = new string('b', 64);

        Assert.Equal(digest, ChecksumFile.FindDigest($"{digest} *setup.exe", "setup.exe"));
    }

    [Fact]
    public void FindDigest_ReturnsNullWhenTheFileIsNotListed() =>
        Assert.Null(ChecksumFile.FindDigest($"{new string('c', 64)}  other.exe", "setup.exe"));
}

/// <summary>
/// End-to-end over a stubbed transport, so the failure paths that matter can actually be run.
/// </summary>
public sealed class UpdateServiceTests
{
    private const string InstallerName = "LocalPhotoPDF-Setup-1.1.0-win-x64.exe";

    private static byte[] InstallerBytes => Encoding.UTF8.GetBytes("pretend this is an installer");

    private static string DigestOf(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage Ok(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static AvailableUpdate Update(long size) => new(
        new ReleaseVersion(1, 1, 0),
        new Uri($"https://github.com/CourageToChange/LocalPhotoPDF/releases/download/v1.1.0/{InstallerName}"),
        InstallerName,
        size,
        new Uri("https://github.com/CourageToChange/LocalPhotoPDF/releases/download/v1.1.0/SHA256SUMS.txt"),
        new Uri("https://github.com/CourageToChange/LocalPhotoPDF/releases/tag/v1.1.0"));

    [Fact]
    public async Task DownloadVerifiedInstaller_ReturnsTheFileWhenTheChecksumMatches()
    {
        var bytes = InstallerBytes;
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
            ? Ok($"{DigestOf(bytes)}  {InstallerName}\n")
            : Ok(bytes));

        using var service = new UpdateService(UpdateService.DefaultRepositorySlug, handler);
        var verified = await service.DownloadVerifiedInstallerAsync(
            Update(bytes.Length), progress: null, TestContext.Current.CancellationToken);
        var path = verified.Path;

        try
        {
            Assert.True(File.Exists(path));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadVerifiedInstaller_RefusesAndDeletesWhenTheChecksumDoesNotMatch()
    {
        // The whole point of the feature. A file that is not the file that was published must not
        // survive on disk, because the next step after this method returns is executing it.
        var bytes = InstallerBytes;
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
            ? Ok($"{new string('a', 64)}  {InstallerName}\n")
            : Ok(bytes));

        using var service = new UpdateService(UpdateService.DefaultRepositorySlug, handler);

        // Compare against a snapshot rather than asserting none exist at all: test classes run in
        // parallel, so another test's staging directory may legitimately be present.
        var before = Directory.GetDirectories(Path.GetTempPath(), "LocalPhotoPDF-update-*");

        var failure = await Assert.ThrowsAsync<UpdateVerificationException>(
            () => service.DownloadVerifiedInstallerAsync(Update(bytes.Length), null, TestContext.Current.CancellationToken));

        Assert.Contains("checksum", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetDirectories(Path.GetTempPath(), "LocalPhotoPDF-update-*").Except(before));
    }

    [Fact]
    public async Task DownloadVerifiedInstaller_RefusesWhenTheChecksumFileDoesNotListTheInstaller()
    {
        var bytes = InstallerBytes;
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
            ? Ok($"{new string('a', 64)}  something-else.zip\n")
            : Ok(bytes));

        using var service = new UpdateService(UpdateService.DefaultRepositorySlug, handler);

        await Assert.ThrowsAsync<UpdateVerificationException>(
            () => service.DownloadVerifiedInstallerAsync(Update(bytes.Length), null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadVerifiedInstaller_RefusesARedirectToAnotherHost()
    {
        // Automatic redirects are off precisely so this can be refused. A 302 is a remote server
        // telling the application where to fetch executable code from, and it gets checked.
        var bytes = InstallerBytes;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal))
            {
                return Ok($"{DigestOf(bytes)}  {InstallerName}\n");
            }

            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://evil.example.com/payload.exe");
            return redirect;
        });

        using var service = new UpdateService(UpdateService.DefaultRepositorySlug, handler);

        var failure = await Assert.ThrowsAsync<UpdateVerificationException>(
            () => service.DownloadVerifiedInstallerAsync(Update(bytes.Length), null, TestContext.Current.CancellationToken));

        Assert.Contains("evil.example.com", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadVerifiedInstaller_FollowsARedirectWithinGitHub()
    {
        var bytes = InstallerBytes;
        var redirected = false;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal))
            {
                return Ok($"{DigestOf(bytes)}  {InstallerName}\n");
            }

            if (!redirected)
            {
                redirected = true;
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://objects.githubusercontent.com/real-asset");
                return redirect;
            }

            return Ok(bytes);
        });

        using var service = new UpdateService(UpdateService.DefaultRepositorySlug, handler);
        var path = (await service.DownloadVerifiedInstallerAsync(
            Update(bytes.Length), null, TestContext.Current.CancellationToken)).Path;

        try
        {
            Assert.True(redirected);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadVerifiedInstaller_StopsAServerThatSendsMoreThanItPromised()
    {
        var bytes = InstallerBytes;
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
            ? Ok($"{DigestOf(bytes)}  {InstallerName}\n")
            : Ok(new byte[bytes.Length * 4]));

        using var service = new UpdateService(UpdateService.DefaultRepositorySlug, handler);

        await Assert.ThrowsAsync<UpdateVerificationException>(
            () => service.DownloadVerifiedInstallerAsync(Update(bytes.Length), null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Check_ReportsNothingPublishedRatherThanFailingOnA404()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        using var service = new UpdateService(UpdateService.DefaultRepositorySlug, handler);
        var result = await service.CheckAsync(new ReleaseVersion(1, 0, 0), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckOutcome.NoReleasesPublished, result.Outcome);
    }

    [Fact]
    public async Task Check_AsksGitHubExactlyOnceAndOnlyForThisRepository()
    {
        var handler = new StubHandler(_ => Ok("""{"tag_name":"v1.0.0","assets":[],"html_url":"https://github.com/x/y"}"""));

        using var service = new UpdateService(UpdateService.DefaultRepositorySlug, handler);
        await service.CheckAsync(new ReleaseVersion(1, 0, 0), TestContext.Current.CancellationToken);

        var requested = Assert.Single(handler.Requested);
        Assert.Equal("https://api.github.com/repos/CourageToChange/LocalPhotoPDF/releases/latest", requested.ToString());
    }
}

/// <summary>
/// Regression tests for the findings of the 2026-08-15 security audit. Each one fails against the
/// code as it stood before that audit, which is the only reason to keep them.
/// </summary>
public sealed class UpdateHardeningTests
{
    [Theory]
    [InlineData("https://api.github.com:9999/repos/x/y/releases/latest")]
    [InlineData("https://github.com:8443/x/y/releases/download/v1/setup.exe")]
    public void IsAllowed_RefusesANonDefaultPort(string url)
    {
        // Uri.Host excludes the port, so an exact host match on its own let any port through.
        // TLS made it hard to abuse, but the allowlist is meant to state exactly where this
        // application will fetch executable code from, and "any port" was not that.
        Assert.False(UpdateManifest.IsAllowed(new Uri(url)));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/x/y/releases/latest")]
    [InlineData("https://objects.githubusercontent.com/a/b")]
    public void IsAllowed_StillAcceptsTheRealEndpoints(string url) =>
        Assert.True(UpdateManifest.IsAllowed(new Uri(url)));

    [Theory]
    [InlineData("https://github.com@evil.example.com/payload.exe")]   // userinfo confusion
    [InlineData("https://evil.example.com/github.com/payload.exe")]   // path, not host
    [InlineData("https://github.com.evil.example.com/payload.exe")]   // suffix confusion
    [InlineData("https://notgithub.com/payload.exe")]
    [InlineData("http://github.com/payload.exe")]                     // scheme downgrade
    [InlineData("https://github.com./payload.exe")]                   // trailing dot
    public void IsAllowed_RefusesTheClassicHostTricks(string url)
    {
        // Pinned as a table so a future refactor that swaps the exact HashSet match for a
        // Contains or EndsWith check fails here rather than in the wild.
        Assert.False(UpdateManifest.IsAllowed(new Uri(url)));
    }

    [Fact]
    public async Task MatchesAsync_IsTrueForAnUntouchedFile()
    {
        var directory = Directory.CreateTempSubdirectory("lpp-hardening-");
        try
        {
            var path = Path.Combine(directory.FullName, "setup.exe");
            var bytes = Encoding.UTF8.GetBytes("the genuine installer");
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            Assert.True(await UpdateService.MatchesAsync(path, digest, TestContext.Current.CancellationToken));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MatchesAsync_IsFalseAfterTheFileIsSwappedPostVerification()
    {
        // THE audit finding. The installer is hashed when it lands, then sits in a directory every
        // same-user process can write to until someone presses Install, possibly hours later. If
        // nothing re-checks, "verified" describes the file that arrived and not the file that runs.
        var directory = Directory.CreateTempSubdirectory("lpp-hardening-");
        try
        {
            var path = Path.Combine(directory.FullName, "setup.exe");
            var genuine = Encoding.UTF8.GetBytes("the genuine installer");
            await File.WriteAllBytesAsync(path, genuine, TestContext.Current.CancellationToken);
            var digest = Convert.ToHexString(SHA256.HashData(genuine)).ToLowerInvariant();

            Assert.True(await UpdateService.MatchesAsync(path, digest, TestContext.Current.CancellationToken));

            // Something else on the machine replaces it after verification succeeded.
            await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("something else entirely"),
                TestContext.Current.CancellationToken);

            Assert.False(await UpdateService.MatchesAsync(path, digest, TestContext.Current.CancellationToken));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MatchesAsync_IsFalseWhenTheFileHasBeenDeleted()
    {
        var directory = Directory.CreateTempSubdirectory("lpp-hardening-");
        var path = Path.Combine(directory.FullName, "gone.exe");
        directory.Delete(recursive: true);

        Assert.False(await UpdateService.MatchesAsync(path, new string('a', 64), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenIfMatches_KeepsTheFileOpenSoItCannotBeSwappedBeforeItRuns()
    {
        // Re-hashing and then closing the file still leaves a gap before Process.Start, and the
        // same-user process that could swap the download before can swap it in that gap too.
        // Holding the handle removes the window instead of narrowing it, so this asserts the
        // property that actually matters: while it is held, nothing else can write to the file.
        var directory = Directory.CreateTempSubdirectory("lpp-hardening-");
        var path = Path.Combine(directory.FullName, "setup.exe");

        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4], TestContext.Current.CancellationToken);
            var digest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))).ToLowerInvariant();

            var handle = await UpdateService.OpenIfMatchesAsync(path, digest, TestContext.Current.CancellationToken);
            Assert.NotNull(handle);

            await using (handle)
            {
                Assert.Throws<IOException>(() =>
                    File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None).Dispose());
            }

            // Once released, the file is writable again, proving the lock came from the handle.
            File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None).Dispose();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task OpenIfMatches_ReturnsNullAndLeaksNoHandleWhenTheFileChanged()
    {
        var directory = Directory.CreateTempSubdirectory("lpp-hardening-");
        var path = Path.Combine(directory.FullName, "setup.exe");

        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4], TestContext.Current.CancellationToken);
            var digest = Convert.ToHexString(SHA256.HashData([1, 2, 3, 4])).ToLowerInvariant();

            await File.WriteAllBytesAsync(path, [9, 9, 9, 9], TestContext.Current.CancellationToken);

            Assert.Null(await UpdateService.OpenIfMatchesAsync(path, digest, TestContext.Current.CancellationToken));

            // A refused file must not be left locked, or the next attempt could not clean it up.
            File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None).Dispose();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class EndlessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                // 1 MB, far past the 64 KB cap the checksum reader claims to enforce.
                Content = new ByteArrayContent(new byte[1024 * 1024]),
            });
    }

    [Fact]
    public async Task DownloadVerifiedInstaller_RefusesAnOversizedChecksumFileWhileReadingIt()
    {
        // The cap used to be applied AFTER ReadAsByteArrayAsync had already pulled the whole body
        // into memory, which enforces nothing. Decompression is on, so a small response could
        // expand far past the cap before anything looked at it.
        using var service = new UpdateService(UpdateService.DefaultRepositorySlug, new EndlessHandler());

        var failure = await Assert.ThrowsAsync<UpdateVerificationException>(() =>
            service.DownloadVerifiedInstallerAsync(
                new AvailableUpdate(
                    new ReleaseVersion(1, 1, 0),
                    new Uri("https://github.com/CourageToChange/LocalPhotoPDF/releases/download/v1.1.0/LocalPhotoPDF-Setup-1.1.0-win-x64.exe"),
                    "LocalPhotoPDF-Setup-1.1.0-win-x64.exe",
                    1000,
                    new Uri("https://github.com/CourageToChange/LocalPhotoPDF/releases/download/v1.1.0/SHA256SUMS.txt"),
                    new Uri("https://github.com/CourageToChange/LocalPhotoPDF/releases/tag/v1.1.0")),
                null,
                TestContext.Current.CancellationToken));

        Assert.Contains("implausibly large", failure.Message, StringComparison.OrdinalIgnoreCase);
    }
}
