using Lantern.Core.Services;

namespace Lantern.Core.Tests;

public sealed class ServiceDefinitionCatalogTests
{
    [Theory]
    [InlineData("www.youtube.com", "youtube")]
    [InlineData("discord.com", "discord")]
    [InlineData("media.discordapp.net", "discord")]
    [InlineData("cdninstagram.com", "instagram")]
    [InlineData("open.spotify.com", "spotify")]
    [InlineData("store.steampowered.com", "steam")]
    public void MatchDomain_MatchesExactAndSubdomains(string domain, string expectedId)
    {
        Assert.Equal(expectedId, ServiceDefinitionCatalog.MatchDomain(domain).Id);
    }

    [Fact]
    public void MatchDomain_NormalizesCaseAndTrailingDot()
    {
        Assert.Equal(
            "youtube",
            ServiceDefinitionCatalog.MatchDomain("WWW.YouTube.COM.").Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("example.invalid")]
    [InlineData("notyoutube.com")]
    public void MatchDomain_DoesNotGuessUnknownInfrastructure(string? domain)
    {
        Assert.Equal("other", ServiceDefinitionCatalog.MatchDomain(domain).Id);
    }

    [Fact]
    public void All_ContainsApprovedInitialServicesWithStableUniqueIds()
    {
        var expected = new[]
        {
            "youtube", "discord", "instagram", "facebook", "messenger",
            "snapchat", "tiktok", "netflix", "twitch", "spotify", "steam",
            "epic-games", "xbox", "playstation", "whatsapp", "telegram",
        };

        Assert.Equal(expected, ServiceDefinitionCatalog.All.Select(item => item.Id));
        Assert.Equal(
            ServiceDefinitionCatalog.All.Count,
            ServiceDefinitionCatalog.All.Select(item => item.Id).Distinct().Count());
    }
}
