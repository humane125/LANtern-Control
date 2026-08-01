using System.Net;
using System.Net.NetworkInformation;
using Lantern.App.ViewModels;
using Lantern.Core.Devices;
using Lantern.Core.Settings;
using Xunit;

namespace Lantern.App.Tests;

public sealed class DashboardSummaryTests
{
    [Fact]
    public void From_UsesOnlyControllableClientsForDashboardMetrics()
    {
        var gateway = CreateDevice(
            "64644A380A15",
            "192.168.31.1",
            "Gateway",
            9_000,
            3_000,
            isProtected: true,
            new DevicePreferences { DownloadKiloBytesPerSecond = 500 });
        var phone = CreateDevice(
            "0E4F69CCE4F0",
            "192.168.31.213",
            "POCO-F6",
            2_000,
            400,
            isProtected: false,
            new DevicePreferences { UploadKiloBytesPerSecond = 100 });
        var laptop = CreateDevice(
            "345A6063C052",
            "192.168.31.61",
            "Humane",
            800,
            100,
            isProtected: false,
            null);

        var summary = DashboardSummary.From([gateway, phone, laptop]);

        Assert.Equal(2, summary.ConnectedDevices);
        Assert.Equal(2_800, summary.DownloadBytesPerSecond);
        Assert.Equal(500, summary.UploadBytesPerSecond);
        Assert.Equal(1, summary.ActiveRules);
        Assert.Equal("POCO-F6", summary.TopDeviceName);
        Assert.Equal(2_000, summary.TopDeviceDownloadBytesPerSecond);
        Assert.Equal(400, summary.TopDeviceUploadBytesPerSecond);
        Assert.Collection(
            summary.DeviceTraffic,
            device =>
            {
                Assert.Equal("POCO-F6", device.DeviceName);
                Assert.Equal(2_000, device.DownloadBytesPerSecond);
                Assert.Equal(400, device.UploadBytesPerSecond);
            },
            device =>
            {
                Assert.Equal("Humane", device.DeviceName);
                Assert.Equal(800, device.DownloadBytesPerSecond);
                Assert.Equal(100, device.UploadBytesPerSecond);
            });
    }

    [Fact]
    public void From_HandlesAnEmptyDeviceList()
    {
        var summary = DashboardSummary.From([]);

        Assert.Equal(0, summary.ConnectedDevices);
        Assert.Equal(0, summary.DownloadBytesPerSecond);
        Assert.Equal(0, summary.UploadBytesPerSecond);
        Assert.Equal(0, summary.ActiveRules);
        Assert.Null(summary.TopDeviceName);
        Assert.Equal(0, summary.TopDeviceDownloadBytesPerSecond);
        Assert.Equal(0, summary.TopDeviceUploadBytesPerSecond);
        Assert.Empty(summary.DeviceTraffic);
    }

    private static DeviceViewModel CreateDevice(
        string mac,
        string ip,
        string name,
        double download,
        double upload,
        bool isProtected,
        DevicePreferences? preferences)
    {
        var now = DateTimeOffset.Parse("2026-07-31T00:00:00Z");
        var snapshot = new DeviceSnapshot(
            PhysicalAddress.Parse(mac),
            IPAddress.Parse(ip),
            name,
            now,
            now,
            download,
            upload);
        var viewModel = new DeviceViewModel(_ => Task.CompletedTask);
        viewModel.Initialize(
            snapshot,
            preferences,
            isProtected,
            isProtected ? "Gateway — protected" : "Online");
        return viewModel;
    }
}
