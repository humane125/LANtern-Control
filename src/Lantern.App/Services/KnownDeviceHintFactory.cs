using System.Net;
using System.Net.NetworkInformation;
using Lantern.Core.Settings;

namespace Lantern.App.Services;

public static class KnownDeviceHintFactory
{
    public static IReadOnlyList<KnownDeviceHint> Build(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var ambiguousNames = FindAmbiguousLearnedNames(settings);
        var hints = new List<KnownDeviceHint>();
        foreach (var pair in settings.Devices)
        {
            if (!PhysicalAddress.TryParse(pair.Key, out var macAddress))
            {
                continue;
            }

            IPAddress.TryParse(pair.Value.LastKnownIp, out var lastKnownIp);
            var alias = Normalize(pair.Value.Alias);
            var learnedName = Normalize(pair.Value.LearnedHostName);
            var hostName = alias ??
                           (learnedName is not null && !ambiguousNames.Contains(learnedName)
                               ? learnedName
                               : null);
            hints.Add(new KnownDeviceHint(macAddress, lastKnownIp, hostName));
        }

        return hints;
    }

    public static IReadOnlySet<string> FindAmbiguousLearnedNames(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Devices.Values
            .Select(preferences => Normalize(preferences.LearnedHostName))
            .Where(name => name is not null)
            .Cast<string>()
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
