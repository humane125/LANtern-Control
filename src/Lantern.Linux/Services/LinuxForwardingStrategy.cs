using Lantern.Core.Control;

namespace Lantern.Linux.Services;

public static class LinuxForwardingStrategy
{
    public static bool RequiresPacing(
        TrafficRule rule,
        TrafficDirection direction)
    {
        return RequiresPacing(
            rule,
            new ServiceTrafficRule(0, 0),
            direction);
    }

    public static bool RequiresPacing(
        TrafficRule rule,
        ServiceTrafficRule serviceRule,
        TrafficDirection direction)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(serviceRule);
        var normalized = rule.Normalize();
        var normalizedService = serviceRule.Normalize();
        var deviceLimited = direction == TrafficDirection.Download
            ? normalized.DownloadKiloBytesPerSecond > 0
            : normalized.UploadKiloBytesPerSecond > 0;
        var serviceLimited = direction == TrafficDirection.Download
            ? normalizedService.DownloadKiloBytesPerSecond > 0
            : normalizedService.UploadKiloBytesPerSecond > 0;
        return deviceLimited || serviceLimited;
    }
}
