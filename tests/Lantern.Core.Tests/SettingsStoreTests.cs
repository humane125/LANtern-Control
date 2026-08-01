using Lantern.Core.Settings;

namespace Lantern.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsRulesByNormalizedMac()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new SettingsStore(directory);
            var settings = new AppSettings
            {
                Devices =
                {
                    ["e2:61:19:0d:bd:54"] = new DevicePreferences
                    {
                        Alias = "Living room",
                        LearnedHostName = "POCO-F6",
                        DownloadKiloBytesPerSecond = 500,
                        UploadKiloBytesPerSecond = 100,
                        PauseInternet = true,
                        LastKnownIp = "192.168.31.213",
                    },
                },
            };

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            var saved = Assert.Single(loaded.Devices);
            Assert.Equal("E261190DBD54", saved.Key);
            Assert.Equal("Living room", saved.Value.Alias);
            Assert.Equal("POCO-F6", saved.Value.LearnedHostName);
            Assert.Equal(500, saved.Value.DownloadKiloBytesPerSecond);
            Assert.Equal(100, saved.Value.UploadKiloBytesPerSecond);
            Assert.True(saved.Value.PauseInternet);
            Assert.Equal("192.168.31.213", saved.Value.LastKnownIp);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAndLoad_DropsInvalidLastKnownIp()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new SettingsStore(directory);
            var settings = new AppSettings
            {
                Devices =
                {
                    ["0E4F69CCE4F0"] = new DevicePreferences
                    {
                        LastKnownIp = "not-an-ip",
                    },
                },
            };

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.Null(Assert.Single(loaded.Devices).Value.LastKnownIp);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAndLoad_PersistsNormalizedPerDeviceDomainBlocks()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var blockedDomainsProperty = typeof(AppSettings).GetProperty("BlockedDomains");
            Assert.NotNull(blockedDomainsProperty);
            var settings = new AppSettings();
            var blockedDomains = Assert.IsAssignableFrom<IDictionary<string, List<string>>>(
                blockedDomainsProperty.GetValue(settings));
            blockedDomains["e2:61:19:0d:bd:54"] =
                [" YouTube.COM. ", "youtube.com", "not a domain"];
            var store = new SettingsStore(directory);

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            var loadedDomains = Assert.IsAssignableFrom<IDictionary<string, List<string>>>(
                blockedDomainsProperty.GetValue(loaded));
            var saved = Assert.Single(loadedDomains);
            Assert.Equal("E261190DBD54", saved.Key);
            Assert.Equal(["youtube.com"], saved.Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Load_MalformedJsonReturnsEmptySettings()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "{ definitely not json");
            var store = new SettingsStore(directory);

            var loaded = await store.LoadAsync();

            Assert.Empty(loaded.Devices);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAndLoad_PersistsUpdateCheckPreferences()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var checkedAt = new DateTimeOffset(2026, 8, 1, 12, 30, 0, TimeSpan.FromHours(2));
            var store = new SettingsStore(directory);
            var settings = new AppSettings
            {
                DisableUpdateChecks = true,
                LastUpdateCheckUtc = checkedAt,
            };

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.True(loaded.DisableUpdateChecks);
            Assert.Equal(checkedAt.ToUniversalTime(), loaded.LastUpdateCheckUtc);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"LanternControlTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
