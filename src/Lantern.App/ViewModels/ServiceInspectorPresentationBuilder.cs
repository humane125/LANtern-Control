using Lantern.Core.Services;
using Lantern.Core.Settings;
using Lantern.Core.Control;

namespace Lantern.App.ViewModels;

public sealed record ServiceDeviceIdentity(string DeviceName, string IpAddress);

public static class ServiceInspectorPresentationBuilder
{
    public static Dictionary<string, ServiceDeviceIdentity> BuildRememberedIdentities(
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var ambiguousNames = settings.Devices.Values
            .Select(preferences => Normalize(preferences.LearnedHostName))
            .Where(name => name is not null)
            .Cast<string>()
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var identities = new Dictionary<string, ServiceDeviceIdentity>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.Devices)
        {
            string macKey;
            try
            {
                macKey = TrafficPolicy.NormalizeMac(pair.Key);
            }
            catch (FormatException)
            {
                continue;
            }

            var alias = Normalize(pair.Value.Alias);
            var learnedName = Normalize(pair.Value.LearnedHostName);
            var deviceName = alias ??
                             (learnedName is not null && !ambiguousNames.Contains(learnedName)
                                 ? learnedName
                                 : null);
            if (deviceName is null)
            {
                continue;
            }

            identities[macKey] = new ServiceDeviceIdentity(
                deviceName,
                Normalize(pair.Value.LastKnownIp) ?? "-");
        }

        return identities;
    }

    public static IReadOnlyList<DeviceServiceGroupViewModel> Build(
        IReadOnlyList<ServiceSessionSnapshot> snapshots,
        IReadOnlyDictionary<string, ServiceDeviceIdentity> identities,
        ServiceUsageHistory history,
        DateTimeOffset now,
        ISet<string> expandedMacKeys)
    {
        return Build(
            snapshots,
            identities,
            history,
            now,
            expandedMacKeys,
            new Dictionary<string, Dictionary<string, ServiceTrafficRule>>(),
            null,
            includeCatalog: false);
    }

    public static IReadOnlyList<DeviceServiceGroupViewModel> Build(
        IReadOnlyList<ServiceSessionSnapshot> snapshots,
        IReadOnlyDictionary<string, ServiceDeviceIdentity> identities,
        ServiceUsageHistory history,
        DateTimeOffset now,
        ISet<string> expandedMacKeys,
        IReadOnlyDictionary<string, Dictionary<string, ServiceTrafficRule>> serviceLimits,
        Action<string, string, ServiceTrafficRule>? ruleChanged)
    {
        return Build(
            snapshots,
            identities,
            history,
            now,
            expandedMacKeys,
            serviceLimits,
            ruleChanged,
            includeCatalog: true);
    }

    private static IReadOnlyList<DeviceServiceGroupViewModel> Build(
        IReadOnlyList<ServiceSessionSnapshot> snapshots,
        IReadOnlyDictionary<string, ServiceDeviceIdentity> identities,
        ServiceUsageHistory history,
        DateTimeOffset now,
        ISet<string> expandedMacKeys,
        IReadOnlyDictionary<string, Dictionary<string, ServiceTrafficRule>> serviceLimits,
        Action<string, string, ServiceTrafficRule>? ruleChanged,
        bool includeCatalog)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(expandedMacKeys);
        ArgumentNullException.ThrowIfNull(serviceLimits);

        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var todayServices = history.Days.FirstOrDefault(day => day.Date == today)?.Services ?? [];
        var macKeys = identities.Keys
            .Concat(snapshots.Select(snapshot => snapshot.MacKey))
            .Concat(todayServices.Select(aggregate => aggregate.MacKey))
            .Concat(serviceLimits.Keys)
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
            var configured = serviceLimits.GetValueOrDefault(macKey) ??
                new Dictionary<string, ServiceTrafficRule>(StringComparer.OrdinalIgnoreCase);
            var serviceIds = (includeCatalog
                    ? ServiceDefinitionCatalog.All.Select(service => service.Id)
                    : [])
                .Concat(currentByService.Keys)
                .Concat(historyByService.Keys)
                .Concat(configured.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var services = serviceIds
                .Select(serviceId => BuildService(
                    macKey,
                    serviceId,
                    currentByService.GetValueOrDefault(serviceId),
                    historyByService.GetValueOrDefault(serviceId),
                    configured.GetValueOrDefault(serviceId),
                    ruleChanged))
                .OrderByDescending(service => service.IsConfigured)
                .ThenByDescending(service => service.IsActive)
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
        string macKey,
        string serviceId,
        ServiceSessionSnapshot? snapshot,
        ServiceUsageAggregate? history,
        ServiceTrafficRule? configuredRule,
        Action<string, string, ServiceTrafficRule>? ruleChanged)
    {
        serviceId = snapshot?.ServiceId ?? history?.ServiceId ?? serviceId;
        var definition = ServiceDefinitionCatalog.All.FirstOrDefault(service =>
            service.Id.Equals(serviceId, StringComparison.OrdinalIgnoreCase));
        var serviceName = snapshot?.ServiceName ?? history?.ServiceName ??
            definition?.Name ?? "Other";
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
            FormatDuration(snapshot?.ActiveDuration ?? history?.ActiveDuration ?? TimeSpan.Zero),
            FormatConnections(snapshot?.ActiveConnections ?? 0),
            snapshot is null ? "-" : snapshot.FirstSeen.ToLocalTime().ToString("HH:mm:ss"),
            lastActivity?.ToLocalTime().ToString("HH:mm:ss") ?? "-",
            (snapshot?.DownloadBytesPerSecond ?? 0) +
            (snapshot?.UploadBytesPerSecond ?? 0),
            configuredRule?.DownloadKiloBytesPerSecond ?? 0,
            configuredRule?.UploadKiloBytesPerSecond ?? 0,
            rule => ruleChanged?.Invoke(macKey, serviceId, rule));
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

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
