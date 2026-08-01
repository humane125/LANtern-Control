namespace Lantern.Core.Settings;

public sealed class AppSettings
{
    public Dictionary<string, DevicePreferences> Devices { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DevicePreferences
{
    public string? Alias { get; set; }

    public int DownloadKiloBytesPerSecond { get; set; }

    public int UploadKiloBytesPerSecond { get; set; }

    public bool PauseInternet { get; set; }

    public string? LastKnownIp { get; set; }
}
