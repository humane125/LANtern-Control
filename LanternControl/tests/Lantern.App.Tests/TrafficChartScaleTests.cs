using Lantern.App.Controls;
using Lantern.App.ViewModels;
using Xunit;

namespace Lantern.App.Tests;

public sealed class TrafficChartScaleTests
{
    [Fact]
    public void GetMaximum_AddsHeadroomAndNeverReturnsZero()
    {
        var samples = new[]
        {
            new TrafficSample(DateTimeOffset.UnixEpoch, 1_000, 250, null),
        };

        Assert.Equal(1_200D, TrafficChartScale.GetMaximum(samples));
        Assert.Equal(1D, TrafficChartScale.GetMaximum([]));
    }

    [Fact]
    public void GetX_UsesActualTimestampSpacing()
    {
        var start = DateTimeOffset.UnixEpoch;
        var end = start.AddSeconds(20);

        var x = TrafficChartScale.GetX(start.AddSeconds(5), start, end, 400);

        Assert.Equal(100D, x);
    }

    [Fact]
    public void GetX_ClampsSamplesOutsideTheVisibleRange()
    {
        var start = DateTimeOffset.UnixEpoch;
        var end = start.AddSeconds(20);

        Assert.Equal(0D, TrafficChartScale.GetX(start.AddSeconds(-5), start, end, 400));
        Assert.Equal(400D, TrafficChartScale.GetX(end.AddSeconds(5), start, end, 400));
    }

    [Fact]
    public void GetNearestSampleIndex_UsesThePointerAcrossTheFullChartSurface()
    {
        var start = DateTimeOffset.UnixEpoch;
        var end = start.AddHours(1);
        var samples = new[]
        {
            new TrafficSample(start.AddMinutes(30), 1_000, 100, "A"),
            new TrafficSample(start.AddMinutes(55), 2_000, 200, "B"),
            new TrafficSample(end, 3_000, 300, "C"),
        };

        Assert.Equal(0, TrafficChartScale.GetNearestSampleIndex(samples, 295, start, end, 600));
        Assert.Equal(1, TrafficChartScale.GetNearestSampleIndex(samples, 548, start, end, 600));
        Assert.Equal(2, TrafficChartScale.GetNearestSampleIndex(samples, 598, start, end, 600));
    }

    [Fact]
    public void GetWindow_UsesTheFullChartWidthForAvailableHistory()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrafficSample(start, 1_000, 100, "A"),
            new TrafficSample(start.AddMinutes(10), 2_000, 200, "B"),
        };

        var window = TrafficChartScale.GetWindow(samples, TimeSpan.FromHours(1));

        Assert.Equal(start, window.Start);
        Assert.Equal(start.AddMinutes(10), window.End);
        Assert.Equal(0D, TrafficChartScale.GetX(samples[0].Timestamp, window.Start, window.End, 600));
        Assert.Equal(600D, TrafficChartScale.GetX(samples[1].Timestamp, window.Start, window.End, 600));
    }

    [Fact]
    public void GetX_WithOneTimestamp_PlacesTheFirstPointAtTheLeft()
    {
        var timestamp = DateTimeOffset.UnixEpoch;

        Assert.Equal(0D, TrafficChartScale.GetX(timestamp, timestamp, timestamp, 600));
    }

    [Fact]
    public void GetWindow_RollsForwardAfterOneHour()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrafficSample(start, 1_000, 100, "A"),
            new TrafficSample(start.AddMinutes(70), 2_000, 200, "B"),
        };

        var window = TrafficChartScale.GetWindow(samples, TimeSpan.FromHours(1));

        Assert.Equal(start.AddMinutes(10), window.Start);
        Assert.Equal(start.AddMinutes(70), window.End);
    }
}
