using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Lantern.App;
using Lantern.App.ViewModels;
using Lantern.Core.Devices;
using Lantern.Core.Settings;
using Xunit;

namespace Lantern.App.Tests;

public sealed class DeviceViewModelTests
{
    [Fact]
    public void ApplicationIcon_ContainsWindowsIconFramesForSmallAndLargeSizes()
    {
        var iconPath = Path.Combine(
            GetProjectRoot(),
            "src",
            "Lantern.App",
            "Assets",
            "RedWatcher.ico");

        Assert.True(File.Exists(iconPath), $"Missing application icon: {iconPath}");
        var bytes = File.ReadAllBytes(iconPath);
        Assert.True(bytes.Length > 6);
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, bytes[..4]);
        Assert.Equal(7, BitConverter.ToUInt16(bytes, 4));
    }

    [Fact]
    public void MainWindow_LoadsBrandingChartAndUsableLimitEditors()
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    var app = System.Windows.Application.Current as App ?? new App();
                    if (app.Resources.Count == 0)
                    {
                        app.InitializeComponent();
                    }

                    SolidColorBrush Brush(string key) =>
                        Assert.IsType<SolidColorBrush>(app.Resources[key]);
                    var background = Brush("WindowBackground").Color;
                    var primaryText = Brush("PrimaryText").Color;
                    var secondaryText = Brush("SecondaryText").Color;
                    var accent = Brush("Accent").Color;
                    var download = Brush("DownloadAccent").Color;
                    var upload = Brush("UploadAccent").Color;
                    var success = Brush("Success").Color;

                    Assert.True(
                        Math.Max(background.R, Math.Max(background.G, background.B)) -
                        Math.Min(background.R, Math.Min(background.G, background.B)) <= 4,
                        "The main canvas must remain neutral near-black rather than blue-tinted.");
                    Assert.True(
                        accent.R > accent.G * 2 && accent.R > accent.B * 2,
                        "The brand accent must be restrained crimson rather than teal or blue.");
                    Assert.True(RelativeLuminance(accent) < 0.25);
                    Assert.True(ContrastRatio(primaryText, background) >= 7);
                    Assert.True(ContrastRatio(secondaryText, background) >= 4.5);
                    Assert.Equal(accent, download);
                    Assert.True(upload.R > upload.B && upload.R > upload.G);
                    Assert.True(success.G > success.R && success.G > success.B);

                    var window = new MainWindow();
                    foreach (var name in new[]
                    {
                        "Sidebar",
                        "BrandLogo",
                        "OverviewNavButton",
                        "ActivityNavButton",
                        "AdapterNavButton",
                        "AdapterStrip",
                        "MetricsPanel",
                        "ConnectedDevicesMetric",
                        "DownloadMetric",
                        "UploadMetric",
                        "ActiveRulesMetric",
                        "ActivitySection",
                        "ChartRangeText",
                        "ChartTopDeviceText",
                        "DeviceSection",
                    })
                    {
                        Assert.NotNull(window.FindName(name));
                    }

                    var brandLogo = Assert.IsType<Lantern.App.Controls.RedWatcherLogo>(
                        window.FindName("BrandLogo"));
                    Assert.Equal(
                        "LANtern Control Red Watcher logo",
                        System.Windows.Automation.AutomationProperties.GetName(brandLogo));
                    var pupil = Assert.IsType<System.Windows.Shapes.Ellipse>(
                        brandLogo.FindName("RedWatcherPupil"));
                    Assert.Equal(
                        System.Windows.Media.Color.FromRgb(2, 2, 3),
                        Assert.IsType<System.Windows.Media.SolidColorBrush>(pupil.Fill).Color);
                    Assert.Null(brandLogo.FindName("RedWatcherCatchlight"));

                    var chart = Assert.IsType<Lantern.App.Controls.LiveTrafficChart>(
                        window.FindName("TrafficChart"));
                    Assert.Equal(TimeSpan.FromHours(1), chart.VisibleDuration);
                    chart.Measure(new System.Windows.Size(800, 238));
                    chart.Arrange(new System.Windows.Rect(0, 0, 800, 238));
                    var emptyChartBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        800,
                        238,
                        96,
                        96,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    emptyChartBitmap.Render(chart);
                    var grid = Assert.IsType<DataGrid>(window.FindName("DeviceGrid"));
                    foreach (var header in new[] { "DOWN LIMIT", "UP LIMIT" })
                    {
                        var column = Assert.IsType<DataGridTemplateColumn>(
                            Assert.Single(grid.Columns, candidate => Equals(candidate.Header, header)));
                        var templateRoot = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(
                            column.CellTemplate!.LoadContent());
                        var editor = Assert.IsType<TextBox>(templateRoot.FindName("LimitEditor"));
                        var binding = Assert.IsType<Binding>(
                            BindingOperations.GetBinding(editor, TextBox.TextProperty));

                        Assert.Equal(
                            UpdateSourceTrigger.PropertyChanged,
                            binding.UpdateSourceTrigger);
                        Assert.True(
                            binding.Delay >= 300,
                            "Limit changes must be debounced so typing 1000 does not apply 1, 10, and 100 first.");
                    }

                    window.Close();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }

    [Fact]
    public void Initialize_RestoresBothSavedBandwidthLimits()
    {
        var now = DateTimeOffset.Parse("2026-07-31T00:00:00Z");
        var snapshot = new DeviceSnapshot(
            PhysicalAddress.Parse("E261190DBD54"),
            IPAddress.Parse("192.168.31.61"),
            "Phone",
            now,
            now,
            0,
            0);
        var preferences = new DevicePreferences
        {
            DownloadKiloBytesPerSecond = 250,
            UploadKiloBytesPerSecond = 75,
        };
        var viewModel = new DeviceViewModel(_ => Task.CompletedTask);

        viewModel.Initialize(snapshot, preferences, false, "Online");

        Assert.Equal(250, viewModel.DownloadLimit);
        Assert.Equal(75, viewModel.UploadLimit);
    }

    [Fact]
    public void Update_ExposesNumericRatesForDashboardAggregation()
    {
        var now = DateTimeOffset.Parse("2026-07-31T00:00:00Z");
        var snapshot = new DeviceSnapshot(
            PhysicalAddress.Parse("0E4F69CCE4F0"),
            IPAddress.Parse("192.168.31.213"),
            "POCO-F6",
            now,
            now,
            1_250,
            375);
        var viewModel = new DeviceViewModel(_ => Task.CompletedTask);

        viewModel.Initialize(snapshot, null, false, "Online");

        Assert.Equal(1_250, viewModel.DownloadBytesPerSecond);
        Assert.Equal(375, viewModel.UploadBytesPerSecond);
    }

    [Fact]
    public void EditableName_SavesManualAliasAndOverridesAutomaticNames()
    {
        var now = DateTimeOffset.Parse("2026-07-31T00:00:00Z");
        var snapshot = new DeviceSnapshot(
            PhysicalAddress.Parse("0E4F69CCE4F0"),
            IPAddress.Parse("192.168.31.213"),
            "POCO-F6",
            now,
            now,
            0,
            0);
        var changes = 0;
        var viewModel = new DeviceViewModel(
            _ =>
            {
                changes++;
                return Task.CompletedTask;
            });
        viewModel.Initialize(snapshot, null, false, "Online");

        viewModel.EditableName = "  Omar's phone  ";
        viewModel.Update(snapshot with { HostName = "Android" }, null);

        Assert.Equal("Omar's phone", viewModel.Alias);
        Assert.Equal("Omar's phone", viewModel.DisplayName);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void HasActiveRule_IsFalseForProtectedDevicesEvenWithSavedLimits()
    {
        var now = DateTimeOffset.Parse("2026-07-31T00:00:00Z");
        var snapshot = new DeviceSnapshot(
            PhysicalAddress.Parse("64644A380A15"),
            IPAddress.Parse("192.168.31.1"),
            "Gateway",
            now,
            now,
            0,
            0);
        var preferences = new DevicePreferences
        {
            DownloadKiloBytesPerSecond = 500,
        };
        var viewModel = new DeviceViewModel(_ => Task.CompletedTask);

        viewModel.Initialize(snapshot, preferences, true, "Gateway — protected");

        Assert.False(viewModel.HasActiveRule);
    }

    private static string GetProjectRoot([CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));

    private static double ContrastRatio(
        System.Windows.Media.Color lighter,
        System.Windows.Media.Color darker)
    {
        var first = RelativeLuminance(lighter);
        var second = RelativeLuminance(darker);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double RelativeLuminance(System.Windows.Media.Color color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255D;
            return normalized <= 0.04045
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) +
               (0.7152 * Channel(color.G)) +
               (0.0722 * Channel(color.B));
    }
}
