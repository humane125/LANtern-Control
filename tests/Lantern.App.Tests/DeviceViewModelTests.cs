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
    public void SafeModePrompt_UsesThemedPreferenceChip()
    {
        var xaml = File.ReadAllText(Path.Combine(
            GetProjectRoot(),
            "src",
            "Lantern.App",
            "SafeModePromptWindow.xaml"));

        Assert.Contains("x:Key=\"PromptPreferenceCheckBoxStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DontAskAgainCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Remember this choice on Wi-Fi", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsChecked\" Value=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsMouseOver\" Value=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsKeyboardFocused\" Value=\"True\"", xaml, StringComparison.Ordinal);
    }

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
                        "SidebarVersionText",
                        "BrandLogo",
                        "OverviewNavButton",
                        "ActivityNavButton",
                        "ServiceInspectorNavButton",
                        "DomainRulesNavButton",
                        "AdapterStrip",
                        "MetricsPanel",
                        "ConnectedDevicesMetric",
                        "DownloadMetric",
                        "UploadMetric",
                        "ActiveRulesMetric",
                        "ActivitySection",
                        "WebsiteActivitySection",
                        "ServiceInspectorSection",
                        "ServiceInspectorDeviceList",
                        "ServiceInspectorEmptyState",
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

                    var sidebarVersion = Assert.IsType<TextBlock>(
                        window.FindName("SidebarVersionText"));
                    Assert.Equal("v0.1.3", sidebarVersion.Text);

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
                    var activitySection = Assert.IsType<Border>(
                        window.FindName("ActivitySection"));
                    Assert.Equal(
                        activitySection.Margin.Left,
                        adapterStrip.Margin.Left);

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

                    var serviceDeviceList = Assert.IsType<ItemsControl>(
                        window.FindName("ServiceInspectorDeviceList"));
                    var serviceItem = Assert.IsType<Border>(
                        serviceDeviceList.ItemTemplate.LoadContent());
                    var serviceExpander = Assert.IsType<Expander>(serviceItem.Child);
                    Assert.Same(
                        window.FindResource("ActivityExpanderStyle"),
                        serviceExpander.Style);

                    var overviewSection = Assert.IsType<Grid>(window.FindName("OverviewSection"));
                    var websiteSection = Assert.IsType<Border>(
                        window.FindName("WebsiteActivitySection"));
                    var domainRulesSection = Assert.IsType<Border>(
                        window.FindName("DomainRulesSection"));
                    var serviceInspectorSection = Assert.IsType<Border>(
                        window.FindName("ServiceInspectorSection"));
                    Assert.Equal(Visibility.Visible, overviewSection.Visibility);
                    Assert.Equal(Visibility.Collapsed, websiteSection.Visibility);
                    Assert.Equal(Visibility.Collapsed, serviceInspectorSection.Visibility);
                    Assert.Equal(Visibility.Collapsed, domainRulesSection.Visibility);

                    var activityNav = Assert.IsType<Button>(window.FindName("ActivityNavButton"));
                    activityNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(Visibility.Collapsed, overviewSection.Visibility);
                    Assert.Equal(Visibility.Visible, websiteSection.Visibility);

                    var serviceInspectorNav = Assert.IsType<Button>(
                        window.FindName("ServiceInspectorNavButton"));
                    serviceInspectorNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(Visibility.Collapsed, websiteSection.Visibility);
                    Assert.Equal(Visibility.Visible, serviceInspectorSection.Visibility);

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
                    Assert.Equal(TimeSpan.FromMinutes(10), chart.VisibleDuration);
                    Assert.Equal(238, chart.Height);
                    Assert.True(
                        chart.ClipToBounds,
                        "The traffic chart must clip strokes and markers inside its own drawing surface.");
                    var deviceGrid = Assert.IsType<DataGrid>(window.FindName("DeviceGrid"));
                    Assert.Equal(68, deviceGrid.RowHeight);
                    var deviceColumn = Assert.IsType<DataGridTemplateColumn>(deviceGrid.Columns[0]);
                    var deviceIdentity = Assert.IsType<Grid>(deviceColumn.CellTemplate!.LoadContent());
                    Assert.Equal(new GridLength(36), deviceIdentity.ColumnDefinitions[0].Width);
                    var deviceNameEditor = Assert.IsType<TextBox>(
                        deviceIdentity.FindName("DeviceNameEditor"));
                    Assert.Equal(18, deviceNameEditor.Height);
                    Assert.Equal(18, deviceNameEditor.MinHeight);
                    Assert.Equal(new Thickness(0), deviceNameEditor.Margin);
                    Assert.Equal(new Thickness(0), deviceNameEditor.Padding);
                    var deviceMetadata = Assert.IsType<StackPanel>(
                        deviceIdentity.FindName("DeviceMetadata"));
                    Assert.Equal(new Thickness(0), deviceMetadata.Margin);
                    var macAddress = Assert.IsType<TextBlock>(
                        deviceIdentity.FindName("DeviceMacAddress"));
                    Assert.Equal(9, macAddress.FontSize);
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
                    Assert.Contains("HTTPS content stays encrypted", xaml, StringComparison.Ordinal);
                    Assert.Contains("grouped as Other", xaml, StringComparison.Ordinal);
                    var overviewNavIndex = xaml.IndexOf("x:Name=\"OverviewNavButton\"", StringComparison.Ordinal);
                    var activityNavIndex = xaml.IndexOf("x:Name=\"ActivityNavButton\"", StringComparison.Ordinal);
                    var serviceNavIndex = xaml.IndexOf("x:Name=\"ServiceInspectorNavButton\"", StringComparison.Ordinal);
                    var rulesNavIndex = xaml.IndexOf("x:Name=\"DomainRulesNavButton\"", StringComparison.Ordinal);
                    Assert.True(overviewNavIndex < activityNavIndex);
                    Assert.True(activityNavIndex < serviceNavIndex);
                    Assert.True(serviceNavIndex < rulesNavIndex);
                    chart.Measure(new System.Windows.Size(800, 238));
                    chart.Arrange(new System.Windows.Rect(0, 0, 800, 238));
                    var emptyChartBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        800,
                        238,
                        96,
                        96,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    emptyChartBitmap.Render(chart);
                    var grid = deviceGrid;
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

                    var topStatus = Assert.IsType<TextBlock>(window.FindName("TopStatusText"));
                    var uptime = Assert.IsType<TextBlock>(window.FindName("UptimeText"));
                    topStatus.Text = "Active";
                    uptime.Text = "Active 00:36:10";
                    var refreshTimer = Assert.IsType<System.Windows.Threading.DispatcherTimer>(
                        typeof(MainWindow)
                            .GetField(
                                "refreshTimer",
                                System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.NonPublic)!
                            .GetValue(window));
                    refreshTimer.Start();
                    var applyEngineState = typeof(MainWindow).GetMethod(
                        "ApplyEngineState",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);
                    Assert.NotNull(applyEngineState);
                    applyEngineState.Invoke(window, [false]);

                    Assert.Equal("Idle", topStatus.Text);
                    Assert.Equal("Idle", uptime.Text);
                    Assert.False(refreshTimer.IsEnabled);
                    Assert.Equal(Visibility.Visible, Assert.IsType<Button>(window.FindName("StartButton")).Visibility);
                    Assert.Equal(Visibility.Collapsed, Assert.IsType<Button>(window.FindName("StopButton")).Visibility);

                    var prompt = new SafeModePromptWindow();
                    var promptRoot = Assert.IsAssignableFrom<FrameworkElement>(prompt.Content);
                    promptRoot.Measure(new System.Windows.Size(prompt.Width, prompt.Height));
                    promptRoot.Arrange(new Rect(0, 0, prompt.Width, prompt.Height));
                    promptRoot.UpdateLayout();
                    var preference = Assert.IsType<CheckBox>(
                        prompt.FindName("DontAskAgainCheckBox"));
                    var preferenceBounds = preference
                        .TransformToAncestor(promptRoot)
                        .TransformBounds(new Rect(preference.RenderSize));
                    var firstActionTop = VisualDescendants<Button>(promptRoot)
                        .Select(button => button
                            .TransformToAncestor(promptRoot)
                            .TransformBounds(new Rect(button.RenderSize)).Top)
                        .Min();
                    Assert.True(
                        preferenceBounds.Bottom + 16 <= firstActionTop,
                        $"Preference chip bottom {preferenceBounds.Bottom:F1} overlaps action row top {firstActionTop:F1}.");

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

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in VisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

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
