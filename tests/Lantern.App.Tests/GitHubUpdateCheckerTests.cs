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

        var result = await checker.CheckAsync(new Version(0, 3, 29, 0));

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

        var result = await checker.CheckAsync(new Version(0, 3, 29, 0));

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

        var result = await checker.CheckAsync(new Version(0, 3, 29, 0));

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckAsync_NetworkFailureDoesNotEscapeIntoStartup()
    {
        using var client = CreateClient(HttpStatusCode.ServiceUnavailable, "service unavailable");
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckAsync(new Version(0, 3, 29, 0));

        Assert.Null(result);
    }

    private static HttpClient CreateClient(HttpStatusCode statusCode, string content) =>
        new(new StaticResponseHandler(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        }));

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
