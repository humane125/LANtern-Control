using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lantern.App.Services;

public enum UpdatePlatform
{
    WindowsX64,
    LinuxX64,
}

public sealed record UpdateAvailability(
    Version LatestVersion,
    Uri ReleasePage,
    string? AssetName = null,
    string? Sha256 = null);

public sealed class GitHubUpdateChecker(HttpClient httpClient)
{
    private static readonly Uri LatestReleaseEndpoint = new(
        "https://api.github.com/repos/humane125/LANtern-Control/releases/latest");

    public static bool ShouldCheck(
        bool disabled,
        DateTimeOffset? lastCheckUtc,
        DateTimeOffset nowUtc)
    {
        if (disabled)
        {
            return false;
        }

        return lastCheckUtc is null ||
               lastCheckUtc > nowUtc ||
               nowUtc - lastCheckUtc >= TimeSpan.FromHours(24);
    }

    public async Task<UpdateAvailability?> CheckAsync(
        Version installedVersion,
        UpdatePlatform platform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installedVersion);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LANtern-Control", Normalize(installedVersion).ToString(3)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<ReleaseResponse>(
                stream,
                cancellationToken: cancellationToken);
            if (release is null ||
                !TryGetOfficialReleasePage(release.HtmlUrl, out var releasePage))
            {
                return null;
            }

            var manifestAsset = release.Assets?.FirstOrDefault(asset =>
                string.Equals(
                    asset.Name,
                    "lantern-update-manifest.json",
                    StringComparison.OrdinalIgnoreCase));
            if (manifestAsset is null)
            {
                return platform == UpdatePlatform.WindowsX64 &&
                       TryParseVersion(release.TagName, out var legacyVersion) &&
                       legacyVersion > Normalize(installedVersion)
                    ? new UpdateAvailability(legacyVersion, releasePage)
                    : null;
            }

            if (!TryGetOfficialAssetUri(manifestAsset.BrowserDownloadUrl, out var manifestUri))
            {
                return null;
            }

            using var manifestRequest = new HttpRequestMessage(HttpMethod.Get, manifestUri);
            manifestRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue(
                "LANtern-Control",
                Normalize(installedVersion).ToString(3)));
            manifestRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var manifestResponse = await httpClient.SendAsync(
                manifestRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!manifestResponse.IsSuccessStatusCode)
            {
                return null;
            }

            await using var manifestStream =
                await manifestResponse.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(
                manifestStream,
                cancellationToken: cancellationToken);
            var platformKey = platform switch
            {
                UpdatePlatform.WindowsX64 => "windows-x64",
                UpdatePlatform.LinuxX64 => "linux-x64",
                _ => throw new ArgumentOutOfRangeException(nameof(platform)),
            };
            if (manifest is null ||
                manifest.SchemaVersion != 1 ||
                manifest.Platforms is null ||
                !manifest.Platforms.TryGetValue(platformKey, out var platformRelease) ||
                !TryParseVersion(platformRelease.Version, out var platformVersion) ||
                platformVersion <= Normalize(installedVersion) ||
                string.IsNullOrWhiteSpace(platformRelease.Asset) ||
                !TryNormalizeSha256(platformRelease.Sha256, out var sha256))
            {
                return null;
            }

            var releaseAsset = release.Assets?.FirstOrDefault(asset =>
                string.Equals(asset.Name, platformRelease.Asset, StringComparison.Ordinal));
            if (releaseAsset is null ||
                !TryGetOfficialAssetUri(releaseAsset.BrowserDownloadUrl, out _) ||
                !DigestMatchesWhenPresent(releaseAsset.Digest, sha256))
            {
                return null;
            }

            return new UpdateAvailability(
                platformVersion,
                releasePage,
                releaseAsset.Name,
                sha256);
        }
        catch (Exception exception) when (exception is
            HttpRequestException or
            JsonException or
            NotSupportedException or
            TaskCanceledException)
        {
            return null;
        }
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(tag) ||
            !Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    private static bool TryGetOfficialReleasePage(string? value, out Uri page)
    {
        page = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !parsed.AbsolutePath.StartsWith(
                "/humane125/LANtern-Control/releases/",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        page = parsed;
        return true;
    }

    private static bool TryGetOfficialAssetUri(string? value, out Uri assetUri)
    {
        assetUri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !parsed.AbsolutePath.StartsWith(
                "/humane125/LANtern-Control/releases/download/",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        assetUri = parsed;
        return true;
    }

    private static bool TryNormalizeSha256(string? value, out string sha256)
    {
        sha256 = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return sha256.Length == 64 && sha256.All(Uri.IsHexDigit);
    }

    private static bool DigestMatchesWhenPresent(string? digest, string sha256)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return true;
        }

        const string prefix = "sha256:";
        return digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(digest[prefix.Length..], sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static Version Normalize(Version version) => new(
        Math.Max(0, version.Major),
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    private sealed record ReleaseResponse(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] IReadOnlyList<ReleaseAsset>? Assets);

    private sealed record ReleaseAsset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);

    private sealed record UpdateManifest(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("platforms")]
        Dictionary<string, PlatformRelease>? Platforms);

    private sealed record PlatformRelease(
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("asset")] string? Asset,
        [property: JsonPropertyName("sha256")] string? Sha256);
}
