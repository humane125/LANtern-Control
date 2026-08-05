using System.Net.NetworkInformation;

namespace Lantern.Core.Networking;

public enum AdapterConnectionKind
{
    Unknown,
    Ethernet,
    Wifi,
}

public static class AdapterConnectionKindClassifier
{
    public static AdapterConnectionKind FromNetworkInterfaceType(
        NetworkInterfaceType interfaceType) => interfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => AdapterConnectionKind.Wifi,
            NetworkInterfaceType.Ethernet or
                NetworkInterfaceType.Ethernet3Megabit or
                NetworkInterfaceType.FastEthernetFx or
                NetworkInterfaceType.FastEthernetT or
                NetworkInterfaceType.GigabitEthernet => AdapterConnectionKind.Ethernet,
            _ => AdapterConnectionKind.Unknown,
        };
}
