using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shell;
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

                    var scrollBarStyle = Assert.IsType<Style>(app.Resources[typeof(ScrollBar)]);
                    Assert.Contains(
                        scrollBarStyle.Setters,
                        setter => setter is Setter { Property: var property } &&
                                  property == Control.TemplateProperty);

                    var window = new MainWindow();
                    Assert.Equal(WindowStyle.None, window.WindowStyle);
                    Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
                    var windowChrome = Assert.IsType<WindowChrome>(
                        WindowChrome.GetWindowChrome(window));
                    Assert.True(windowChrome.CaptionHeight >= 38);
                    Assert.True(windowChrome.ResizeBorderThickness.Left >= 6);
                    foreach (var name in new[]
                    {
                        "CustomTitleBar",
                        "MinimizeWindowButton",
                        "MaximizeWindowButton",
                        "CloseWindowButton",
                        "Sidebar",
                        "BrandLogo",
                        "OverviewNavButton",
                        "ActivityNavButton",
                        "DomainRulesNavButton",
                        "AdapterStrip",
                        "MetricsPanel",
                        "ConnectedDevicesMetric",
                        "DownloadMetric",
                        "UploadMetric",
                        "ActiveRulesMetric",
                        "ActivitySection",
                        "WebsiteActivitySection",
                        "WebsiteActivityDeviceList",
                        "ClearActivityButton",
                        "DomainRulesSection",
                        "DomainPresetDeviceSelector",
                        "DomainPresetSelector",
                        "ApplyDomainPresetButton",
                        "DomainRuleDeviceSelector",
                        "DomainRuleInput",
                        "AddDomainRuleButton",
                        "DomainPresetRulesList",
                        "DomainRulesList",
                        "DomainRulesEmptyState",
                        "ChartDeviceSummaryText",
                        "DeviceSection",
                    })
                    {
                        Assert.NotNull(window.FindName(name));
                    }

                    var minimizeButton = Assert.IsType<Button>(
                        window.FindName("MinimizeWindowButton"));
                    minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(WindowState.Minimized, window.WindowState);
                    window.WindowState = WindowState.Normal;

                    var maximizeButton = Assert.IsType<Button>(
                        window.FindName("MaximizeWindowButton"));
                    maximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(WindowState.Maximized, window.WindowState);
                    maximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(WindowState.Normal, window.WindowState);

                    Assert.Null(window.FindName("AdapterNavButton"));

                    var adapterStrip = Assert.IsType<Border>(window.FindName("AdapterStrip"));
                    Assert.True(
                        adapterStrip.Margin.Left >= 12,
                        "The network adapter card must not touch the left content border.");

                    var adapterSelector = Assert.IsType<ComboBox>(
                        window.FindName("AdapterSelector"));
                    adapterSelector.ApplyTemplate();
                    var dropDownToggle = Assert.IsType<ToggleButton>(
                        adapterSelector.Template.FindName("DropDownToggle", adapterSelector));
                    dropDownToggle.ApplyTemplate();
                    var comboBorder = Assert.IsType<Border>(
                        dropDownToggle.Template.FindName("ComboBorder", dropDownToggle));
                    Assert.True(
                        comboBorder.Padding.Left >= 12,
                        "The selected adapter text needs a visible inset from its input border. " +
                        $"Selector={adapterSelector.Padding.Left}, " +
                        $"Toggle={dropDownToggle.Padding.Left}, " +
                        $"Border={comboBorder.Padding.Left}.");

                    var localIpBlock = Assert.IsType<StackPanel>(
                        window.FindName("LocalIpBlock"));
                    Assert.True(
                        localIpBlock.Margin.Left >= 16,
                        "The Local IP block needs a visible inset from its column edge.");

                    var websiteDeviceList = Assert.IsType<ItemsControl>(
                        window.FindName("WebsiteActivityDeviceList"));
                    var activityItem = Assert.IsType<Border>(
                        websiteDeviceList.ItemTemplate.LoadContent());
                    var deviceExpander = Assert.IsType<Expander>(activityItem.Child);
                    var expandedBinding = Assert.IsType<Binding>(
                        BindingOperations.GetBinding(
                            deviceExpander,
                            Expander.IsExpandedProperty));
                    Assert.Equal("IsExpanded", expandedBinding.Path.Path);
                    Assert.Equal(BindingMode.TwoWay, expandedBinding.Mode);

                    var overviewSection = Assert.IsType<Grid>(window.FindName("OverviewSection"));
                    var websiteSection = Assert.IsType<Border>(
                        window.FindName("WebsiteActivitySection"));
                    var domainRulesSection = Assert.IsType<Border>(
                        window.FindName("DomainRulesSection"));
                    Assert.Equal(Visibility.Visible, overviewSection.Visibility);
                    Assert.Equal(Visibility.Collapsed, websiteSection.Visibility);
                    Assert.Equal(Visibility.Collapsed, domainRulesSection.Visibility);

                    var activityNav = Assert.IsType<Button>(window.FindName("ActivityNavButton"));
                    activityNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(Visibility.Collapsed, overviewSection.Visibility);
                    Assert.Equal(Visibility.Visible, websiteSection.Visibility);

                    var overviewNav = Assert.IsType<Button>(window.FindName("OverviewNavButton"));
                    overviewNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(Visibility.Visible, overviewSection.Visibility);
                    Assert.Equal(Visibility.Collapsed, websiteSection.Visibility);

                    var domainRulesNav = Assert.IsType<Button>(
                        window.FindName("DomainRulesNavButton"));
                    domainRulesNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(Visibility.Collapsed, overviewSection.Visibility);
                    Assert.Equal(Visibility.Collapsed, websiteSection.Visibility);
                    Assert.Equal(Visibility.Visible, domainRulesSection.Visibility);

                    var presetSelector = Assert.IsType<ComboBox>(
                        window.FindName("DomainPresetSelector"));
                    var presetNames = presetSelector.Items.Cast<object>()
                        .Select(item =>
                            item.GetType().GetProperty("Name")?.GetValue(item) as string ??
                            string.Empty)
                        .ToArray();
                    Assert.Equal(
                        ["YouTube", "Instagram", "Facebook", "Snapchat", "Discord", "Messenger"],
                        presetNames);

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
                    Assert.True(
                        brandLogo.Width >= 48,
                        "The in-app brand mark must use enough of its badge to preserve eye detail.");
                    var irisTexture = Assert.IsType<System.Windows.Shapes.Path>(
                        brandLogo.FindName("RedWatcherIrisTexture"));
                    Assert.True(
                        irisTexture.Data.Bounds.Width >= 55,
                        "The Red Watcher iris must include visible vector texture rather than a flat dot.");
                    Assert.IsType<System.Windows.Shapes.Ellipse>(
                        brandLogo.FindName("RedWatcherInnerIrisRing"));

                    var chart = Assert.IsType<Lantern.App.Controls.LiveTrafficChart>(
                        window.FindName("TrafficChart"));
                    Assert.Equal(TimeSpan.FromHours(1), chart.VisibleDuration);
                    var xaml = File.ReadAllText(Path.Combine(
                        GetProjectRoot(),
                        "src",
                        "Lantern.App",
                        "MainWindow.xaml"));
                    Assert.DoesNotContain("Text=\"LOCAL PROCESSING\"", xaml, StringComparison.Ordinal);
                    Assert.DoesNotContain("Text=\"1 sec live\"", xaml, StringComparison.Ordinal);
                    Assert.DoesNotContain("1 second samples", xaml, StringComparison.Ordinal);
                    Assert.Contains("ItemsSource=\"{Binding DomainPresetRules}\"", xaml, StringComparison.Ordinal);
                    Assert.Contains("Text=\"{Binding PresetName}\"", xaml, StringComparison.Ordinal);
                    Assert.Contains("ItemsSource=\"{Binding Domains}\"", xaml, StringComparison.Ordinal);
                    Assert.Contains("IsExpanded=\"{Binding IsExpanded, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
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

                    var now = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
                    DeviceViewModel CreateDevice(
                        string mac,
                        string ip,
                        string name,
                        double download,
                        bool isGateway)
                    {
                        var snapshot = new DeviceSnapshot(
                            PhysicalAddress.Parse(mac),
                            IPAddress.Parse(ip),
                            name,
                            now,
                            now,
                            download,
                            0);
                        var device = new DeviceViewModel(_ => Task.CompletedTask);
                        device.Initialize(
                            snapshot,
                            null,
                            isGateway,
                            isGateway ? "Gateway — protected" : "Online");
                        return device;
                    }

                    var phoneB = CreateDevice(
                        "0E4F69CCE4F0",
                        "192.168.31.213",
                        "Phone B",
                        10,
                        false);
                    var gateway = CreateDevice(
                        "64644A380A15",
                        "192.168.31.1",
                        "Gateway",
                        10_000,
                        true);
                    var phoneA = CreateDevice(
                        "D2574CDCA5B2",
                        "192.168.31.225",
                        "Phone A",
                        1_000,
                        false);
                    window.Devices.Add(phoneB);
                    window.Devices.Add(gateway);
                    window.Devices.Add(phoneA);

                    var deviceView = CollectionViewSource.GetDefaultView(window.Devices);
                    deviceView.Refresh();
                    Assert.Equal(
                        new[] { "Phone A", "Phone B", "Gateway" },
                        deviceView.Cast<DeviceViewModel>().Select(device => device.DisplayName));

                    phoneB.Update(
                        new DeviceSnapshot(
                            PhysicalAddress.Parse("0E4F69CCE4F0"),
                            IPAddress.Parse("192.168.31.213"),
                            "Phone B",
                            now,
                            now,
                            50_000,
                            0),
                        null);
                    deviceView.Refresh();
                    Assert.Equal(
                        new[] { "Phone A", "Phone B", "Gateway" },
                        deviceView.Cast<DeviceViewModel>().Select(device => device.DisplayName));

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
    public void SetPresence_ShowsOfflineWithoutDiscardingTheDevice()
    {
        var now = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
        var snapshot = new DeviceSnapshot(
            PhysicalAddress.Parse("0E4F69CCE4F0"),
            IPAddress.Parse("192.168.31.213"),
            "POCO-F6",
            now,
            now,
            0,
            0);
        var viewModel = new DeviceViewModel(_ => Task.CompletedTask);
        viewModel.Initialize(snapshot, null, false, "Online");

        viewModel.SetPresence(false);

        Assert.False(viewModel.IsOnline);
        Assert.Equal("Offline", viewModel.ProtectedReason);
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
