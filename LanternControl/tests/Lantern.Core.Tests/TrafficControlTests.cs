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
    public void TrafficPolicy_OnlyActiveLimitsRequireArpInterception()
    {
        var policy = new TrafficPolicy();
        policy.SetRule("00:11:22:33:44:01", new TrafficRule(false, 0, 0));
        policy.SetRule("00:11:22:33:44:02", new TrafficRule(false, 100, 0));
        policy.SetRule("00:11:22:33:44:03", new TrafficRule(false, 0, 50));
        policy.SetRule("00:11:22:33:44:04", new TrafficRule(true, 0, 0));

        Assert.False(policy.RequiresInterception("00:11:22:33:44:01"));
        Assert.True(policy.RequiresInterception("00:11:22:33:44:02"));
        Assert.True(policy.RequiresInterception("00:11:22:33:44:03"));
        Assert.True(policy.RequiresInterception("00:11:22:33:44:04"));
        Assert.False(policy.RequiresInterception("00:11:22:33:44:05"));
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

    private sealed class ManualClock
    {
        public double Seconds { get; set; }

        public double Read() => Seconds;
    }
}
