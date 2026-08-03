using System.Collections.Concurrent;
using System.Net.NetworkInformation;

namespace Lantern.App.Services;

public sealed class ResolvedDeviceNameClaims
{
    private readonly string controllerHostName;
    private readonly ConcurrentDictionary<string, string> owners =
        new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> rejectedNames = new(StringComparer.OrdinalIgnoreCase);

    public ResolvedDeviceNameClaims(string controllerHostName)
    {
        this.controllerHostName = Normalize(controllerHostName) ?? string.Empty;
    }

    public void Reset(IEnumerable<string>? rejected = null)
    {
        owners.Clear();
        rejectedNames = (rejected ?? [])
            .Select(Normalize)
            .Where(name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool TryClaim(
        PhysicalAddress macAddress,
        string? candidate,
        out string acceptedName)
    {
        return TryClaim(macAddress, candidate, out acceptedName, out _);
    }

    public bool TryClaim(
        PhysicalAddress macAddress,
        string? candidate,
        out string acceptedName,
        out bool isNewClaim)
    {
        ArgumentNullException.ThrowIfNull(macAddress);
        isNewClaim = false;
        acceptedName = Normalize(candidate) ?? string.Empty;
        if (acceptedName.Length == 0 ||
            rejectedNames.Contains(acceptedName) ||
            string.Equals(acceptedName, controllerHostName, StringComparison.OrdinalIgnoreCase))
        {
            acceptedName = string.Empty;
            return false;
        }

        var macKey = macAddress.ToString();
        if (owners.TryAdd(acceptedName, macKey))
        {
            isNewClaim = true;
            return true;
        }

        var owner = owners.GetValueOrDefault(acceptedName);
        if (string.Equals(owner, macKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        acceptedName = string.Empty;
        return false;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
