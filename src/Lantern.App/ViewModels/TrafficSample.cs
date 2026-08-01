namespace Lantern.App.ViewModels;

public sealed record DeviceTrafficSnapshot(
    string DeviceName,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond)
{
    public double TotalBytesPerSecond =>
        DownloadBytesPerSecond + UploadBytesPerSecond;
}

public sealed record TrafficSample(
    DateTimeOffset Timestamp,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    string? TopDevice,
    double TopDeviceDownloadBytesPerSecond = 0,
    double TopDeviceUploadBytesPerSecond = 0,
    IReadOnlyList<DeviceTrafficSnapshot>? DeviceTraffic = null);
