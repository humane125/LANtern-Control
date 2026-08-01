using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using Lantern.Core.Control;

namespace Lantern.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string directory;
    private readonly string settingsPath;

    public SettingsStore(string? directory = null)
    {
        this.directory = directory ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LANternControl");
        settingsPath = Path.Combine(this.directory, "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(settingsPath);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                JsonOptions,
                cancellationToken);
            return Normalize(loaded ?? new AppSettings());
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    Normalize(settings),
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var normalized = new AppSettings();
        foreach (var pair in settings.Devices)
        {
            string mac;
            try
            {
                mac = TrafficPolicy.NormalizeMac(pair.Key);
            }
            catch (FormatException)
            {
                continue;
            }

            normalized.Devices[mac] = new DevicePreferences
            {
                Alias = string.IsNullOrWhiteSpace(pair.Value.Alias)
                    ? null
                    : pair.Value.Alias.Trim(),
                LearnedHostName = string.IsNullOrWhiteSpace(pair.Value.LearnedHostName)
                    ? null
                    : pair.Value.LearnedHostName.Trim(),
                DownloadKiloBytesPerSecond = Math.Max(
                    0,
                    pair.Value.DownloadKiloBytesPerSecond),
                UploadKiloBytesPerSecond = Math.Max(
                    0,
                    pair.Value.UploadKiloBytesPerSecond),
                PauseInternet = pair.Value.PauseInternet,
                LastKnownIp = NormalizeIpv4(pair.Value.LastKnownIp),
            };
        }

        return normalized;
    }

    private static string? NormalizeIpv4(string? value) =>
        IPAddress.TryParse(value, out var address) &&
        address.AddressFamily == AddressFamily.InterNetwork
            ? address.ToString()
            : null;
}
