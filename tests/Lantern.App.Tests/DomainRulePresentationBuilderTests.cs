using Lantern.App.ViewModels;
using Xunit;

namespace Lantern.App.Tests;

public sealed class DomainRulePresentationBuilderTests
{
    [Fact]
    public void Build_GroupsAppliedPresetAndLeavesOrdinaryDomainsIndividual()
    {
        var blockedDomains = new[]
        {
            "youtube.com",
            "youtu.be",
            "youtube-nocookie.com",
            "googlevideo.com",
            "ytimg.com",
            "youtubei.googleapis.com",
            "youtube.googleapis.com",
            "example.com",
        };

        var presentation = DomainRulePresentationBuilder.Build(
            "E261190DBD54",
            "POCO-F6",
            blockedDomains,
            ["YouTube"]);

        var preset = Assert.Single(presentation.Presets);
        Assert.Equal("YouTube", preset.PresetName);
        Assert.Equal("POCO-F6", preset.DeviceName);
        Assert.Equal("7 blocked domains", preset.DomainCountText);
        Assert.Contains("googlevideo.com", preset.Domains);

        var ordinary = Assert.Single(presentation.IndividualRules);
        Assert.Equal("example.com", ordinary.Domain);
        Assert.Equal("POCO-F6", ordinary.DeviceName);
    }

    [Fact]
    public void Build_DoesNotGroupDomainsWithoutAppliedPresetMetadata()
    {
        var presentation = DomainRulePresentationBuilder.Build(
            "E261190DBD54",
            "POCO-F6",
            ["youtube.com", "youtu.be"],
            []);

        Assert.Empty(presentation.Presets);
        Assert.Equal(2, presentation.IndividualRules.Count);
    }
}
