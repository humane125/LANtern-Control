using Lantern.Core.Control;
using Lantern.Linux.Services;

namespace Lantern.Linux.Tests;

public sealed class LinuxForwardingStrategyTests
{
    [Fact]
    public void UnlimitedTraffic_UsesTheImmediateForwardingPath()
    {
        var rule = new TrafficRule(false, 0, 0);

        Assert.False(LinuxForwardingStrategy.RequiresPacing(
            rule,
            TrafficDirection.Download));
        Assert.False(LinuxForwardingStrategy.RequiresPacing(
            rule,
            TrafficDirection.Upload));
    }

    [Fact]
    public void DownloadLimit_PacesOnlyDownloadTraffic()
    {
        var rule = new TrafficRule(false, 100, 0);

        Assert.True(LinuxForwardingStrategy.RequiresPacing(
            rule,
            TrafficDirection.Download));
        Assert.False(LinuxForwardingStrategy.RequiresPacing(
            rule,
            TrafficDirection.Upload));
    }

    [Fact]
    public void UploadLimit_PacesOnlyUploadTraffic()
    {
        var rule = new TrafficRule(false, 0, 100);

        Assert.False(LinuxForwardingStrategy.RequiresPacing(
            rule,
            TrafficDirection.Download));
        Assert.True(LinuxForwardingStrategy.RequiresPacing(
            rule,
            TrafficDirection.Upload));
    }
}
