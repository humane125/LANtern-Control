using System.Net.NetworkInformation;
using System.Net.Sockets;
using Lantern.Core.Networking;

namespace Lantern.App.Services;

public static class WindowsAdapterService
{
    public static IReadOnlyList<AdapterProfile> GetUsableAdapters()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(
                adapter =>
                    adapter.OperationalStatus == OperationalStatus.Up &&
                    adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback and
                        not NetworkInterfaceType.Tunnel &&
                    adapter.GetPhysicalAddress().GetAddressBytes().Length == 6)
            .Select(TryCreateProfile)
            .Where(profile => profile is not null)
            .Cast<AdapterProfile>()
            .OrderByDescending(IsDefaultPrivateLan)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AdapterProfile? TryCreateProfile(NetworkInterface adapter)
    {
        var properties = adapter.GetIPProperties();
        var address = properties.UnicastAddresses.FirstOrDefault(
            value => value.Address.AddressFamily == AddressFamily.InterNetwork);
        var gateway = properties.GatewayAddresses.FirstOrDefault(
            value =>
                value.Address.AddressFamily == AddressFamily.InterNetwork &&
                !value.Address.Equals(System.Net.IPAddress.Any));
        if (address is null || gateway is null)
        {
            return null;
        }

        return new AdapterProfile(
            adapter.Id,
            adapter.Name,
            adapter.Description,
            address.Address,
            address.PrefixLength,
            gateway.Address,
            adapter.GetPhysicalAddress())
        {
            ConnectionKind = MapConnectionKind(adapter.NetworkInterfaceType),
        };
    }

    public static AdapterConnectionKind MapConnectionKind(
        NetworkInterfaceType interfaceType) =>
        AdapterConnectionKindClassifier.FromNetworkInterfaceType(interfaceType);

    private static bool IsDefaultPrivateLan(AdapterProfile profile)
    {
        var bytes = profile.LocalAddress.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }
}
