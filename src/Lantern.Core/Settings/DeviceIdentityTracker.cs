using System.Net;
using System.Net.Sockets;
using Lantern.Core.Control;

namespace Lantern.Core.Settings;

public sealed record DeviceIdentityResult(
    string MacKey,
    string? PreviousMacKey,
    DevicePreferences Preferences);

public static class DeviceIdentityTracker
{
    public static DeviceIdentityResult Learn(
        AppSettings settings,
        string macAddress,
        string hostName,
        string? lastKnownIp)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var macKey = TrafficPolicy.NormalizeMac(macAddress);
        var normalizedHostName = NormalizeHostName(hostName);
        var normalizedIp = NormalizeIpv4(lastKnownIp);
        settings.Devices.TryGetValue(macKey, out var current);

        var matches = settings.Devices
            .Where(pair =>
                !string.Equals(pair.Key, macKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    pair.Value.LearnedHostName,
                    normalizedHostName,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 1 && CanReplaceWithMigratedProfile(current))
        {
            var previous = matches[0];
            settings.Devices.Remove(previous.Key);
            settings.Devices.Remove(macKey);
            previous.Value.LearnedHostName = normalizedHostName;
            previous.Value.LastKnownIp =
                normalizedIp ?? current?.LastKnownIp ?? previous.Value.LastKnownIp;
            settings.Devices[macKey] = previous.Value;
            return new DeviceIdentityResult(macKey, previous.Key, previous.Value);
        }

        current ??= settings.Devices[macKey] = new DevicePreferences();
        current.LearnedHostName = normalizedHostName;
        if (normalizedIp is not null)
        {
            current.LastKnownIp = normalizedIp;
        }

        return new DeviceIdentityResult(macKey, null, current);
    }

    private static bool CanReplaceWithMigratedProfile(DevicePreferences? current) =>
        current is null ||
        (string.IsNullOrWhiteSpace(current.Alias) &&
         string.IsNullOrWhiteSpace(current.LearnedHostName) &&
         current.DownloadKiloBytesPerSecond == 0 &&
         current.UploadKiloBytesPerSecond == 0 &&
         !current.PauseInternet);

    private static string NormalizeHostName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static string? NormalizeIpv4(string? value) =>
        IPAddress.TryParse(value, out var address) &&
        address.AddressFamily == AddressFamily.InterNetwork
            ? address.ToString()
            : null;
}
