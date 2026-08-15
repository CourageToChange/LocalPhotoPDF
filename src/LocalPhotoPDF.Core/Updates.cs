using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace LocalPhotoPDF.Core;

/// <summary>
/// A three-part version, as used by this project's release tags (<c>v1.2.3</c>).
/// </summary>
/// <remarks>
/// <see cref="System.Version"/> would nearly do, but it treats an omitted field as -1 and happily
/// parses two- and four-part inputs. Release tags here are validated as exactly three parts by the
/// release workflow, so parsing accepts exactly that and rejects everything else. A version we
/// cannot parse must never be treated as "newer".
/// </remarks>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch)
    : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var parts = trimmed.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            // int.TryParse would accept leading signs and whitespace. Release tags are digits only.
            if (parts[i].Length == 0 || parts[i].Length > 9)
            {
                return false;
            }

            foreach (var character in parts[i])
            {
                if (!char.IsAsciiDigit(character))
                {
                    return false;
                }
            }

            numbers[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>
/// The published release that an update check found, once it has been validated.
/// </summary>
public sealed record AvailableUpdate(
    ReleaseVersion Version,
    Uri InstallerUrl,
    string InstallerFileName,
    long InstallerSizeBytes,
    Uri ChecksumsUrl,
    Uri ReleasePageUrl);

/// <summary>
/// Why an update check did not produce an update. Distinguished so the UI can say something
/// truthful rather than a generic failure.
/// </summary>
public enum UpdateCheckOutcome
{
    /// <summary>A newer release is published and its assets validated.</summary>
    UpdateAvailable,

    /// <summary>The newest published release is the version already running, or older.</summary>
    AlreadyUpToDate,

    /// <summary>Nothing has been published yet, or every release was a draft or prerelease.</summary>
    NoReleasesPublished,

    /// <summary>A release exists but does not carry the assets an update needs.</summary>
    ReleaseIsNotUsable,
}

public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, AvailableUpdate? Update)
{
    public static UpdateCheckResult None(UpdateCheckOutcome outcome) => new(outcome, null);
}

