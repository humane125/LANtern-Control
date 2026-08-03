using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Xml.Linq;
using Lantern.App.Services;
using Lantern.App.ViewModels;
using Lantern.Core.Devices;
using Lantern.Core.Networking;

[assembly: AvaloniaTestApplication(typeof(Lantern.Linux.Tests.TestAppBuilder))]

namespace Lantern.Linux.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .WithInterFont();
}

public sealed class LinuxMainWindowTests
{
    [Fact]
    public void LinuxPalette_UsesTheExactWindowsColorTokens()
    {
        var appXaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "App.axaml"));
        var windowXaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "MainWindow.axaml"));
        var windowCode = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "MainWindow.axaml.cs")) +
            File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "Lantern.Linux", "MainWindow.Demo.cs"));

        Assert.Contains("x:Key=\"WindowBackgroundBrush\">#08090B", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SurfaceBrush\">#101114", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PrimaryTextBrush\">#F4EEF0", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SecondaryTextBrush\">#A89095", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"AccentBrush\">#D72C43", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"UploadAccentBrush\">#9B6670", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SuccessBrush\">#68C08A", appXaml, StringComparison.Ordinal);

        foreach (var legacyColor in new[]
        {
            "#F02D4F", "#806F75", "#BDAFB3", "#A86A77", "#63D09B",
            "#101013", "#0C0C0F", "#30252A",
        })
        {
            Assert.DoesNotContain(legacyColor, windowXaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(legacyColor, windowCode, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void LinuxPreview_SupportsADataRichDemoMode()
    {
        var appCode = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "App.axaml.cs"));
        var windowCode = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "MainWindow.axaml.cs")) +
            File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "Lantern.Linux", "MainWindow.Demo.cs"));

        Assert.Contains("--demo", appCode, StringComparison.Ordinal);
        Assert.Contains("LoadDemoData", windowCode, StringComparison.Ordinal);
        Assert.Contains("Aurora Laptop", windowCode, StringComparison.Ordinal);
        Assert.Contains("Living Room TV", windowCode, StringComparison.Ordinal);
        Assert.Contains("Nova Phone", windowCode, StringComparison.Ordinal);
        Assert.Contains("youtube.com", windowCode, StringComparison.Ordinal);
        Assert.Contains("TrafficSample", windowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxUpdatePrompt_MatchesWindowsPopupContentAndGeometry()
    {
        var promptXaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "UpdatePromptWindow.axaml"));

        Assert.Contains("Width=\"560\"", promptXaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"390\"", promptXaml, StringComparison.Ordinal);
        Assert.Contains("NEW VERSION DETECTED", promptXaml, StringComparison.Ordinal);
        Assert.Contains("A new signal is live.", promptXaml, StringComparison.Ordinal);
        Assert.Contains("Never ask again", promptXaml, StringComparison.Ordinal);
        Assert.Contains("Not now", promptXaml, StringComparison.Ordinal);
        Assert.Contains("View update", promptXaml, StringComparison.Ordinal);
        Assert.Contains("#6E1828", promptXaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InstalledVersionText", promptXaml, StringComparison.Ordinal);
        Assert.Contains("AvailableVersionText", promptXaml, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void LinuxUpdatePrompt_RendersVersionsAndAllThreeChoices()
    {
        var prompt = new UpdatePromptWindow(new Version(0, 1, 0), new Version(0, 1, 3));

        Assert.Equal(560, prompt.Width);
        Assert.Equal(390, prompt.Height);
        Assert.Equal("v0.1.0", prompt.FindControl<TextBlock>("InstalledVersionText")?.Text);
        Assert.Equal("v0.1.3", prompt.FindControl<TextBlock>("AvailableVersionText")?.Text);
        Assert.NotNull(prompt.FindControl<Button>("NeverAskAgainButton"));
        Assert.NotNull(prompt.FindControl<Button>("NotNowButton"));
        Assert.NotNull(prompt.FindControl<Button>("ViewUpdateButton"));
        Assert.Equal(UpdatePromptChoice.NotNow, prompt.Choice);
    }

    [AvaloniaFact]
    public void Sidebar_ShowsTheLinuxAssemblyVersionAtTheBottom()
    {
        var window = new MainWindow();

        var version = Assert.IsType<TextBlock>(
            window.FindControl<TextBlock>("SidebarVersionText"));
        Assert.Equal("v0.1.0 beta", version.Text);
    }

    [AvaloniaFact]
    public void LinuxUpdatePrompt_ButtonsReturnTheirMatchingChoices()
    {
        foreach (var (buttonName, expected) in new[]
        {
            ("NeverAskAgainButton", UpdatePromptChoice.NeverAskAgain),
            ("NotNowButton", UpdatePromptChoice.NotNow),
            ("ViewUpdateButton", UpdatePromptChoice.Update),
        })
        {
            var prompt = new UpdatePromptWindow(new Version(0, 1, 0), new Version(0, 1, 3));
            prompt.Show();
            var button = Assert.IsType<Button>(prompt.FindControl<Button>(buttonName));

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(expected, prompt.Choice);
        }
    }

    [AvaloniaFact]
    public void Window_HasBrandingWindowControlsAndUsefulEmptyStates()
    {
        var window = new MainWindow();

        Assert.NotNull(window.Icon);
        Assert.NotNull(window.FindControl<Control>("BrandLogo"));
        Assert.NotNull(window.FindControl<Button>("MinimizeWindowButton"));
        Assert.NotNull(window.FindControl<Button>("MaximizeWindowButton"));
        Assert.NotNull(window.FindControl<Button>("CloseWindowButton"));
        Assert.NotNull(window.FindControl<Control>("ActivityEmptyState"));
        Assert.NotNull(window.FindControl<Control>("DomainRulesEmptyState"));
    }

    [AvaloniaFact]
    public void Window_CanRenderStyledScrollbarsAndDropdowns()
    {
        var window = new MainWindow();

        window.Show();
        window.Close();
    }

    [AvaloniaFact]
    public void DemoWindow_OpensWithDevicesActivityAndRules()
    {
        var window = new MainWindow(demoMode: true);

        window.Show();

        Assert.Equal(5, window.Devices.Count);
        Assert.True(window.DeviceActivityGroups.Count >= 4);
        Assert.NotEmpty(window.DomainPresetRules);
        Assert.NotEmpty(window.DomainRules);
        Assert.Equal("Aurora Laptop", window.Devices[0].DisplayName);

        window.Close();
    }

    [AvaloniaFact]
    public void ConnectedDeviceWithoutObservedDomains_IsStillListedOnVisitedDomainsPage()
    {
        var window = new MainWindow();
        var device = new DeviceViewModel(_ => Task.CompletedTask);
        var now = DateTimeOffset.UtcNow;
        device.Initialize(
            new DeviceSnapshot(
                PhysicalAddress.Parse("0E4F69CCE4F0"),
                IPAddress.Parse("192.168.31.213"),
                "POCO-F6",
                now,
                now,
                0,
                0),
            null,
            false,
            "Online");
        device.SetPresence(true);
        window.Devices.Add(device);

        var sync = typeof(MainWindow).GetMethod(
            "SyncWebsiteActivityGroups",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(sync);
        sync.Invoke(window, null);
        var group = Assert.Single(window.DeviceActivityGroups);
        Assert.Equal("POCO-F6", group.DeviceName);
        Assert.Equal("No domains yet", group.DomainCountText);

        window.Close();
    }

    [AvaloniaFact]
    public void NewDomainObservation_DoesNotOverrideTheUsersExpandedState()
    {
        var window = new MainWindow();
        var device = new DeviceViewModel(_ => Task.CompletedTask);
        var now = DateTimeOffset.UtcNow;
        var mac = PhysicalAddress.Parse("0E4F69CCE4F0");
        device.Initialize(
            new DeviceSnapshot(
                mac,
                IPAddress.Parse("192.168.31.213"),
                "POCO-F6",
                now,
                now,
                0,
                0),
            null,
            false,
            "Online");
        device.SetPresence(true);
        window.Devices.Add(device);

        var sync = typeof(MainWindow).GetMethod(
            "SyncWebsiteActivityGroups",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var observe = typeof(MainWindow).GetMethod(
            "ObserveDomain",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(sync);
        Assert.NotNull(observe);
        sync.Invoke(window, null);
        var group = Assert.Single(window.DeviceActivityGroups);
        Assert.False(group.IsExpanded);

        observe.Invoke(window, [new DeviceDomainObservedEventArgs(
            mac,
            new DomainObservation(
                "youtube.com",
                DomainObservationSource.Dns,
                IPAddress.Parse("142.250.186.206")),
            now)]);

        Assert.False(group.IsExpanded);
        Assert.Equal("1 domain", group.DomainCountText);
        window.Close();
    }

    [AvaloniaFact]
    public void Navigation_ShowsVisitedDomainsAndDomainRulesPages()
    {
        var window = new MainWindow();
        var activityPage = Assert.IsAssignableFrom<Control>(window.FindControl<Control>("ActivityPage"));
        var rulesPage = Assert.IsAssignableFrom<Control>(window.FindControl<Control>("RulesPage"));
        var activityButton = Assert.IsType<Button>(
            window.FindControl<Button>("ActivityNavButton"));
        var rulesButton = Assert.IsType<Button>(
            window.FindControl<Button>("RulesNavButton"));

        activityButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(activityPage.IsVisible);
        Assert.False(rulesPage.IsVisible);

        rulesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(activityPage.IsVisible);
        Assert.True(rulesPage.IsVisible);
    }

    [Fact]
    public void LinuxLayout_MatchesCurrentWindowsContentDecisions()
    {
        var xaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "MainWindow.axaml"));

        Assert.DoesNotContain("LOCAL PROCESSING", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("No router login required", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("1 sec live", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("1 second samples", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"LINUX\"", xaml, StringComparison.Ordinal);
        Assert.Contains("controls:RedWatcherLogo", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Refresh devices\" IsVisible=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Stop &amp; restore\" IsVisible=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WebsiteActivityCountText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DomainRuleCountText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DomainPresetRulesList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("No connected devices", xaml, StringComparison.Ordinal);
        Assert.Contains("No blocked domains", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Observed traffic", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Device rules\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollBar:vertical /template/ Thumb.lantern-scroll-thumb", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_Track\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"lantern-scroll-thumb\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"4\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ComboBox[IsDropDownOpen=true] /template/ Border#PopupBorder", xaml, StringComparison.Ordinal);
        Assert.Contains("<Style.Animations>", xaml, StringComparison.Ordinal);
        Assert.Contains("ComboBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\" Margin=\"30,0,0,20\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OverviewPage\" RowDefinitions=\"*,Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OverviewContentScroll\" Grid.Row=\"0\" HorizontalScrollBarVisibility=\"Disabled\" VerticalScrollBarVisibility=\"Auto\" AllowAutoHide=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"Transparent\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Border Classes=\"card\" Padding=\"22\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Classes=\"card\" Margin=\"12,0,12,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ActivityGroups\" HorizontalAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ItemsControl.activity-groups > ContentPresenter\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Template\">", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_Popup\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberBinding=\"{Binding Name}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OffContent=\"Paused\" OnContent=\"Paused\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextBox:disabled", xaml, StringComparison.Ordinal);
        Assert.Contains("Border.limit-editor:disabled", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewStatusBar_IsPinnedOutsideTheScrollableContent()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "MainWindow.axaml");
        var document = XDocument.Load(path);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var overviewPage = Assert.Single(
            document.Descendants(),
            element => (string?)element.Attribute(x + "Name") == "OverviewPage");
        var contentScroll = Assert.Single(
            document.Descendants(),
            element => (string?)element.Attribute(x + "Name") == "OverviewContentScroll");
        var statusBar = Assert.Single(
            document.Descendants(),
            element => (string?)element.Attribute(x + "Name") == "OverviewStatusBar");

        Assert.Equal("Grid", overviewPage.Name.LocalName);
        Assert.Same(overviewPage, contentScroll.Parent);
        Assert.Same(overviewPage, statusBar.Parent);
        Assert.Equal("0", (string?)contentScroll.Attribute("Grid.Row"));
        Assert.Equal("1", (string?)statusBar.Attribute("Grid.Row"));
    }

    [Fact]
    public void VisitedDomainsAndRulesFooters_ArePinnedToTheBottomOfTheirCards()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "MainWindow.axaml");
        var document = XDocument.Load(path);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var (pageName, footerName) in new[]
                 {
                     ("ActivityPage", "ActivityPageFooter"),
                     ("RulesPage", "RulesPageFooter"),
                 })
        {
            var page = Assert.Single(
                document.Descendants(),
                element => (string?)element.Attribute(x + "Name") == pageName);
            var footer = Assert.Single(
                document.Descendants(),
                element => (string?)element.Attribute(x + "Name") == footerName);
            var card = Assert.Single(
                page.Elements(),
                element => element.Name.LocalName == "Border" &&
                           ((string?)element.Attribute("Classes"))?.Contains("card", StringComparison.Ordinal) == true);
            var cardGrid = Assert.Single(card.Elements(), element => element.Name.LocalName == "Grid");

            Assert.Equal("Grid", page.Name.LocalName);
            Assert.Same(cardGrid, footer.Parent);
            Assert.Equal("3", (string?)footer.Attribute("Grid.Row"));
            Assert.Contains("*", (string?)cardGrid.Attribute("RowDefinitions"));
        }
    }

    [Fact]
    public void LimitTextConverter_IgnoresIncompleteInputInsteadOfReturningAValidationError()
    {
        var converterType = typeof(MainWindow).Assembly.GetType(
            "Lantern.Linux.Converters.NonNegativeIntegerConverter");
        Assert.NotNull(converterType);
        var converter = Assert.IsAssignableFrom<IValueConverter>(
            Activator.CreateInstance(converterType));

        Assert.Equal(
            1000,
            converter.ConvertBack("1000", typeof(int), null, CultureInfo.InvariantCulture));
        Assert.Same(
            BindingOperations.DoNothing,
            converter.ConvertBack(string.Empty, typeof(int), null, CultureInfo.InvariantCulture));
        Assert.Same(
            BindingOperations.DoNothing,
            converter.ConvertBack("not a number", typeof(int), null, CultureInfo.InvariantCulture));
        Assert.Same(
            BindingOperations.DoNothing,
            converter.ConvertBack("-1", typeof(int), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void LinuxLayout_MatchesWindowsAlignmentAndExpanderStructure()
    {
        var xaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "MainWindow.axaml"));

        Assert.DoesNotContain("MinWidth=\"596\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AdapterSelector\" Margin=\"0,8,0,0\" MinHeight=\"48\" MinWidth=\"360\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LocalIpText\" Text=\"-\" Margin=\"0,6,0,0\" FontSize=\"13\" FontWeight=\"SemiBold\" TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GatewayText\" Text=\"-\" Margin=\"0,6,0,0\" FontSize=\"13\" FontWeight=\"SemiBold\" TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextBox Classes=\"device-name\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"18\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Cursor=\"Arrow\"", xaml, StringComparison.Ordinal);

        Assert.Contains("Style Selector=\"Expander.lantern-expander\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToggleButton Classes=\"expander-toggle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{TemplateBinding Header}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{TemplateBinding IsExpanded}\"", xaml, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(xaml, "Classes=\"lantern-expander\""));
        Assert.Contains("ColumnDefinitions=\"42,1.7*,1.2*,Auto\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxInteractions_KeepExpandersStableAndControlsCentered()
    {
        var xaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "MainWindow.axaml"));

        Assert.Contains("x:Name=\"ExpanderChevronDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExpanderChevronUp\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToggleButton.expander-toggle:checked /template/ Path#ExpanderChevronDown", xaml, StringComparison.Ordinal);
        Assert.Contains("ToggleButton.expander-toggle:checked /template/ Path#ExpanderChevronUp", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsControl.preset-rules > ContentPresenter", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DomainPresetRulesList\" Classes=\"preset-rules\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToggleSurface\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);

        Assert.Contains("Style Selector=\"Button.action\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"HorizontalContentAlignment\" Value=\"Center\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"VerticalContentAlignment\" Value=\"Center\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions=\"2.2*,0.8*,0.8*,Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\" Margin=\"28,0,24,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"2\" Margin=\"28,0,24,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AdapterSelector\" Margin=\"0,8,0,0\" MinHeight=\"48\" MinWidth=\"360\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SavedDomainIcon\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SavedDomainIconCell\" ColumnDefinitions=\"54,*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SavedDomainIconViewbox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Canvas Width=\"20\" Height=\"20\">", xaml, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"ToggleButton.expander-toggle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"ToggleButton.expander-toggle:pressed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ScaleTransform ScaleX=\"1\" ScaleY=\"1\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Transitions />", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid x:Name=\"ActivityPage\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid x:Name=\"RulesPage\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid x:Name=\"ActivityList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer Grid.Row=\"1\" Classes=\"domain-scroll\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"ScrollViewer.domain-scroll\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxTrafficChart_PaintsTheFullControlAsAContinuousHoverTarget()
    {
        var chartCode = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "Controls", "TrafficChart.cs"));

        Assert.Contains(
            "context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));",
            chartCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxTrafficChart_UsesOnlyItsThemedInChartHoverCard()
    {
        var chartCode = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "Controls", "TrafficChart.cs"));

        Assert.Contains("DrawHoverCard(context, sample, x);", chartCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolTip.SetTip(this", chartCode, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxTrafficChart_UsesTheSharedTenMinuteWindow()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lantern.Linux", "Controls", "TrafficChart.cs"));

        Assert.Contains("TimeSpan.FromMinutes(10)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromHours(1)", source, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string text)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(text, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += text.Length;
        }

        return count;
    }

    [AvaloniaFact]
    public void WindowButtons_MinimizeAndToggleMaximize()
    {
        var window = new MainWindow();
        var minimize = Assert.IsType<Button>(
            window.FindControl<Button>("MinimizeWindowButton"));
        var maximize = Assert.IsType<Button>(
            window.FindControl<Button>("MaximizeWindowButton"));

        minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Minimized, window.WindowState);

        window.WindowState = WindowState.Normal;
        maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Maximized, window.WindowState);
        maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Normal, window.WindowState);
    }
}
