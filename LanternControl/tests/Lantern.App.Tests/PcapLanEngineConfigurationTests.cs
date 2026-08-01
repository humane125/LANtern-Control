using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class PcapLanEngineConfigurationTests
{
    [Fact]
    public void ProbeInterval_MatchesThePreviouslyWorkingScanner()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(10), PcapLanEngine.ProbeInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(800), PcapLanEngine.ProbeReplyWindow);
    }

    [Fact]
    public void AutomaticMaintenance_DoesNotContinuouslySweepTheSubnet()
    {
        Assert.False(PassiveDiscoveryProfile.Default.ProbeSubnetOnRefresh);
        Assert.Equal(TimeSpan.FromSeconds(5), PassiveDiscoveryProfile.Default.RefreshInterval);
    }
}
