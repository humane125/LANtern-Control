using Lantern.App.Controls;
using Lantern.App.ViewModels;
using Xunit;

namespace Lantern.App.Tests;

public sealed class TrafficChartPresentationTests
{
    [Fact]
    public void BuildHoverText_ListsTotalsAndEveryActiveDevice()
    {
        var sample = new TrafficSample(
            DateTimeOffset.Parse("2026-08-01T12:00:00Z"),
            4_000_000,
            300_000,
            "Laptop",
            3_000_000,
            200_000,
            [
                new DeviceTrafficSnapshot("Laptop", 3_000_000, 200_000),
                new DeviceTrafficSnapshot("Phone", 1_000_000, 100_000),
            ]);

        var text = TrafficChartPresentation.BuildHoverText(sample);

        Assert.Contains("Total", text, StringComparison.Ordinal);
        Assert.Contains("Laptop", text, StringComparison.Ordinal);
        Assert.Contains("Phone", text, StringComparison.Ordinal);
        Assert.Contains("4.0 MB/s", text, StringComparison.Ordinal);
        Assert.Contains("3.0 MB/s", text, StringComparison.Ordinal);
        Assert.Contains("1.0 MB/s", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLatestSummary_ReportsAllActiveDevicesInsteadOfOnlyTheTopDevice()
    {
        var sample = new TrafficSample(
            DateTimeOffset.UnixEpoch,
            4_000,
            300,
            "Laptop",
            3_000,
            200,
            [
                new DeviceTrafficSnapshot("Laptop", 3_000, 200),
                new DeviceTrafficSnapshot("Phone", 1_000, 100),
            ]);

        var text = TrafficChartPresentation.BuildLatestSummary(sample);

        Assert.Contains("2 active devices", text, StringComparison.Ordinal);
        Assert.Contains("Total", text, StringComparison.Ordinal);
    }
}
