using Lantern.App.ViewModels;
using Xunit;

namespace Lantern.App.Tests;

public sealed class TrafficHistoryTests
{
    [Fact]
    public void Add_KeepsOnlyTheNewestSamples()
    {
        var history = new TrafficHistory(2);
        history.Add(new TrafficSample(DateTimeOffset.UnixEpoch, 10, 1, "A"));
        history.Add(new TrafficSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 20, 2, "B"));
        history.Add(new TrafficSample(DateTimeOffset.UnixEpoch.AddSeconds(2), 30, 3, "C"));

        Assert.Equal(
            new[] { 20D, 30D },
            history.Samples.Select(sample => sample.DownloadBytesPerSecond));
    }

    [Fact]
    public void Clear_RemovesEverySample()
    {
        var history = new TrafficHistory(2);
        history.Add(new TrafficSample(DateTimeOffset.UnixEpoch, 10, 1, "A"));

        history.Clear();

        Assert.Empty(history.Samples);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrafficHistory(0));
    }

    [Fact]
    public void TryAdd_OnlyAcceptsSamplesAtTheConfiguredInterval()
    {
        var history = new TrafficHistory(10);
        var start = DateTimeOffset.UnixEpoch;

        Assert.True(history.TryAdd(new TrafficSample(start, 10, 1, "A"), TimeSpan.FromSeconds(5)));
        Assert.False(history.TryAdd(new TrafficSample(start.AddSeconds(4), 20, 2, "B"), TimeSpan.FromSeconds(5)));
        Assert.True(history.TryAdd(new TrafficSample(start.AddSeconds(5), 30, 3, "C"), TimeSpan.FromSeconds(5)));
        Assert.Equal(2, history.Samples.Count);
    }

    [Fact]
    public void Add_RemovesSamplesOlderThanTheRetentionWindow()
    {
        var history = new TrafficHistory(1_000, TimeSpan.FromHours(1));
        var start = DateTimeOffset.UnixEpoch;
        history.Add(new TrafficSample(start, 10, 1, "A"));

        history.Add(new TrafficSample(start.AddHours(1).AddSeconds(1), 20, 2, "B"));

        var remaining = Assert.Single(history.Samples);
        Assert.Equal("B", remaining.TopDevice);
    }

    [Fact]
    public void TrafficSample_CarriesTheTopDevicesIndividualRates()
    {
        var sample = new TrafficSample(
            DateTimeOffset.UnixEpoch,
            3_000,
            700,
            "POCO-F6",
            2_000,
            400);

        Assert.Equal(2_000, sample.TopDeviceDownloadBytesPerSecond);
        Assert.Equal(400, sample.TopDeviceUploadBytesPerSecond);
    }
}
