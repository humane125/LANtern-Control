using System.Net.NetworkInformation;

namespace Lantern.Linux.Services;

public static class LinuxCaptureFilter
{
    public static string ForForwarding(PhysicalAddress localMac)
    {
        ArgumentNullException.ThrowIfNull(localMac);
        var address = string.Join(
            ":",
            localMac.GetAddressBytes().Select(value => value.ToString("x2")));
        return $"ip and ether dst {address}";
    }
}
