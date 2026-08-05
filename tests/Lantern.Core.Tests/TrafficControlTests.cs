using System.Net;
using System.Net.NetworkInformation;
using Lantern.Core.Control;
using Lantern.Core.Devices;

namespace Lantern.Core.Tests;

public sealed class TrafficControlTests
{
    [Fact]
    public void TokenBucket_ZeroRateIsUnlimited()
    {
        var clock = new ManualClock();
        var bucket = new TokenBucket(0, clock.Read);

        Assert.True(bucket.TryConsume(10_000_000));
        Assert.True(bucket.TryConsume(10_000_000));
    }

    [Fact]
    public void TokenBucket_AllowsExactBurstThenRejectsAnotherByte()
    {
        var clock = new ManualClock();
        var bucket = new TokenBucket(1_000, clock.Read, burstSeconds: 1);

        Assert.True(bucket.TryConsume(1_000));
        Assert.False(bucket.TryConsume(1));
    }

    [Fact]
    public void TokenBucket_RefillsFromMonotonicClock()
    {
        var clock = new ManualClock();
        var bucket = new TokenBucket(1_000, clock.Read, burstSeconds: 1);
        Assert.True(bucket.TryConsume(1_000));

        clock.Seconds = 0.25;

        Assert.True(bucket.TryConsume(250));
        Assert.False(bucket.TryConsume(1));
    }

    [Fact]
    public void TrafficPolicy_PauseDropsBothDirections()
    {
        var policy = new TrafficPolicy();
        policy.SetRule("E2-61-19-0D-BD-54", new TrafficRule(true, 0, 0));

        Assert.False(policy.ShouldForward("e2:61:19:0d:bd:54", TrafficDirection.Download, 64));
        Assert.False(policy.ShouldForward("e2:61:19:0d:bd:54", TrafficDirection.Upload, 64));
    }

    [Fact]
    public void TrafficPolicy_UnknownDeviceIsUnlimited()
    {
        var policy = new TrafficPolicy();

        Assert.True(policy.ShouldForward("00:11:22:33:44:55", TrafficDirection.Download, 1_500));
    }

    [Fact]
    public void TrafficPolicy_DomainBlocksAreNormalizedAndScopedToOneDevice()
    {
        const string blockedMac = "E2-61-19-0D-BD-54";
        const string otherMac = "00-11-22-33-44-55";
        var policy = new TrafficPolicy();
        var setBlockedDomains = typeof(TrafficPolicy).GetMethod("SetBlockedDomains");
        var shouldBlockDomain = typeof(TrafficPolicy).GetMethod("ShouldBlockDomain");

        Assert.NotNull(setBlockedDomains);
        Assert.NotNull(shouldBlockDomain);
        setBlockedDomains.Invoke(
            policy,
            [blockedMac, new[] { " YouTube.COM. " }]);

        Assert.True((bool)shouldBlockDomain.Invoke(policy, [blockedMac, "youtube.com"])!);
        Assert.True((bool)shouldBlockDomain.Invoke(policy, [blockedMac, "www.youtube.com"])!);
        Assert.False((bool)shouldBlockDomain.Invoke(policy, [blockedMac, "notyoutube.com"])!);
        Assert.False((bool)shouldBlockDomain.Invoke(policy, [otherMac, "youtube.com"])!);
    }

    [Fact]
    public void DomainBlockPresets_CoverRequestedAppsAndTheirServiceHosts()
    {
        var catalogType = typeof(TrafficPolicy).Assembly.GetType(
            "Lantern.Core.Control.DomainBlockPresetCatalog");
        Assert.NotNull(catalogType);
        var allProperty = catalogType.GetProperty("All");
        Assert.NotNull(allProperty);
        var presets = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                allProperty.GetValue(null))
            .Cast<object>()
            .ToArray();
        var names = presets.Select(preset =>
                (string)preset.GetType().GetProperty("Name")!.GetValue(preset)!)
            .ToArray();

