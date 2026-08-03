using System.Net;
using System.Text;
using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class GitHubUpdateCheckerTests
{
    [Theory]
    [InlineData(false, null, true)]
    [InlineData(false, 23, false)]
    [InlineData(false, 24, true)]
    [InlineData(true, null, false)]
    public void ShouldCheck_EnforcesDailyCadenceAndPermanentOptOut(
        bool disabled,
        int? hoursSinceLastCheck,
        bool expected)
    {
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var lastCheck = hoursSinceLastCheck is { } hours
            ? now.AddHours(-hours)
            : (DateTimeOffset?)null;

        var result = GitHubUpdateChecker.ShouldCheck(disabled, lastCheck, now);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task CheckAsync_ReturnsNewerReleaseAndOfficialPage()
    {
        using var client = CreateClient(HttpStatusCode.OK, """
            {
              "tag_name": "v0.4.0",
              "html_url": "https://github.com/humane125/LANtern-Control/releases/tag/v0.4.0",
              "name": "LANtern Control v0.4.0",
              "draft": false,
              "prerelease": false,
              "assets": []
            }
            """);
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckAsync(
            new Version(0, 3, 29, 0),
            UpdatePlatform.WindowsX64);

        Assert.NotNull(result);
        Assert.Equal(new Version(0, 4, 0, 0), result.LatestVersion);
        Assert.Equal(
            new Uri("https://github.com/humane125/LANtern-Control/releases/tag/v0.4.0"),
            result.ReleasePage);
    }

    [Fact]
    public async Task CheckAsync_TreatsThreePartMatchingTagAsCurrentVersion()
    {
        using var client = CreateClient(HttpStatusCode.OK, """
            {
              "tag_name": "v0.3.29",
              "html_url": "https://github.com/humane125/LANtern-Control/releases/tag/v0.3.29",
              "name": "LANtern Control v0.3.29",
              "draft": false,
              "prerelease": false,
              "assets": []
            }
            """);
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckAsync(
            new Version(0, 3, 29, 0),
            UpdatePlatform.WindowsX64);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("not-a-version", "https://github.com/humane125/LANtern-Control/releases/latest")]
    [InlineData("v9.0.0", "https://example.com/not-lantern")]
    public async Task CheckAsync_RejectsInvalidReleaseData(string tag, string page)
    {
        using var client = CreateClient(HttpStatusCode.OK, $$"""
            {
              "tag_name": "{{tag}}",
              "html_url": "{{page}}",
              "name": "Release",
              "draft": false,
              "prerelease": false,
              "assets": []
            }
            """);
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckAsync(
            new Version(0, 3, 29, 0),
            UpdatePlatform.WindowsX64);

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckAsync_NetworkFailureDoesNotEscapeIntoStartup()
    {
        using var client = CreateClient(HttpStatusCode.ServiceUnavailable, "service unavailable");
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckAsync(
            new Version(0, 3, 29, 0),
            UpdatePlatform.WindowsX64);

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckAsync_LinuxUsesItsManifestVersionAndAsset()
    {
        const string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        using var client = CreateManifestClient(
            """
            {
              "tag_name": "v0.4.0",
              "html_url": "https://github.com/humane125/LANtern-Control/releases/tag/v0.4.0",
              "assets": [
                {
                  "name": "lantern-update-manifest.json",
                  "browser_download_url": "https://github.com/humane125/LANtern-Control/releases/download/v0.4.0/lantern-update-manifest.json"
                },
                {
                  "name": "LANtern-Control-v0.3.1-linux-x86_64-beta.AppImage",
                  "browser_download_url": "https://github.com/humane125/LANtern-Control/releases/download/v0.4.0/LANtern-Control-v0.3.1-linux-x86_64-beta.AppImage",
                  "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                }
              ]
            }
            """,
            $$"""
            {
              "schemaVersion": 1,
              "platforms": {
                "linux-x64": {
                  "version": "0.3.1",
                  "asset": "LANtern-Control-v0.3.1-linux-x86_64-beta.AppImage",
                  "sha256": "{{digest}}"
                }
              }
            }
            """);
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckAsync(
            new Version(0, 3, 0, 0),
            UpdatePlatform.LinuxX64);

        Assert.NotNull(result);
        Assert.Equal(new Version(0, 3, 1, 0), result.LatestVersion);
        Assert.Equal("LANtern-Control-v0.3.1-linux-x86_64-beta.AppImage", result.AssetName);
        Assert.Equal(digest, result.Sha256);
    }

    [Fact]
    public async Task CheckAsync_LinuxIgnoresWindowsOnlyRelease()
    {
        using var client = CreateManifestClient("""
            {
              "tag_name": "v0.4.0",
              "html_url": "https://github.com/humane125/LANtern-Control/releases/tag/v0.4.0",
              "assets": [
                {
                  "name": "lantern-update-manifest.json",
                  "browser_download_url": "https://github.com/humane125/LANtern-Control/releases/download/v0.4.0/lantern-update-manifest.json"
                },
                {
                  "name": "LANtern-Control-Setup-v0.4.0-win-x64.msi",
                  "browser_download_url": "https://github.com/humane125/LANtern-Control/releases/download/v0.4.0/LANtern-Control-Setup-v0.4.0-win-x64.msi"
                }
              ]
            }
            """, """
            {
              "schemaVersion": 1,
              "platforms": {
                "windows-x64": {
                  "version": "0.4.0",
                  "asset": "LANtern-Control-Setup-v0.4.0-win-x64.msi",
                  "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                }
              }
            }
            """);
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckAsync(
            new Version(0, 1, 0, 0),
            UpdatePlatform.LinuxX64);

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckAsync_RejectsManifestChecksumThatDisagreesWithGitHubAsset()
    {
        using var client = CreateManifestClient(
            """
            {
              "tag_name": "v0.4.0",
              "html_url": "https://github.com/humane125/LANtern-Control/releases/tag/v0.4.0",
              "assets": [
                {
                  "name": "lantern-update-manifest.json",
                  "browser_download_url": "https://github.com/humane125/LANtern-Control/releases/download/v0.4.0/lantern-update-manifest.json"
                },
                {
                  "name": "LANtern-Control-v0.3.1-linux-x86_64-beta.AppImage",
                  "browser_download_url": "https://github.com/humane125/LANtern-Control/releases/download/v0.4.0/LANtern-Control-v0.3.1-linux-x86_64-beta.AppImage",
                  "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                }
              ]
            }
            """,
            """
            {
              "schemaVersion": 1,
              "platforms": {
                "linux-x64": {
                  "version": "0.3.1",
                  "asset": "LANtern-Control-v0.3.1-linux-x86_64-beta.AppImage",
                  "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                }
              }
            }
            """);
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckAsync(
            new Version(0, 3, 0),
            UpdatePlatform.LinuxX64);

        Assert.Null(result);
    }

    private static HttpClient CreateClient(HttpStatusCode statusCode, string content) =>
        new(new StaticResponseHandler(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        }));

    private static HttpClient CreateManifestClient(string releaseJson, string manifestJson) =>
        new(new RoutingResponseHandler(request =>
        {
            var content = request.RequestUri?.AbsolutePath.EndsWith(
                "/lantern-update-manifest.json",
                StringComparison.OrdinalIgnoreCase) == true
                ? manifestJson
                : releaseJson;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
        }));

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class RoutingResponseHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
