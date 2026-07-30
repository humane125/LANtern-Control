using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class LanScanProfileTests
{
    [Fact]
    public void HomeRouterSafe_CapsBroadcastProbesAtTwentyFivePerSecond()
    {
        var profile = LanScanProfile.HomeRouterSafe;

        Assert.True(
            profile.ProbeInterval >= TimeSpan.FromMilliseconds(40),
            $"Probe interval was only {profile.ProbeInterval.TotalMilliseconds} ms.");
    }

    [Fact]
    public void HomeRouterSafe_WaitsAtLeastOneMinuteBetweenAutomaticSweeps()
    {
        var profile = LanScanProfile.HomeRouterSafe;

        Assert.True(
            profile.AutomaticRescanInterval >= TimeSpan.FromMinutes(1),
            $"Automatic rescan interval was only {profile.AutomaticRescanInterval}.");
    }
}
