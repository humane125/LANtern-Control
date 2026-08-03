using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Lantern.Core.Networking;

namespace Lantern.Linux.Services;

public sealed record LinuxInterfaceSnapshot(
    string Id,
    string Name,
    string Description,
    bool IsUp,
    PhysicalAddress MacAddress,
    IReadOnlyList<(IPAddress Address, int PrefixLength)> Addresses,
    IReadOnlyList<IPAddress> Gateways);

public static class LinuxAdapterService
{
    public static IReadOnlyList<AdapterProfile> GetUsableAdapters() =>
        BuildProfiles(NetworkInterface.GetAllNetworkInterfaces().Select(CreateSnapshot));

    public static IReadOnlyList<AdapterProfile> BuildProfiles(
        IEnumerable<LinuxInterfaceSnapshot> interfaces)
    {
        ArgumentNullException.ThrowIfNull(interfaces);
        return interfaces
            .Where(item => item.IsUp && !item.MacAddress.Equals(PhysicalAddress.None))
            .Select(item =>
            {
                var address = item.Addresses.FirstOrDefault(candidate =>
                    candidate.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(candidate.Address));
                var gateway = item.Gateways.FirstOrDefault(candidate =>
                    candidate.AddressFamily == AddressFamily.InterNetwork &&
                    !candidate.Equals(IPAddress.Any));
                return address.Address is null || gateway is null
                    ? null
                    : new AdapterProfile(
                        item.Id,
                        item.Name,
                        item.Description,
                        address.Address,
                        address.PrefixLength,
                        gateway,
                        item.MacAddress);
            })
            .Where(profile => profile is not null)
            .Cast<AdapterProfile>()
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LinuxInterfaceSnapshot CreateSnapshot(NetworkInterface adapter)
    {
        var properties = adapter.GetIPProperties();
        return new LinuxInterfaceSnapshot(
            adapter.Id,
            adapter.Name,
            adapter.Description,
            adapter.OperationalStatus == OperationalStatus.Up,
            adapter.GetPhysicalAddress(),
            properties.UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => (address.Address, address.PrefixLength))
                .ToArray(),
            properties.GatewayAddresses.Select(gateway => gateway.Address).ToArray());
    }
}
