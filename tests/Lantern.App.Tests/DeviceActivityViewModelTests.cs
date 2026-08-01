using Lantern.App.ViewModels;
using Lantern.Core.Networking;
using Xunit;

namespace Lantern.App.Tests;

public sealed class DeviceActivityViewModelTests
{
    [Fact]
    public void DeviceGroup_OrganizesDomainsUnderOneConnectedDevice()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
        var group = new DeviceActivityGroupViewModel(
            "E261190DBD54",
            "Phone",
            "192.168.31.61");
        group.IsExpanded = true;
        var activity = new DeviceActivityViewModel(
            group.MacKey,
            group.DeviceName,
            group.IpAddress,
            "youtube.com",
            DomainObservationSource.Dns,
            observedAt);

        group.AddDomain(activity);
        group.UpdateIdentity("POCO-F6", "192.168.31.213");

        Assert.True(group.HasDomains);
        Assert.Equal("1 domain", group.DomainCountText);
        Assert.Equal("POCO-F6", group.DeviceName);
        Assert.Equal("192.168.31.213", group.IpAddress);
        Assert.True(group.IsExpanded);
        Assert.Same(activity, Assert.Single(group.Domains));

        group.RemoveDomain(activity);

        Assert.False(group.HasDomains);
        Assert.Equal("No domains yet", group.DomainCountText);
    }

    [Fact]
    public void Observe_AggregatesRepeatedVisitsAndRefreshesDeviceIdentity()
    {
        var firstSeen = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
        var activity = new DeviceActivityViewModel(
            "E261190DBD54",
            "Phone",
            "192.168.31.61",
            "youtube.com",
            DomainObservationSource.Dns,
            firstSeen);

        activity.Observe(
            "POCO-F6",
            "192.168.31.213",
            DomainObservationSource.Tls,
            firstSeen.AddSeconds(5));

        Assert.Equal("POCO-F6", activity.DeviceName);
        Assert.Equal("192.168.31.213", activity.IpAddress);
        Assert.Equal("DNS + TLS", activity.SourceLabel);
        Assert.Equal(2, activity.HitCount);
        Assert.Equal(firstSeen.AddSeconds(5), activity.LastSeen);
    }

    [Fact]
    public void BlockingState_ChangesVisitedDomainActionWithoutRemovingActivity()
    {
        var activity = new DeviceActivityViewModel(
            "E261190DBD54",
            "Phone",
            "192.168.31.61",
            "youtube.com",
            DomainObservationSource.Dns,
            DateTimeOffset.Parse("2026-08-01T12:00:00Z"));
        var setBlocked = typeof(DeviceActivityViewModel).GetMethod("SetBlocked");
        var isBlocked = typeof(DeviceActivityViewModel).GetProperty("IsBlocked");
        var canBlock = typeof(DeviceActivityViewModel).GetProperty("CanBlock");
        var blockActionText = typeof(DeviceActivityViewModel).GetProperty("BlockActionText");

        Assert.NotNull(setBlocked);
        Assert.NotNull(isBlocked);
        Assert.NotNull(canBlock);
        Assert.NotNull(blockActionText);
        setBlocked.Invoke(activity, [true]);

        Assert.True((bool)isBlocked.GetValue(activity)!);
        Assert.False((bool)canBlock.GetValue(activity)!);
        Assert.Equal("Blocked", blockActionText.GetValue(activity));
    }
}
