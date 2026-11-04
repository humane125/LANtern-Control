using System.Net.NetworkInformation;

namespace Lantern.Linux.Services;

public static class MacHardwareAddress
{
    private static readonly PhysicalAddress UnprivilegedPlaceholder =
        PhysicalAddress.Parse("020000000000");

    public static bool TryParse(string text, out PhysicalAddress address)
    {
        address = PhysicalAddress.None;
        var octets = text.Split(':');
        if (octets.Length != 6 || octets.Any(octet => octet.Length is < 1 or > 2))
        {
            return false;
        }

        var padded = string.Concat(octets.Select(octet => octet.PadLeft(2, '0')));
        return PhysicalAddress.TryParse(padded, out address!);
    }

    public static bool IsUnusable(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length != 6 ||
            bytes.All(value => value == 0) ||
            address.Equals(UnprivilegedPlaceholder);
    }
}
