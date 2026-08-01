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

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"LanternControlTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
