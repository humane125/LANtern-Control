using System.Net;
using System.Net.NetworkInformation;

namespace Lantern.Core.Networking;

public sealed record AdapterProfile(
    string Id,
    string Name,
    string Description,
    IPAddress LocalAddress,
    int PrefixLength,
    IPAddress GatewayAddress,
    PhysicalAddress LocalMac)
{
    public AdapterConnectionKind ConnectionKind { get; init; } =
        AdapterConnectionKind.Unknown;

    public string Summary => $"{LocalAddress}/{PrefixLength}  •  Gateway {GatewayAddress}";
}