        Assert.Equal(
            ["YouTube", "Instagram", "Facebook", "Snapchat", "Discord", "Messenger"],
            names);
        var youtube = presets[0];
        var domains = Assert.IsAssignableFrom<IEnumerable<string>>(
            youtube.GetType().GetProperty("Domains")!.GetValue(youtube));
        var policy = new TrafficPolicy();
        policy.SetBlockedDomains("E261190DBD54", domains);

        Assert.True(policy.ShouldBlockDomain("E261190DBD54", "r5.googlevideo.com"));
        Assert.True(policy.ShouldBlockDomain("E261190DBD54", "youtubei.googleapis.com"));
        Assert.False(policy.ShouldBlockDomain("E261190DBD54", "instagram.com"));

        var instagram = presets[1];
        var instagramDomains = Assert.IsAssignableFrom<IEnumerable<string>>(
            instagram.GetType().GetProperty("Domains")!.GetValue(instagram));
        Assert.Contains("facebook.com", instagramDomains);
        Assert.Contains("fbcdn.net", instagramDomains);
    }

    [Fact]
    public void TrafficPolicy_UnlimitedDevicesUseTwoWayInterceptionForLiveRates()
    {
        var policy = new TrafficPolicy();
        policy.SetRule("00:11:22:33:44:01", new TrafficRule(false, 0, 0));

        Assert.True(policy.RequiresInterception("00:11:22:33:44:01"));
        Assert.Equal(
            InterceptionTargets.Client | InterceptionTargets.Gateway,
            policy.GetInterceptionTargets("00:11:22:33:44:01"));
        Assert.Equal(
            InterceptionTargets.Client | InterceptionTargets.Gateway,
            policy.GetInterceptionTargets("00:11:22:33:44:05"));
    }

    [Fact]
    public void TrafficPolicy_SafeModeInterceptsOnlyDevicesWithEnforceableRules()
    {
        const string unrestricted = "00:11:22:33:44:01";
        const string deviceLimited = "00:11:22:33:44:02";
        const string serviceLimited = "00:11:22:33:44:03";
        const string domainBlocked = "00:11:22:33:44:04";
        var policy = new TrafficPolicy();
        policy.SetRule(unrestricted, new TrafficRule(false, 0, 0));
        policy.SetRule(deviceLimited, new TrafficRule(false, 100, 0));
        policy.SetServiceRule(
            serviceLimited,
            "youtube",
            new ServiceTrafficRule(1_000, 0));
        policy.SetBlockedDomains(domainBlocked, ["youtube.com"]);

        policy.SetSafeMode(true);

        Assert.Equal(InterceptionTargets.None, policy.GetInterceptionTargets(unrestricted));
        Assert.Equal(
            InterceptionTargets.Client | InterceptionTargets.Gateway,
            policy.GetInterceptionTargets(deviceLimited));
        Assert.Equal(
            InterceptionTargets.Client | InterceptionTargets.Gateway,
            policy.GetInterceptionTargets(serviceLimited));
        Assert.Equal(
            InterceptionTargets.Client | InterceptionTargets.Gateway,
            policy.GetInterceptionTargets(domainBlocked));
    }

    [Fact]
    public void TrafficPolicy_DisablingSafeModeRestoresInterceptAllMonitoring()
    {
        const string mac = "00:11:22:33:44:01";
        var policy = new TrafficPolicy();
        policy.SetSafeMode(true);
        Assert.Equal(InterceptionTargets.None, policy.GetInterceptionTargets(mac));

        policy.SetSafeMode(false);

        Assert.Equal(
            InterceptionTargets.Client | InterceptionTargets.Gateway,
            policy.GetInterceptionTargets(mac));
    }

    [Fact]
    public void TrafficPolicy_ZeroServiceRuleRemovesInterceptionRequirement()
    {
        const string mac = "00:11:22:33:44:01";
        var policy = new TrafficPolicy();
        policy.SetSafeMode(true);
        policy.SetServiceRule(mac, "youtube", new ServiceTrafficRule(100, 50));

        policy.SetServiceRule(mac, "youtube", new ServiceTrafficRule(0, 0));

        Assert.Empty(policy.GetServiceRules(mac));
        Assert.Equal(InterceptionTargets.None, policy.GetInterceptionTargets(mac));
    }

    [Fact]
    public void TrafficPolicy_ServiceLimitOperatesInsideHardDeviceCeiling()
    {
        const string mac = "00:11:22:33:44:01";
        var clock = new ManualClock();
        var policy = new TrafficPolicy(clock.Read);
        policy.SetRule(mac, new TrafficRule(false, 2, 0));
        policy.SetServiceRule(mac, "youtube", new ServiceTrafficRule(1, 0));

        Assert.True(policy.ShouldForward(
            mac, "youtube", TrafficDirection.Download, 1_500));
        Assert.True(policy.ShouldForward(
            mac, "spotify", TrafficDirection.Download, 1_500));
        Assert.False(policy.ShouldForward(
            mac, "spotify", TrafficDirection.Download, 1));

        clock.Seconds = 1;

        Assert.True(policy.ShouldForward(
            mac, "youtube", TrafficDirection.Download, 1_000));
        Assert.True(policy.ShouldForward(
            mac, "spotify", TrafficDirection.Download, 1_000));
        Assert.False(policy.ShouldForward(
            mac, "spotify", TrafficDirection.Download, 1));
    }

    [Fact]
    public void TrafficPolicy_RejectedChildDoesNotConsumeParentCapacity()
    {
        const string mac = "00:11:22:33:44:01";
        var policy = new TrafficPolicy(() => 0);
        policy.SetRule(mac, new TrafficRule(false, 2, 0));
        policy.SetServiceRule(mac, "youtube", new ServiceTrafficRule(1, 0));

        Assert.False(policy.ShouldForward(
            mac, "youtube", TrafficDirection.Download, 1_501));

        Assert.True(policy.ShouldForward(
            mac, "spotify", TrafficDirection.Download, 3_000));
    }

    [Theory]
    [InlineData(false, 0, 0, InterceptionTargets.Client | InterceptionTargets.Gateway)]
    [InlineData(true, 0, 0, InterceptionTargets.Client | InterceptionTargets.Gateway)]
    [InlineData(false, 0, 50, InterceptionTargets.Client | InterceptionTargets.Gateway)]
    [InlineData(false, 100, 0, InterceptionTargets.Client | InterceptionTargets.Gateway)]
    [InlineData(false, 100, 50, InterceptionTargets.Client | InterceptionTargets.Gateway)]
    public void TrafficPolicy_UsesTwoWayMonitoringForEveryRule(
        bool pause,
        int downloadLimit,
        int uploadLimit,
        InterceptionTargets expected)
    {
        const string mac = "00:11:22:33:44:55";
        var policy = new TrafficPolicy();
        policy.SetRule(mac, new TrafficRule(pause, downloadLimit, uploadLimit));

        Assert.Equal(expected, policy.GetInterceptionTargets(mac));
    }

    [Theory]
    [InlineData(
        InterceptionTargets.Client | InterceptionTargets.Gateway,
        InterceptionTargets.Client,
        InterceptionTargets.Gateway,
        InterceptionTargets.Client)]
    [InlineData(
        InterceptionTargets.Client,
        InterceptionTargets.None,
        InterceptionTargets.Client,
        InterceptionTargets.None)]
    [InlineData(
        InterceptionTargets.None,
        InterceptionTargets.Client | InterceptionTargets.Gateway,
        InterceptionTargets.None,
        InterceptionTargets.Client | InterceptionTargets.Gateway)]
    public void InterceptionTransition_RestoresRemovedPeersAndPoisonsCurrentPeers(
        InterceptionTargets previous,
        InterceptionTargets current,
        InterceptionTargets expectedRestore,
        InterceptionTargets expectedPoison)
    {
        var transition = InterceptionTransition.Between(previous, current);

        Assert.Equal(expectedRestore, transition.Restore);
        Assert.Equal(expectedPoison, transition.Poison);
    }

    [Fact]
    public void TrafficRule_ForwardingModePreservesRequestedLimits()
    {
        var requested = new TrafficRule(false, 250, 75);

        var forwardingRule = requested.ForForwardingMode();

        Assert.Equal(250, forwardingRule.DownloadKiloBytesPerSecond);
        Assert.Equal(75, forwardingRule.UploadKiloBytesPerSecond);
        Assert.False(forwardingRule.PauseInternet);
    }

    [Fact]
    public void DeviceRegistry_ComputesIndependentUploadAndDownloadRates()
    {
        var start = DateTimeOffset.Parse("2026-07-30T12:00:00Z");
        var registry = new DeviceRegistry(start);
        var mac = PhysicalAddress.Parse("E261190DBD54");
        registry.Observe(IPAddress.Parse("192.168.31.61"), mac, start);
        registry.RecordTraffic(mac, TrafficDirection.Download, 2_000);
        registry.RecordTraffic(mac, TrafficDirection.Upload, 500);

        var snapshot = Assert.Single(registry.TakeSnapshot(start.AddSeconds(2)));

        Assert.Equal(1_000, snapshot.DownloadBytesPerSecond);
        Assert.Equal(250, snapshot.UploadBytesPerSecond);
        Assert.Equal(1_250, snapshot.TotalBytesPerSecond);
    }

    [Fact]
    public void DeviceRegistry_UpdatesIpWithoutCreatingDuplicateMac()
    {
        var start = DateTimeOffset.Parse("2026-07-30T12:00:00Z");
        var registry = new DeviceRegistry(start);
        var mac = PhysicalAddress.Parse("E261190DBD54");
        registry.Observe(IPAddress.Parse("192.168.31.61"), mac, start);
        registry.Observe(IPAddress.Parse("192.168.31.99"), mac, start.AddSeconds(1));

        var snapshot = Assert.Single(registry.TakeSnapshot(start.AddSeconds(2)));

        Assert.Equal(IPAddress.Parse("192.168.31.99"), snapshot.IpAddress);
    }

    [Fact]
    public void DeviceRegistry_RememberingCachedNeighborDoesNotRefreshLastSeen()
    {
        var start = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
        var registry = new DeviceRegistry(start);
        var mac = PhysicalAddress.Parse("0E4F69CCE4F0");
        registry.Observe(IPAddress.Parse("192.168.31.213"), mac, start);

        registry.Remember(IPAddress.Parse("192.168.31.213"), mac, start.AddMinutes(5));

        var snapshot = Assert.Single(registry.Peek());
        Assert.Equal(start, snapshot.LastSeen);
    }

    [Fact]
    public void DeviceRegistry_NewCachedNeighborRemainsUnconfirmedUntilObserved()
    {
        var start = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
        var registry = new DeviceRegistry(start);
        var mac = PhysicalAddress.Parse("D2574CDCA5B2");
        var address = IPAddress.Parse("192.168.31.225");

        registry.Remember(address, mac, start);

        var cached = Assert.Single(registry.Peek());
        Assert.Equal(DateTimeOffset.MinValue, cached.LastSeen);

        registry.Observe(address, mac, start.AddSeconds(1));

        var confirmed = Assert.Single(registry.Peek());
        Assert.Equal(start.AddSeconds(1), confirmed.LastSeen);
    }

    [Fact]
    public void DeviceRegistry_TrafficRefreshesLastSeen()
    {
        var start = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
        var trafficAt = start.AddSeconds(10);
        var registry = new DeviceRegistry(start);
        var mac = PhysicalAddress.Parse("0E4F69CCE4F0");
        registry.Observe(IPAddress.Parse("192.168.31.213"), mac, start);

        registry.RecordTraffic(mac, TrafficDirection.Download, 100, trafficAt);

        var snapshot = Assert.Single(registry.Peek());
        Assert.Equal(trafficAt, snapshot.LastSeen);
    }

    [Fact]
    public void DeviceRegistry_BeginSessionRemovesStaleNamesAndAddresses()
    {
        var registry = new DeviceRegistry();
        registry.Observe(
            IPAddress.Parse("192.168.31.213"),
            PhysicalAddress.Parse("0E4F69CCE4F0"),
            DateTimeOffset.UtcNow,
            "Humane");

        registry.BeginSession();

        Assert.Empty(registry.Peek());
    }

    private sealed class ManualClock
    {
        public double Seconds { get; set; }

        public double Read() => Seconds;
    }
}
