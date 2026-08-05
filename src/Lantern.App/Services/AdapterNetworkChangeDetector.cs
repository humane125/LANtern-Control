using Lantern.Core.Networking;

namespace Lantern.App.Services;

public static class AdapterNetworkChangeDetector
{
    public static bool HasChanged(
        AdapterProfile activeProfile,
        IReadOnlyList<AdapterProfile> currentProfiles)
    {
        ArgumentNullException.ThrowIfNull(activeProfile);
        ArgumentNullException.ThrowIfNull(currentProfiles);

        return !currentProfiles.Any(candidate =>
            candidate.Id.Equals(activeProfile.Id, StringComparison.OrdinalIgnoreCase) &&
            candidate.LocalAddress.Equals(activeProfile.LocalAddress) &&
            candidate.GatewayAddress.Equals(activeProfile.GatewayAddress) &&
            candidate.LocalMac.Equals(activeProfile.LocalMac));
    }
}
