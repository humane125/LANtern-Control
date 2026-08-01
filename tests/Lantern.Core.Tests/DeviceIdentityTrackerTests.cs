using Lantern.Core.Settings;

namespace Lantern.Core.Tests;

public sealed class DeviceIdentityTrackerTests
{
    [Fact]
    public void Learn_MigratesUniqueSavedProfileWhenPrivateMacChanges()
    {
        var settings = new AppSettings
        {
            Devices =
            {
                ["0E4F69CCE4F0"] = new DevicePreferences
                {
                    Alias = "Omar's phone",
                    LearnedHostName = "POCO-F6",
                    DownloadKiloBytesPerSecond = 100,
                    LastKnownIp = "192.168.31.213",
                },
                ["AA7051600355"] = new DevicePreferences
                {
                    LastKnownIp = "192.168.31.50",
                },
            },
        };

        var result = DeviceIdentityTracker.Learn(
            settings,
            "AA7051600355",
            "POCO-F6",
            "192.168.31.50");

        Assert.Equal("0E4F69CCE4F0", result.PreviousMacKey);
        Assert.False(settings.Devices.ContainsKey("0E4F69CCE4F0"));
        var migrated = settings.Devices["AA7051600355"];
        Assert.Equal("Omar's phone", migrated.Alias);
        Assert.Equal("POCO-F6", migrated.LearnedHostName);
        Assert.Equal(100, migrated.DownloadKiloBytesPerSecond);
        Assert.Equal("192.168.31.50", migrated.LastKnownIp);
    }

    [Fact]
    public void Learn_DoesNotTransferProfileWhenHostNameIsAmbiguous()
    {
        var settings = new AppSettings
        {
            Devices =
            {
                ["0E4F69CCE4F0"] = new DevicePreferences
                {
                    Alias = "First phone",
                    LearnedHostName = "Android",
                },
                ["D2574CDCA5B2"] = new DevicePreferences
                {
                    Alias = "Second phone",
                    LearnedHostName = "Android",
                },
            },
        };

        var result = DeviceIdentityTracker.Learn(
            settings,
            "AA7051600355",
            "Android",
            "192.168.31.50");

        Assert.Null(result.PreviousMacKey);
        Assert.Equal(3, settings.Devices.Count);
        Assert.Null(settings.Devices["AA7051600355"].Alias);
        Assert.Equal("Android", settings.Devices["AA7051600355"].LearnedHostName);
    }

    [Fact]
    public void Learn_UpdatesTheExistingMacWithoutReplacingItsProfile()
    {
        var settings = new AppSettings
        {
            Devices =
            {
                ["0E4F69CCE4F0"] = new DevicePreferences
                {
                    Alias = "My phone",
                    UploadKiloBytesPerSecond = 50,
                },
            },
        };

        var result = DeviceIdentityTracker.Learn(
            settings,
            "0E4F69CCE4F0",
            "POCO-F6",
            "192.168.31.99");

        Assert.Null(result.PreviousMacKey);
        var profile = settings.Devices["0E4F69CCE4F0"];
        Assert.Equal("My phone", profile.Alias);
        Assert.Equal(50, profile.UploadKiloBytesPerSecond);
        Assert.Equal("POCO-F6", profile.LearnedHostName);
        Assert.Equal("192.168.31.99", profile.LastKnownIp);
    }

    [Fact]
    public void Learn_MacChangeWithoutCurrentIpPreservesThePreviousAddressHint()
    {
        var settings = new AppSettings
        {
            Devices =
            {
                ["0E4F69CCE4F0"] = new DevicePreferences
                {
                    LearnedHostName = "POCO-F6",
                    LastKnownIp = "192.168.31.213",
                },
            },
        };

        DeviceIdentityTracker.Learn(
            settings,
            "AA7051600355",
            "POCO-F6",
            null);

        Assert.Equal(
            "192.168.31.213",
            settings.Devices["AA7051600355"].LastKnownIp);
    }
}
