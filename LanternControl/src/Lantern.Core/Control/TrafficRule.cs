namespace Lantern.Core.Control;

public enum TrafficDirection
{
    Download,
    Upload,
}

public sealed record TrafficRule(
    bool PauseInternet,
    int DownloadKiloBytesPerSecond,
    int UploadKiloBytesPerSecond)
{
    public TrafficRule Normalize() =>
        this with
        {
            DownloadKiloBytesPerSecond = Math.Max(0, DownloadKiloBytesPerSecond),
            UploadKiloBytesPerSecond = Math.Max(0, UploadKiloBytesPerSecond),
        };

    public TrafficRule ForForwardingMode() =>
        Normalize();
}
