namespace Lantern.App.ViewModels;

public sealed record DashboardSummary(
    int ConnectedDevices,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    int ActiveRules,
    string? TopDeviceName,
    double TopDeviceDownloadBytesPerSecond,
    double TopDeviceUploadBytesPerSecond,
    IReadOnlyList<DeviceTrafficSnapshot> DeviceTraffic)
{
    public static DashboardSummary From(IEnumerable<DeviceViewModel> devices)
    {
        var clients = devices.Where(device => device.CanControl).ToArray();
        var topDevice = clients
            .OrderByDescending(device => device.TotalRate)
            .FirstOrDefault();

        return new DashboardSummary(
            clients.Length,
            clients.Sum(device => device.DownloadBytesPerSecond),
            clients.Sum(device => device.UploadBytesPerSecond),
            clients.Count(device => device.HasActiveRule),
            topDevice?.DisplayName,
            topDevice?.DownloadBytesPerSecond ?? 0,
            topDevice?.UploadBytesPerSecond ?? 0,
            clients
                .Select(device => new DeviceTrafficSnapshot(
                    device.DisplayName,
                    device.DownloadBytesPerSecond,
                    device.UploadBytesPerSecond))
                .OrderByDescending(device => device.TotalBytesPerSecond)
                .ToArray());
    }
}
