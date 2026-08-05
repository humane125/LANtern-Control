namespace Lantern.Core.Control;

public sealed record ServiceTrafficRule(
    int DownloadKiloBytesPerSecond,
    int UploadKiloBytesPerSecond)
{
    public ServiceTrafficRule Normalize() =>
        this with
        {
            DownloadKiloBytesPerSecond = Math.Max(0, DownloadKiloBytesPerSecond),
            UploadKiloBytesPerSecond = Math.Max(0, UploadKiloBytesPerSecond),
        };

    public bool IsUnlimited =>
        DownloadKiloBytesPerSecond <= 0 && UploadKiloBytesPerSecond <= 0;
}
