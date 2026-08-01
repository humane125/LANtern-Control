namespace Lantern.App.ViewModels;

public sealed record TrafficSample(
    DateTimeOffset Timestamp,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    string? TopDevice,
    double TopDeviceDownloadBytesPerSecond = 0,
    double TopDeviceUploadBytesPerSecond = 0);
