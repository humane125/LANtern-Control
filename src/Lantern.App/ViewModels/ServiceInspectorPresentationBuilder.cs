using Lantern.Core.Services;
using Lantern.Core.Settings;

namespace Lantern.App.ViewModels;

public sealed record ServiceDeviceIdentity(string DeviceName, string IpAddress);

public static class ServiceInspectorPresentationBuilder
{
    public static IReadOnlyList<DeviceServiceGroupViewModel> Build(
        IReadOnlyList<ServiceSessionSnapshot> snapshots,
        IReadOnlyDictionary<string, ServiceDeviceIdentity> identities,
        ServiceUsageHistory history,
        DateTimeOffset now,
        ISet<string> expandedMacKeys)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(expandedMacKeys);

        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var todayServices = history.Days.FirstOrDefault(day => day.Date == today)?.Services ?? [];
        var macKeys = snapshots.Select(snapshot => snapshot.MacKey)
            .Concat(todayServices.Select(aggregate => aggregate.MacKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = new List<DeviceServiceGroupViewModel>(macKeys.Length);
        foreach (var macKey in macKeys)
        {
            identities.TryGetValue(macKey, out var identity);
            var currentByService = snapshots
                .Where(snapshot => snapshot.MacKey.Equals(macKey, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(snapshot => snapshot.ServiceId, StringComparer.OrdinalIgnoreCase);
            var historyByService = todayServices
                .Where(aggregate => aggregate.MacKey.Equals(macKey, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(aggregate => aggregate.ServiceId, StringComparer.OrdinalIgnoreCase);
            var serviceIds = currentByService.Keys
                .Concat(historyByService.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var services = serviceIds
                .Select(serviceId => BuildService(
                    currentByService.GetValueOrDefault(serviceId),
                    historyByService.GetValueOrDefault(serviceId)))
                .OrderByDescending(service => service.IsActive)
                .ThenByDescending(service => service.TotalRate)
                .ThenBy(service => service.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            result.Add(new DeviceServiceGroupViewModel(
                macKey,
                identity?.DeviceName ?? $"Device {FormatMac(macKey)}",
                identity?.IpAddress ?? "-",
                services,
                expandedMacKeys.Contains(macKey)));
        }

        return result
            .OrderBy(group => group.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ServiceSessionViewModel BuildService(
        ServiceSessionSnapshot? snapshot,
        ServiceUsageAggregate? history)
    {
        var serviceId = snapshot?.ServiceId ?? history?.ServiceId ?? "other";
        var serviceName = snapshot?.ServiceName ?? history?.ServiceName ?? "Other";
        var currentDownload = snapshot?.DownloadBytes ?? 0;
        var currentUpload = snapshot?.UploadBytes ?? 0;
        var todayDownload = (history?.DownloadBytes ?? 0) + currentDownload;
        var todayUpload = (history?.UploadBytes ?? 0) + currentUpload;
        var lastActivity = snapshot?.LastActivity ?? history?.LastActivity;
        return new ServiceSessionViewModel(
            serviceId,
            serviceName,
            snapshot?.IsActive == true,
            snapshot?.IsActive == true ? "Active" : "Idle",
            DeviceViewModel.FormatRate(snapshot?.DownloadBytesPerSecond ?? 0),
            DeviceViewModel.FormatRate(snapshot?.UploadBytesPerSecond ?? 0),
            FormatBytes(currentDownload),
            FormatBytes(currentUpload),
            FormatBytes(todayDownload),
            FormatBytes(todayUpload),
            FormatDuration(snapshot?.ActiveDuration ?? TimeSpan.Zero),
            FormatConnections(snapshot?.ActiveConnections ?? 0),
            snapshot is null ? "-" : snapshot.FirstSeen.ToLocalTime().ToString("HH:mm:ss"),
            lastActivity?.ToLocalTime().ToString("HH:mm:ss") ?? "-",
            (snapshot?.DownloadBytesPerSecond ?? 0) +
            (snapshot?.UploadBytesPerSecond ?? 0));
    }

    public static string FormatBytes(long byteCount) => byteCount switch
    {
        >= 1_000_000_000 => $"{byteCount / 1_000_000_000D:0.0} GB",
        >= 1_000_000 => $"{byteCount / 1_000_000D:0.0} MB",
        >= 1_000 => $"{byteCount / 1_000D:0.0} KB",
        _ => $"{byteCount} B",
    };

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes:00}m";
        }

        return $"{(int)duration.TotalMinutes}m {duration.Seconds:00}s";
    }

    private static string FormatConnections(int count) =>
        count == 1 ? "1 connection" : $"{count} connections";

    private static string FormatMac(string macKey) =>
        macKey.Length == 12
            ? string.Join(":", Enumerable.Range(0, 6).Select(index => macKey.Substring(index * 2, 2)))
            : macKey;
}
