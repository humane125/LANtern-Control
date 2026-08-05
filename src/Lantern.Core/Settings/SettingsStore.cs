using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using Lantern.Core.Control;
using Lantern.Core.Services;

namespace Lantern.Core.Settings;

public sealed class SettingsStore
{
    private const int PrimaryLoadAttempts = 6;
    private static readonly TimeSpan PrimaryLoadRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string directory;
    private readonly string settingsPath;
    private readonly string backupPath;

    public SettingsStore(string? directory = null)
    {
        this.directory = directory ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LANternControl");
        settingsPath = Path.Combine(this.directory, "settings.json");
        backupPath = Path.Combine(this.directory, "settings.backup.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var pathLock = GetPathLock();
        await pathLock.WaitAsync(cancellationToken);
        try
        {
            var loaded = await TryLoadAsync(
                    settingsPath,
                    PrimaryLoadAttempts,
                    cancellationToken) ??
                await TryLoadAsync(backupPath, attempts: 1, cancellationToken);
            return Normalize(loaded ?? new AppSettings());
        }
        finally
        {
            pathLock.Release();
        }
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var saveLock = GetPathLock();
        await saveLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
            var backupTemporaryPath = Path.Combine(
                directory,
                $"settings-backup-{Guid.NewGuid():N}.tmp");
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

                File.Copy(temporaryPath, backupTemporaryPath);
                File.Move(backupTemporaryPath, backupPath, overwrite: true);
                File.Move(temporaryPath, settingsPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                if (File.Exists(backupTemporaryPath))
                {
                    File.Delete(backupTemporaryPath);
                }
            }
        }
        finally
        {
            saveLock.Release();
        }
    }

    private SemaphoreSlim GetPathLock() =>
        SaveLocks.GetOrAdd(
            Path.GetFullPath(settingsPath),
            static _ => new SemaphoreSlim(1, 1));

    private static async Task<AppSettings?> TryLoadAsync(
        string path,
        int attempts,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    useAsync: true);
                return await JsonSerializer.DeserializeAsync<AppSettings>(
                    stream,
                    JsonOptions,
                    cancellationToken);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (IOException) when (attempt < attempts)
            {
                await Task.Delay(PrimaryLoadRetryDelay, cancellationToken);
            }
            catch (IOException)
            {
                return null;
            }
        }

        return null;
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var normalized = new AppSettings
        {
            DisableUpdateChecks = settings.DisableUpdateChecks,
            LastUpdateCheckUtc = settings.LastUpdateCheckUtc?.ToUniversalTime(),
            SafeModeEnabled = settings.SafeModeEnabled,
            SuppressWifiSafeModePrompt = settings.SuppressWifiSafeModePrompt,
        };
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

        foreach (var pair in settings.BlockedDomains)
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

            var domains = new List<string>();
            foreach (var candidate in pair.Value ?? [])
            {
                try
                {
                    domains.Add(TrafficPolicy.NormalizeDomain(candidate));
                }
                catch (FormatException)
                {
                }
            }

            var normalizedDomains = domains
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalizedDomains.Count > 0)
            {
                normalized.BlockedDomains[mac] = normalizedDomains;
            }
        }

        foreach (var pair in settings.AppliedDomainPresets)
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

            var presetNames = (pair.Value ?? [])
                .Select(candidate => DomainBlockPresetCatalog.All.FirstOrDefault(preset =>
                    preset.Name.Equals(candidate?.Trim(), StringComparison.OrdinalIgnoreCase))?.Name)
                .Where(name => name is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (presetNames.Count == 0)
            {
                continue;
            }

            normalized.AppliedDomainPresets[mac] = presetNames;
            if (!normalized.BlockedDomains.TryGetValue(mac, out var blockedDomains))
            {
                blockedDomains = [];
                normalized.BlockedDomains[mac] = blockedDomains;
            }

            foreach (var presetName in presetNames)
            {
                var preset = DomainBlockPresetCatalog.All.First(item =>
                    item.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
                foreach (var domain in preset.Domains)
                {
                    if (!blockedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                    {
                        blockedDomains.Add(domain);
                    }
                }
            }

            blockedDomains.Sort(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var pair in settings.ServiceLimits)
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

            var rules = new Dictionary<string, ServiceTrafficRule>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var servicePair in pair.Value ?? [])
            {
                var service = ServiceDefinitionCatalog.All.FirstOrDefault(candidate =>
                    candidate.Id.Equals(
                        servicePair.Key?.Trim(),
                        StringComparison.OrdinalIgnoreCase));
                if (service is null || servicePair.Value is null)
                {
                    continue;
                }

                var rule = servicePair.Value.Normalize();
                if (!rule.IsUnlimited)
                {
                    rules[service.Id] = rule;
                }
            }

            if (rules.Count > 0)
            {
                normalized.ServiceLimits[mac] = rules;
            }
        }

        return normalized;
    }

    private static string? NormalizeIpv4(string? value) =>
        IPAddress.TryParse(value, out var address) &&
        address.AddressFamily == AddressFamily.InterNetwork
            ? address.ToString()
            : null;
}
