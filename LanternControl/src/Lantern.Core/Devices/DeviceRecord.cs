using System.Net;
using System.Net.NetworkInformation;

namespace Lantern.Core.Devices;

public sealed record DeviceSnapshot(
    PhysicalAddress MacAddress,
    IPAddress IpAddress,
    string? HostName,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond)
{
    public double TotalBytesPerSecond => DownloadBytesPerSecond + UploadBytesPerSecond;
}

internal sealed class DeviceRecord
{
    public required PhysicalAddress MacAddress { get; init; }

    public required IPAddress IpAddress { get; set; }

    public string? HostName { get; set; }

    public required DateTimeOffset FirstSeen { get; init; }

    public required DateTimeOffset LastSeen { get; set; }

    public long DownloadBytes;

    public long UploadBytes;
}