/// <summary>
/// Turns the GitHub Releases API response into a validated <see cref="AvailableUpdate"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a pure function over a JSON string so it can be tested exhaustively without
/// a network. The WPF layer of this application has no automated tests at all, so every decision
/// that could send a user to the wrong download lives here, in the tested library, rather than
/// in the view model.
/// </para>
/// <para>
/// 🔒 Every URL used by an update comes from a remote JSON document, so none of them are trusted.
/// Each one is re-checked against <see cref="AllowedHosts"/> before it is returned. Without that,
/// a tampered API response could name any host it liked and the application would fetch, and then
/// run, whatever came back. The download is additionally checksum-verified before execution; see
/// <see cref="UpdateInstaller"/>.
/// </para>
/// </remarks>
public static class UpdateManifest
{
    /// <summary>Hosts an update is permitted to contact. Nothing else is ever fetched.</summary>
    /// <remarks>
    /// Release asset links start on github.com and redirect to githubusercontent.com. Both are
    /// listed because the redirect target is validated too, rather than trusting the hop.
    /// </remarks>
    public static readonly IReadOnlySet<string> AllowedHosts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "api.github.com",
            "github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com",
        };

    /// <summary>The checksum file every release publishes, used to verify the installer.</summary>
    public const string ChecksumFileName = "SHA256SUMS.txt";

    /// <summary>Refuse an installer larger than this. A self-contained build is ~45 MB.</summary>
    public const long MaximumInstallerSizeBytes = 250L * 1024 * 1024;

    /// <remarks>
    /// The port is pinned as well as the host. <see cref="Uri.Host"/> excludes the port, so without
    /// <see cref="Uri.IsDefaultPort"/> a URL like <c>https://api.github.com:9999/…</c> passed this
    /// check. TLS makes that hard to abuse, but the allowlist is supposed to say exactly where this
    /// application will fetch executable code from, and "any port on these hosts" was not it.
    /// </remarks>
    public static bool IsAllowed([NotNullWhen(true)] Uri? uri) =>
        uri is not null
        && uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && AllowedHosts.Contains(uri.Host);

    /// <summary>
    /// Parses the response of <c>GET /repos/{owner}/{repo}/releases/latest</c>.
    /// </summary>
    /// <param name="json">The raw response body.</param>
    /// <param name="currentVersion">The version running now. Only a strictly greater one is offered.</param>
    public static UpdateCheckResult Parse(string json, ReleaseVersion currentVersion)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return UpdateCheckResult.None(UpdateCheckOutcome.ReleaseIsNotUsable);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return UpdateCheckResult.None(UpdateCheckOutcome.ReleaseIsNotUsable);
        }

        // A draft or prerelease is never offered. /releases/latest already excludes both, but the
        // flags are checked anyway so this stays correct if the caller ever passes a different feed.
        if (GetBool(root, "draft") || GetBool(root, "prerelease"))
        {
            return UpdateCheckResult.None(UpdateCheckOutcome.NoReleasesPublished);
        }

        if (!ReleaseVersion.TryParse(GetString(root, "tag_name"), out var version))
        {
            return UpdateCheckResult.None(UpdateCheckOutcome.NoReleasesPublished);
        }

        // Strictly greater. Equal is up to date, and older must never trigger a "downgrade".
        if (version <= currentVersion)
        {
            return UpdateCheckResult.None(UpdateCheckOutcome.AlreadyUpToDate);
        }

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return UpdateCheckResult.None(UpdateCheckOutcome.ReleaseIsNotUsable);
        }

        Uri? installerUrl = null;
        Uri? checksumsUrl = null;
        var installerName = string.Empty;
        var installerSize = 0L;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = GetString(asset, "name");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!Uri.TryCreate(GetString(asset, "browser_download_url"), UriKind.Absolute, out var url)
                || !IsAllowed(url))
            {
                continue;
            }

            if (name.Equals(ChecksumFileName, StringComparison.OrdinalIgnoreCase))
            {
                checksumsUrl = url;
            }
            else if (IsInstallerAssetName(name, version))
            {
                var size = GetLong(asset, "size");
                if (size <= 0 || size > MaximumInstallerSizeBytes)
                {
                    continue;
                }

                installerUrl = url;
                installerName = name;
                installerSize = size;
            }
        }

        if (installerUrl is null || checksumsUrl is null)
        {
            return UpdateCheckResult.None(UpdateCheckOutcome.ReleaseIsNotUsable);
        }

        if (!Uri.TryCreate(GetString(root, "html_url"), UriKind.Absolute, out var pageUrl)
            || !IsAllowed(pageUrl))
        {
            return UpdateCheckResult.None(UpdateCheckOutcome.ReleaseIsNotUsable);
        }

        return new UpdateCheckResult(
            UpdateCheckOutcome.UpdateAvailable,
            new AvailableUpdate(version, installerUrl, installerName, installerSize, checksumsUrl, pageUrl));
    }

    /// <summary>
    /// Matches the installer asset this project publishes, and only for the version being offered.
    /// </summary>
    /// <remarks>
    /// The name is pinned rather than pattern-matched loosely, so a release that also carries the
    /// portable ZIP, the SBOM or a future asset cannot have one of those handed to the installer
    /// launcher by accident.
    /// </remarks>
    public static bool IsInstallerAssetName(string name, ReleaseVersion version) =>
        name.Equals($"LocalPhotoPDF-Setup-{version}-win-x64.exe", StringComparison.OrdinalIgnoreCase);

    private static bool GetBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long GetLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : 0L;
}

/// <summary>
/// Reads a <c>SHA256SUMS.txt</c> file of the form <c>&lt;hex&gt;  &lt;filename&gt;</c>.
/// </summary>
public static class ChecksumFile
{
    /// <summary>
    /// Returns the lowercase hex digest recorded for <paramref name="fileName"/>, or null.
    /// </summary>
    public static string? FindDigest(string contents, string fileName)
    {
        if (string.IsNullOrEmpty(contents) || string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        foreach (var rawLine in contents.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(' ');
            if (separator <= 0)
            {
                continue;
            }

            var digest = line[..separator];
            if (digest.Length != 64 || !IsHex(digest))
            {
                continue;
            }

            // The format writes two spaces for a binary file; a leading '*' is also conventional.
            var name = line[separator..].TrimStart(' ', '*');
            if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return digest.ToLowerInvariant();
            }
        }

        return null;
    }

    private static bool IsHex(string text)
    {
        foreach (var character in text)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
