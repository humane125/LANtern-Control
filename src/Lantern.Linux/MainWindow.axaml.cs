using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Lantern.App.Services;
using Lantern.App.ViewModels;
using Lantern.Core.Control;
using Lantern.Core.Devices;
using Lantern.Core.Networking;
using Lantern.Core.Settings;
using Lantern.Linux.Services;
using Lantern.Linux.ViewModels;

namespace Lantern.Linux;

public partial class MainWindow : Window
{
    private static readonly HttpClient UpdateHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
    };
    private static readonly TimeSpan OfflineRetention = TimeSpan.FromSeconds(45);
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#68C08A"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#A89095"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#D72C43"));
    private static readonly IBrush InactiveNavBrush = new SolidColorBrush(Color.Parse("#A89095"));
    private readonly bool demoMode;
    private readonly DeviceRegistry registry = new();
    private readonly TrafficPolicy policy = new();
    private readonly SettingsStore settingsStore = new();
    private readonly GitHubUpdateChecker updateChecker = new(UpdateHttpClient);
    private readonly TrafficHistory trafficHistory = new(
        TrafficSamplingProfile.Capacity,
        TrafficSamplingProfile.Retention);
    private readonly Dictionary<string, DeviceViewModel> devicesByMac =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceActivityGroupViewModel> activityGroupsByMac =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceActivityViewModel> activityByKey =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer refreshTimer;
    private readonly LinuxLanEngine engine;
    private AppSettings settings = new();
    private LinuxDashboardState dashboardState;
    private DateTimeOffset? startedAt;
    private bool closingAfterRestore;
    private bool busy;

    public string VersionText =>
        $"v{(typeof(MainWindow).Assembly.GetName().Version ?? new Version()).ToString(3)} beta";

    public MainWindow()
        : this(false)
    {
    }

    public MainWindow(bool demoMode)
    {
        this.demoMode = demoMode;
        InitializeComponent();
        DataContext = this;
        SidebarVersionText.Text = VersionText;
        engine = new LinuxLanEngine(registry, policy);
        dashboardState = new LinuxDashboardState(settings, policy);
        engine.StatusChanged += Engine_OnStatusChanged;
        engine.StateChanged += Engine_OnStateChanged;
        engine.DeviceIdentityLearned += Engine_OnDeviceIdentityLearned;
        engine.DeviceDomainObserved += Engine_OnDeviceDomainObserved;

        refreshTimer = new DispatcherTimer { Interval = TrafficSamplingProfile.Interval };
        refreshTimer.Tick += RefreshTimer_OnTick;
        Opened += MainWindow_OnOpened;
        Closing += MainWindow_OnClosing;
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];
    public ObservableCollection<DeviceViewModel> DomainRuleDevices { get; } = [];
    public ObservableCollection<DeviceActivityGroupViewModel> DeviceActivityGroups { get; } = [];
    public ObservableCollection<DomainPresetRuleViewModel> DomainPresetRules { get; } = [];
    public ObservableCollection<DomainRuleViewModel> DomainRules { get; } = [];
    public IReadOnlyList<DomainBlockPreset> DomainPresets => DomainBlockPresetCatalog.All;

    private async void MainWindow_OnOpened(object? sender, EventArgs eventArgs)
    {
        if (demoMode)
        {
            LoadDemoData();
            refreshTimer.Start();
            return;
        }

        settings = await settingsStore.LoadAsync();
        dashboardState = new LinuxDashboardState(settings, policy);
        ApplyAllSavedRules();
        LoadDomainRulesFromSettings();
        LoadAdapters();
        refreshTimer.Start();
        RefreshDashboard();
        UpdateButtons();
        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        var checkedAt = DateTimeOffset.UtcNow;
        if (!GitHubUpdateChecker.ShouldCheck(
                settings.DisableUpdateChecks,
                settings.LastUpdateCheckUtc,
                checkedAt))
        {
            return;
        }

        settings.LastUpdateCheckUtc = checkedAt;
        try
        {
            var installedVersion = typeof(MainWindow).Assembly.GetName().Version ?? new Version();
            var available = await updateChecker.CheckAsync(
                installedVersion,
                UpdatePlatform.LinuxX64);
            await settingsStore.SaveAsync(settings);
            if (available is null)
            {
                return;
            }

            var prompt = new UpdatePromptWindow(installedVersion, available.LatestVersion);
            var choice = await prompt.ShowDialog<UpdatePromptChoice>(this);
            if (choice == UpdatePromptChoice.NeverAskAgain)
            {
                settings.DisableUpdateChecks = true;
                await settingsStore.SaveAsync(settings);
                return;
            }

            if (choice == UpdatePromptChoice.Update)
            {
                Process.Start(new ProcessStartInfo(available.ReleasePage.AbsoluteUri)
                {
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception)
        {
            // Update checks are optional and must never interrupt app startup.
        }
    }

    private void LoadAdapters()
    {
        try
        {
            var adapters = LinuxAdapterService.GetUsableAdapters();
            AdapterSelector.ItemsSource = adapters;
            AdapterSelector.SelectedIndex = adapters.Count > 0 ? 0 : -1;
            SetStatus(adapters.Count == 0
                ? "No active IPv4 adapter with a gateway was found."
                : "Ready. Start control to scan the selected LAN.");
        }
        catch (Exception exception)
        {
            SetStatus($"Could not enumerate adapters: {exception.Message}");
        }
    }

    private async void StartButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (demoMode)
        {
            return;
        }

        if (busy || engine.IsRunning || AdapterSelector.SelectedItem is not AdapterProfile adapter)
        {
            return;
        }

        busy = true;
        UpdateButtons();
        try
        {
            ClearObservedActivity();
            ApplyAllSavedRules();
            UpdateKnownDeviceHints();
            SetStatus("Starting libpcap and resolving the gateway...");
            await engine.StartAsync(adapter, CancellationToken.None);
            startedAt = DateTimeOffset.UtcNow;
            SetActiveState(true);
            RefreshDashboard();
        }
        catch (Exception exception)
        {
            SetStatus(
                $"Could not start Linux control: {exception.Message} " +
                "Install libpcap and grant CAP_NET_RAW + CAP_NET_ADMIN, or run as root.");
            SetActiveState(false);
        }
        finally
        {
            busy = false;
            UpdateButtons();
        }
    }

    private async void RefreshButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (demoMode)
        {
            RefreshDemoDashboard();
            SetStatus("Demo devices refreshed.");
            return;
        }

        if (busy || !engine.IsRunning)
        {
            return;
        }

        busy = true;
        UpdateButtons();
        try
        {
            UpdateKnownDeviceHints();
            SetStatus("Sweeping the local subnet...");
            var result = await engine.RefreshNeighborsAsync();
            SetStatus(result.StatusMessage);
            RefreshDashboard();
        }
        catch (Exception exception)
        {
            SetStatus($"Device refresh failed: {exception.Message}");
        }
        finally
        {
            busy = false;
            UpdateButtons();
        }
    }

    private async void StopButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (demoMode)
        {
            SetStatus("Demo preview stays active so every page remains populated.");
            return;
        }

        await StopEngineAsync();
    }

    private async Task StopEngineAsync()
    {
        if (busy || !engine.IsRunning)
        {
            return;
        }

        busy = true;
        UpdateButtons();
        try
        {
            SetStatus("Restoring normal ARP mappings...");
            await engine.StopAsync();
            startedAt = null;
            SetActiveState(false);
        }
        catch (Exception exception)
        {
            SetStatus($"Stop and restore reported an error: {exception.Message}");
        }
        finally
        {
            busy = false;
            UpdateButtons();
        }
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs eventArgs)
    {
        if (demoMode)
        {
            RefreshDemoDashboard();
            return;
        }

        RefreshDashboard();
    }

    private void RefreshDashboard()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshots = registry.TakeSnapshot(now)
            .Where(snapshot => IsGateway(snapshot) ||
                (snapshot.LastSeen != DateTimeOffset.MinValue && now - snapshot.LastSeen <= OfflineRetention))
            .OrderBy(snapshot => IsGateway(snapshot) ? 1 : 0)
            .ThenBy(snapshot => Ipv4SortKey(snapshot.IpAddress))
            .ToArray();

        var visibleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots)
        {
            var macKey = TrafficPolicy.NormalizeMac(snapshot.MacAddress.ToString());
            visibleKeys.Add(macKey);
            settings.Devices.TryGetValue(macKey, out var preferences);
            if (!devicesByMac.TryGetValue(macKey, out var device))
            {
                device = new DeviceViewModel(OnDeviceRuleChangedAsync);
                device.Initialize(
                    snapshot,
                    preferences,
                    IsProtected(snapshot),
                    IsGateway(snapshot) ? "Gateway — protected" : "This computer — protected");
                devicesByMac[macKey] = device;
            }
            else
            {
                device.Update(snapshot, preferences?.Alias);
            }

            device.SetPresence(true);
        }

        SyncDevices(snapshots.Select(snapshot =>
            devicesByMac[TrafficPolicy.NormalizeMac(snapshot.MacAddress.ToString())]).ToArray());
        SyncWebsiteActivityGroups();
        SyncDomainRuleDevices();
        UpdateActivityIdentities();

        var summary = DashboardSummary.From(Devices.Where(device => device.IsOnline));
        DeviceCountText.Text = summary.ConnectedDevices.ToString();
        DeviceCountLabel.Text = $"{summary.ConnectedDevices} device{(summary.ConnectedDevices == 1 ? string.Empty : "s")}";
        DownloadTotalText.Text = DeviceViewModel.FormatRate(summary.DownloadBytesPerSecond);
        UploadTotalText.Text = DeviceViewModel.FormatRate(summary.UploadBytesPerSecond);
        ActiveRulesText.Text = summary.ActiveRules.ToString();
        EmptyDeviceText.IsVisible = Devices.Count == 0;
        UpdateEmptyStates();

        if (engine.IsRunning)
        {
            var sample = summary.ToTrafficSample(now);
            trafficHistory.TryAdd(sample, TrafficSamplingProfile.Interval);
            TrafficChart.SetSamples(trafficHistory.Samples);
        }
        else
        {
            trafficHistory.Clear();
            TrafficChart.SetSamples([]);
        }
        TopDeviceText.Text = summary.TopDeviceName is null
            ? "Waiting for traffic"
            : $"{summary.TopDeviceName}  ↓ {DeviceViewModel.FormatRate(summary.TopDeviceDownloadBytesPerSecond)}  ↑ {DeviceViewModel.FormatRate(summary.TopDeviceUploadBytesPerSecond)}";

        UptimeText.Text = startedAt is { } start
            ? $"Active {FormatDuration(now - start)}"
            : "Idle";
    }

    private void SyncDevices(IReadOnlyList<DeviceViewModel> ordered)
    {
        for (var index = Devices.Count - 1; index >= 0; index--)
        {
            if (!ordered.Contains(Devices[index]))
            {
                Devices.RemoveAt(index);
            }
        }

        for (var target = 0; target < ordered.Count; target++)
        {
            var current = Devices.IndexOf(ordered[target]);
            if (current < 0)
            {
                Devices.Insert(target, ordered[target]);
            }
            else if (current != target)
            {
                Devices.Move(current, target);
            }
        }
    }

    private async Task OnDeviceRuleChangedAsync(DeviceViewModel device)
    {
        if (!device.CanControl)
        {
            return;
        }

        try
        {
            var rule = new TrafficRule(device.PauseInternet, device.DownloadLimit, device.UploadLimit);
            dashboardState.ApplyTrafficRule(device.MacKey, rule);
            var preferences = settings.Devices[device.MacKey];
            preferences.Alias = device.Alias;
            preferences.LastKnownIp = device.IpAddress;
            if (demoMode)
            {
                RefreshDemoDashboard();
                return;
            }

            await engine.ApplyRuleAsync(device.MacKey, rule);
            await settingsStore.SaveAsync(settings);
            Dispatcher.UIThread.Post(RefreshDashboard);
        }
        catch (Exception exception)
        {
            Dispatcher.UIThread.Post(() => SetStatus($"Could not apply device rule: {exception.Message}"));
        }
    }

    private void ApplyAllSavedRules()
    {
        foreach (var pair in settings.Devices)
        {
            policy.SetRule(
                pair.Key,
                new TrafficRule(
                    pair.Value.PauseInternet,
                    pair.Value.DownloadKiloBytesPerSecond,
                    pair.Value.UploadKiloBytesPerSecond));
        }

        foreach (var pair in settings.BlockedDomains)
        {
            policy.SetBlockedDomains(pair.Key, pair.Value);
        }
    }

    private void UpdateKnownDeviceHints()
    {
        engine.ReplaceKnownDeviceHints(KnownDeviceHintFactory.Build(settings));
        engine.ReplaceRejectedResolvedNames(
            KnownDeviceHintFactory.FindAmbiguousLearnedNames(settings));
    }

    private void Engine_OnStatusChanged(object? sender, string message) =>
        Dispatcher.UIThread.Post(() => SetStatus(message));

    private void Engine_OnStateChanged(object? sender, LinuxEngineStateChangedEventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() =>
        {
            startedAt = eventArgs.IsRunning
                ? startedAt ?? DateTimeOffset.UtcNow
                : null;
            SetActiveState(eventArgs.IsRunning);
            SetStatus(eventArgs.StatusMessage);
            UpdateButtons();
            RefreshDashboard();
        });

    private void Engine_OnDeviceIdentityLearned(object? sender, DeviceIdentityLearnedEventArgs eventArgs) =>
        Dispatcher.UIThread.Post(async () =>
        {
            var macKey = TrafficPolicy.NormalizeMac(eventArgs.MacAddress.ToString());
            if (!settings.Devices.TryGetValue(macKey, out var preferences))
            {
                preferences = new DevicePreferences();
                settings.Devices[macKey] = preferences;
            }

            preferences.LearnedHostName = eventArgs.HostName.Trim();
            await settingsStore.SaveAsync(settings);
            RefreshDashboard();
        });

    private void Engine_OnDeviceDomainObserved(object? sender, DeviceDomainObservedEventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() => ObserveDomain(eventArgs));

    private void ObserveDomain(DeviceDomainObservedEventArgs eventArgs)
    {
        var macKey = TrafficPolicy.NormalizeMac(eventArgs.MacAddress.ToString());
        var device = devicesByMac.GetValueOrDefault(macKey);
        var deviceName = device?.DisplayName ?? "Unknown device";
        var ipAddress = device?.IpAddress ?? "—";
        var group = GetOrCreateWebsiteActivityGroup(macKey, deviceName, ipAddress);

        var activityKey = $"{macKey}|{eventArgs.Observation.Domain}";
        if (!activityByKey.TryGetValue(activityKey, out var activity))
        {
            activity = new DeviceActivityViewModel(
                macKey,
                deviceName,
                ipAddress,
                eventArgs.Observation.Domain,
                eventArgs.Observation.Source,
                eventArgs.ObservedAt);
            activityByKey[activityKey] = activity;
            group.AddDomain(activity);
        }
        else
        {
            activity.Observe(deviceName, ipAddress, eventArgs.Observation.Source, eventArgs.ObservedAt);
            group.TouchDomain(activity);
        }

        activity.SetBlocked(eventArgs.Blocked || policy.ShouldBlockDomain(macKey, activity.Domain));
        UpdateEmptyStates();
    }

    private DeviceActivityGroupViewModel GetOrCreateWebsiteActivityGroup(
        string macKey,
        string deviceName,
        string ipAddress)
    {
        if (!activityGroupsByMac.TryGetValue(macKey, out var group))
        {
            group = new DeviceActivityGroupViewModel(macKey, deviceName, ipAddress);
            activityGroupsByMac[macKey] = group;
        }
        else
        {
            group.UpdateIdentity(deviceName, ipAddress);
        }

        if (!DeviceActivityGroups.Contains(group))
        {
            DeviceActivityGroups.Add(group);
        }

        return group;
    }

    private void SyncWebsiteActivityGroups()
    {
        var connectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in Devices.Where(device => device.CanControl && device.IsOnline))
        {
            connectedKeys.Add(device.MacKey);
            _ = GetOrCreateWebsiteActivityGroup(
                device.MacKey,
                device.DisplayName,
                device.IpAddress);
        }

        foreach (var group in DeviceActivityGroups
                     .Where(group => !connectedKeys.Contains(group.MacKey))
                     .ToArray())
        {
            DeviceActivityGroups.Remove(group);
        }

        UpdateEmptyStates();
    }

    private async void BlockDomainButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: DeviceActivityViewModel activity })
        {
            await AddDomainsAsync(activity.MacKey, [activity.Domain]);
        }
    }

    private async void ApplyPresetButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (RuleDeviceSelector.SelectedItem is DeviceViewModel device &&
            PresetSelector.SelectedItem is DomainBlockPreset preset)
        {
            dashboardState.ApplyPreset(device.MacKey, preset);
            await SaveDomainRulesAsync($"{preset.Name} blocked for {device.DisplayName}.");
        }
    }

    private async void AddCustomDomainButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (RuleDeviceSelector.SelectedItem is not DeviceViewModel device)
        {
            SetStatus("Choose a device first.");
            return;
        }

        try
        {
            var domain = TrafficPolicy.NormalizeDomain(CustomDomainTextBox.Text ?? string.Empty);
            await AddDomainsAsync(device.MacKey, [domain]);
            CustomDomainTextBox.Text = string.Empty;
        }
        catch (FormatException exception)
        {
            SetStatus(exception.Message);
        }
    }

    private async Task AddDomainsAsync(string macKey, IReadOnlyList<string> domains)
    {
        dashboardState.ApplyPreset(macKey, new DomainBlockPreset("Custom", domains));
        await SaveDomainRulesAsync($"Blocked {domains.Count} domain rule{(domains.Count == 1 ? string.Empty : "s")}.");
    }

    private async void RemoveRuleButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: DomainRuleViewModel rule })
        {
            return;
        }

        dashboardState.RemoveDomain(rule.MacKey, rule.Domain);
        await SaveDomainRulesAsync($"Removed {rule.Domain}.");
    }

    private async void RemovePresetButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: DomainPresetRuleViewModel preset })
        {
            return;
        }

        if (settings.AppliedDomainPresets.TryGetValue(preset.MacKey, out var appliedPresets))
        {
            appliedPresets.RemoveAll(name =>
                name.Equals(preset.PresetName, StringComparison.OrdinalIgnoreCase));
            if (appliedPresets.Count == 0)
            {
                settings.AppliedDomainPresets.Remove(preset.MacKey);
            }
        }

        var stillClaimed = settings.AppliedDomainPresets
            .GetValueOrDefault(preset.MacKey, [])
            .SelectMany(name => DomainBlockPresetCatalog.All
                .Where(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(candidate => candidate.Domains))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (settings.BlockedDomains.TryGetValue(preset.MacKey, out var blockedDomains))
        {
            blockedDomains.RemoveAll(domain =>
                preset.Domains.Contains(domain, StringComparer.OrdinalIgnoreCase) &&
                !stillClaimed.Contains(domain));
            if (blockedDomains.Count == 0)
            {
                settings.BlockedDomains.Remove(preset.MacKey);
            }
        }

        policy.SetBlockedDomains(preset.MacKey, settings.BlockedDomains.GetValueOrDefault(preset.MacKey, []));
        await SaveDomainRulesAsync($"Removed the {preset.PresetName} preset from {preset.DeviceName}.");
    }

    private async Task SaveDomainRulesAsync(string message)
    {
        if (!demoMode)
        {
            await settingsStore.SaveAsync(settings);
        }

        LoadDomainRulesFromSettings();
        RefreshBlockedActivityState();
        SetStatus(message);
    }

    private void LoadDomainRulesFromSettings()
    {
        DomainPresetRules.Clear();
        DomainRules.Clear();
        foreach (var pair in settings.BlockedDomains.OrderBy(pair => pair.Key))
        {
            var name = devicesByMac.GetValueOrDefault(pair.Key)?.DisplayName ??
                settings.Devices.GetValueOrDefault(pair.Key)?.Alias ??
                settings.Devices.GetValueOrDefault(pair.Key)?.LearnedHostName ??
                FormatMac(pair.Key);
            var presentation = DomainRulePresentationBuilder.Build(
                pair.Key,
                name,
                pair.Value,
                settings.AppliedDomainPresets.GetValueOrDefault(pair.Key, []));
            foreach (var preset in presentation.Presets)
            {
                DomainPresetRules.Add(preset);
            }

            foreach (var rule in presentation.IndividualRules)
            {
                DomainRules.Add(rule);
            }
        }

        UpdateEmptyStates();
    }

    private void RefreshBlockedActivityState()
    {
        foreach (var activity in activityByKey.Values)
        {
            activity.SetBlocked(policy.ShouldBlockDomain(activity.MacKey, activity.Domain));
        }
    }

    private void UpdateActivityIdentities()
    {
        foreach (var pair in activityGroupsByMac)
        {
            var device = devicesByMac.GetValueOrDefault(pair.Key);
            if (device is not null)
            {
                pair.Value.UpdateIdentity(device.DisplayName, device.IpAddress);
            }
        }

        foreach (var rule in DomainRules)
        {
            var device = devicesByMac.GetValueOrDefault(rule.MacKey);
            if (device is not null)
            {
                rule.UpdateDeviceName(device.DisplayName);
            }
        }

        foreach (var preset in DomainPresetRules)
        {
            var device = devicesByMac.GetValueOrDefault(preset.MacKey);
            if (device is not null)
            {
                preset.UpdateDeviceName(device.DisplayName);
            }
        }
    }

    private void SyncDomainRuleDevices()
    {
        var candidates = Devices.Where(device => device.CanControl).ToArray();
        for (var index = DomainRuleDevices.Count - 1; index >= 0; index--)
        {
            if (!candidates.Contains(DomainRuleDevices[index]))
            {
                DomainRuleDevices.RemoveAt(index);
            }
        }

        foreach (var candidate in candidates)
        {
            if (!DomainRuleDevices.Contains(candidate))
            {
                DomainRuleDevices.Add(candidate);
            }
        }

        if (RuleDeviceSelector.SelectedIndex < 0 && DomainRuleDevices.Count > 0)
        {
            RuleDeviceSelector.SelectedIndex = 0;
        }
    }

    private void ClearActivityButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        ClearObservedActivity();
        SetStatus("Observed domain activity cleared.");
    }

    private void ClearObservedActivity()
    {
        DeviceActivityGroups.Clear();
        activityGroupsByMac.Clear();
        activityByKey.Clear();
        UpdateEmptyStates();
    }

    private void AdapterSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (AdapterSelector.SelectedItem is AdapterProfile adapter)
        {
            LocalIpText.Text = adapter.LocalAddress.ToString();
            GatewayText.Text = adapter.GatewayAddress.ToString();
        }
        else
        {
            LocalIpText.Text = "-";
            GatewayText.Text = "-";
        }

        UpdateButtons();
    }

    private void OverviewNavButton_OnClick(object? sender, RoutedEventArgs eventArgs) => ShowPage(OverviewPage, OverviewNavButton);
    private void ActivityNavButton_OnClick(object? sender, RoutedEventArgs eventArgs) => ShowPage(ActivityPage, ActivityNavButton);
    private void RulesNavButton_OnClick(object? sender, RoutedEventArgs eventArgs) => ShowPage(RulesPage, RulesNavButton);

    private void ShowPage(Control page, Button activeButton)
    {
        OverviewPage.IsVisible = ReferenceEquals(page, OverviewPage);
        ActivityPage.IsVisible = ReferenceEquals(page, ActivityPage);
        RulesPage.IsVisible = ReferenceEquals(page, RulesPage);
        foreach (var button in new[] { OverviewNavButton, ActivityNavButton, RulesNavButton })
        {
            button.Classes.Set("active", ReferenceEquals(button, activeButton));
        }

        OverviewNavIcon.Fill = ReferenceEquals(activeButton, OverviewNavButton) ? AccentBrush : InactiveNavBrush;
        ActivityNavIcon.Stroke = ReferenceEquals(activeButton, ActivityNavButton) ? AccentBrush : InactiveNavBrush;
        RulesNavIcon.Stroke = ReferenceEquals(activeButton, RulesNavButton) ? AccentBrush : InactiveNavBrush;
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (eventArgs.Source is not Button &&
            eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(eventArgs);
        }
    }

    private void MinimizeButton_OnClick(object? sender, RoutedEventArgs eventArgs) => WindowState = WindowState.Minimized;
    private void MaximizeButton_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Close();

    private async void MainWindow_OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (demoMode)
        {
            refreshTimer.Stop();
            return;
        }

        if (closingAfterRestore)
        {
            return;
        }

        eventArgs.Cancel = true;
        refreshTimer.Stop();
        if (engine.IsRunning)
        {
            await StopEngineAsync();
        }

        await settingsStore.SaveAsync(settings);
        closingAfterRestore = true;
        Close();
    }

    private void UpdateButtons()
    {
        if (demoMode)
        {
            StartButton.IsVisible = false;
            RefreshButton.IsVisible = true;
            StopButton.IsVisible = true;
            RefreshButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            AdapterSelector.IsEnabled = true;
            return;
        }

        StartButton.IsEnabled = !busy && !engine.IsRunning && AdapterSelector.SelectedItem is AdapterProfile;
        RefreshButton.IsEnabled = !busy && engine.IsRunning;
        StopButton.IsEnabled = !busy && engine.IsRunning;
        AdapterSelector.IsEnabled = !busy && !engine.IsRunning;
        StartButton.IsVisible = !engine.IsRunning;
        RefreshButton.IsVisible = engine.IsRunning;
        StopButton.IsVisible = engine.IsRunning;
    }

    private void SetActiveState(bool active)
    {
        ActiveIndicator.Fill = active ? ActiveBrush : IdleBrush;
        ActiveStatusText.Text = active ? "Active" : "Idle";
    }

    private void UpdateEmptyStates()
    {
        var hasActivity = DeviceActivityGroups.Count > 0;
        ActivityList.IsVisible = hasActivity;
        ActivityEmptyState.IsVisible = !hasActivity;
        var domainCount = DeviceActivityGroups.Sum(group => group.Domains.Count);
        WebsiteActivityCountText.Text =
            $"{DeviceActivityGroups.Count} device{(DeviceActivityGroups.Count == 1 ? string.Empty : "s")}  •  " +
            $"{domainCount} domain{(domainCount == 1 ? string.Empty : "s")}";

        var hasRules = DomainRules.Count > 0 || DomainPresetRules.Count > 0;
        DomainRulesList.IsVisible = hasRules;
        DomainRulesEmptyState.IsVisible = !hasRules;
        DomainRuleCountText.Text =
            $"{DomainPresetRules.Count} preset{(DomainPresetRules.Count == 1 ? string.Empty : "s")}  •  " +
            $"{DomainRules.Count} rule{(DomainRules.Count == 1 ? string.Empty : "s")}";
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private bool IsGateway(DeviceSnapshot snapshot) =>
        AdapterSelector.SelectedItem is AdapterProfile adapter && snapshot.IpAddress.Equals(adapter.GatewayAddress);

    private bool IsProtected(DeviceSnapshot snapshot) =>
        IsGateway(snapshot) ||
        (AdapterSelector.SelectedItem is AdapterProfile adapter && snapshot.MacAddress.Equals(adapter.LocalMac));

    private static uint Ipv4SortKey(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            ? ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3]
            : uint.MaxValue;
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";

    private static string FormatMac(string macKey) => macKey.Length == 12
        ? string.Join(":", Enumerable.Range(0, 6).Select(index => macKey.Substring(index * 2, 2)))
        : macKey;
}
