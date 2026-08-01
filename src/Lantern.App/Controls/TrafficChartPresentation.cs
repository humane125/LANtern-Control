using System.Globalization;
using Lantern.App.ViewModels;

namespace Lantern.App.Controls;

public static class TrafficChartPresentation
{
    public static string BuildHoverText(TrafficSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var lines = new List<string>
        {
            sample.Timestamp.LocalDateTime.ToString("h:mm:ss tt", CultureInfo.CurrentCulture),
            $"Total  \u2193 {DeviceViewModel.FormatRate(sample.DownloadBytesPerSecond)}    " +
            $"\u2191 {DeviceViewModel.FormatRate(sample.UploadBytesPerSecond)}",
        };
        var activeDevices = GetActiveDevices(sample);
        if (activeDevices.Count == 0)
        {
            lines.Add("No active device traffic");
        }
        else
        {
            lines.AddRange(activeDevices.Select(device =>
                $"{device.DeviceName}  " +
                $"\u2193 {DeviceViewModel.FormatRate(device.DownloadBytesPerSecond)}    " +
                $"\u2191 {DeviceViewModel.FormatRate(device.UploadBytesPerSecond)}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildLatestSummary(TrafficSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var count = GetActiveDevices(sample).Count;
        if (count == 0)
        {
            return "No active device traffic";
        }

        var label = count == 1 ? "1 active device" : $"{count} active devices";
        return $"{label}  \u2022  Total \u2193 {DeviceViewModel.FormatRate(sample.DownloadBytesPerSecond)}  " +
               $"\u2191 {DeviceViewModel.FormatRate(sample.UploadBytesPerSecond)}";
    }

    private static IReadOnlyList<DeviceTrafficSnapshot> GetActiveDevices(TrafficSample sample) =>
        (sample.DeviceTraffic ?? Array.Empty<DeviceTrafficSnapshot>())
        .Where(device => device.TotalBytesPerSecond > 0)
        .OrderByDescending(device => device.TotalBytesPerSecond)
        .ToArray();
}
