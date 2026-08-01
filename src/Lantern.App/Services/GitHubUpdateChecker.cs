using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lantern.App.Services;

public sealed record UpdateAvailability(Version LatestVersion, Uri ReleasePage);

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
                !TryParseVersion(release.TagName, out var latestVersion) ||
                !TryGetOfficialReleasePage(release.HtmlUrl, out var releasePage) ||
                latestVersion <= Normalize(installedVersion))
            {
                return null;
            }

            return new UpdateAvailability(latestVersion, releasePage);
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

    private static Version Normalize(Version version) => new(
        Math.Max(0, version.Major),
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    private sealed record ReleaseResponse(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
