using Lantern.Core.Control;

namespace Lantern.Linux.Services;

public static class LinuxForwardingStrategy
{
    public static bool RequiresPacing(
        TrafficRule rule,
        TrafficDirection direction)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var normalized = rule.Normalize();
        return direction == TrafficDirection.Download
            ? normalized.DownloadKiloBytesPerSecond > 0
            : normalized.UploadKiloBytesPerSecond > 0;
    }
}
