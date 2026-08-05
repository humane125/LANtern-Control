using Lantern.Core.Control;

namespace Lantern.Core.Settings;

public sealed class AppSettings
{
    public bool DisableUpdateChecks { get; set; }

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public bool SafeModeEnabled { get; set; }

    public bool SuppressWifiSafeModePrompt { get; set; }

    public Dictionary<string, DevicePreferences> Devices { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> BlockedDomains { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> AppliedDomainPresets { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, ServiceTrafficRule>> ServiceLimits { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DevicePreferences
{
    public string? Alias { get; set; }

    public string? LearnedHostName { get; set; }

    public int DownloadKiloBytesPerSecond { get; set; }

    public int UploadKiloBytesPerSecond { get; set; }

    public bool PauseInternet { get; set; }

    public string? LastKnownIp { get; set; }
}
