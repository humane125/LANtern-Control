using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lantern.App.Controls;
using Lantern.App.Services;
using Lantern.App.ViewModels;
using Lantern.Core.Control;
using Lantern.Core.Devices;
using Lantern.Core.Networking;
using Lantern.Core.Settings;

namespace Lantern.App;

public partial class MainWindow : Window
{
    private const int MaxWebsiteActivityRows = 250;
    private static readonly HttpClient UpdateHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };
    private readonly DeviceRegistry registry = new();
    private readonly TrafficPolicy policy = new();
    private readonly SettingsStore settingsStore = new();
    private readonly SemaphoreSlim identitySaveSync = new(1, 1);
    private readonly DispatcherTimer refreshTimer;
    private readonly Dictionary<string, DeviceViewModel> deviceIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceActivityViewModel> websiteActivityIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceActivityGroupViewModel> websiteActivityGroupIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TrafficHistory trafficHistory = new(TrafficSamplingProfile.Capacity, TrafficSamplingProfile.Retention);
    private readonly GitHubUpdateChecker updateChecker = new(UpdateHttpClient);
    private PcapLanEngine engine;
    private AppSettings settings = new();
    private DateTimeOffset? controlStartedAt;
    private bool operationInProgress;
    private bool closeAfterStop;
    private bool settingsDirty;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        DomainPresetSelector.ItemsSource = DomainPresets;
        engine = new PcapLanEngine(registry, policy);
        engine.StatusChanged += Engine_OnStatusChanged;
        engine.DeviceIdentityLearned += Engine_OnDeviceIdentityLearned;
        engine.DeviceDomainObserved += Engine_OnDeviceDomainObserved;
        refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, RefreshDevices, Dispatcher);
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;

        var view = CollectionViewSource.GetDefaultView(Devices);
        view.SortDescriptions.Add(
            new SortDescription(nameof(DeviceViewModel.IsProtected), ListSortDirection.Ascending));
        view.SortDescriptions.Add(
            new SortDescription(nameof(DeviceViewModel.DisplayName), ListSortDirection.Ascending));

        var activityView = CollectionViewSource.GetDefaultView(WebsiteActivity);
        activityView.SortDescriptions.Add(
            new SortDescription(nameof(DeviceActivityViewModel.LastSeen), ListSortDirection.Descending));

        var groupView = CollectionViewSource.GetDefaultView(DeviceActivityGroups);
        groupView.SortDescriptions.Add(
            new SortDescription(nameof(DeviceActivityGroupViewModel.DeviceName), ListSortDirection.Ascending));

        var domainRuleView = CollectionViewSource.GetDefaultView(DomainRules);
        domainRuleView.SortDescriptions.Add(
            new SortDescription(nameof(DomainRuleViewModel.DeviceName), ListSortDirection.Ascending));
        domainRuleView.SortDescriptions.Add(
            new SortDescription(nameof(DomainRuleViewModel.Domain), ListSortDirection.Ascending));

        var domainPresetRuleView = CollectionViewSource.GetDefaultView(DomainPresetRules);
        domainPresetRuleView.SortDescriptions.Add(
            new SortDescription(nameof(DomainPresetRuleViewModel.DeviceName), ListSortDirection.Ascending));
        domainPresetRuleView.SortDescriptions.Add(
            new SortDescription(nameof(DomainPresetRuleViewModel.PresetName), ListSortDirection.Ascending));
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    public ObservableCollection<DeviceActivityViewModel> WebsiteActivity { get; } = [];

    public ObservableCollection<DeviceActivityGroupViewModel> DeviceActivityGroups { get; } = [];

    public ObservableCollection<DeviceViewModel> DomainRuleDevices { get; } = [];

    public ObservableCollection<DomainRuleViewModel> DomainRules { get; } = [];

    public ObservableCollection<DomainPresetRuleViewModel> DomainPresetRules { get; } = [];

    public IReadOnlyList<DomainBlockPreset> DomainPresets => DomainBlockPresetCatalog.All;

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        settings = await settingsStore.LoadAsync();
        LoadDomainRulesFromSettings();
        var adapters = WindowsAdapterService.GetUsableAdapters();
        AdapterSelector.ItemsSource = adapters;
        AdapterSelector.SelectedIndex = adapters.Count > 0 ? 0 : -1;
        UpdateAdapterSummary();
        if (adapters.Count == 0)
        {
            SetStatus("No active IPv4 adapter with a gateway was found.", false);
            StartButton.IsEnabled = false;
        }

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
            var available = await updateChecker.CheckAsync(installedVersion);
            await settingsStore.SaveAsync(settings);
            settingsDirty = false;
            if (available is null)
            {
                return;
            }

            var prompt = new UpdatePromptWindow(installedVersion, available.LatestVersion)
            {
                Owner = this,
            };
            prompt.ShowDialog();
            if (prompt.Choice == UpdatePromptChoice.NeverAskAgain)
            {
                settings.DisableUpdateChecks = true;
                await settingsStore.SaveAsync(settings);
                settingsDirty = false;
                return;
            }

            if (prompt.Choice == UpdatePromptChoice.Update)
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

    private async void StartButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (operationInProgress || AdapterSelector.SelectedItem is not AdapterProfile selected)
        {
            return;
        }

        operationInProgress = true;
        UpdateButtons();
        SetStatus("Starting packet capture…", null);
        try
        {
            ApplyAllSavedRules();
            UpdateKnownDeviceHints();
            await engine.StartAsync(selected, CancellationToken.None);
            controlStartedAt = DateTimeOffset.UtcNow;
            TrafficChart.Samples = trafficHistory.Samples;
            RefreshDevices(null, EventArgs.Empty);
            await SaveSettingsIfDirtyAsync();
            SetStatus("Control active", true);
            refreshTimer.Start();
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, false);
            MessageBox.Show(
                exception.Message,
                "Could not start LAN control",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            operationInProgress = false;
            UpdateButtons();
        }
    }

    private async void StopButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        await StopEngineAsync();
    }

    private async void ScanButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (operationInProgress || !engine.IsRunning)
        {
            return;
        }

        operationInProgress = true;
        UpdateButtons();
        try
        {
            SetStatus("Sending a full /24 ARP sweep…", null);
            UpdateKnownDeviceHints();
            var result = await engine.RefreshNeighborsAsync();
            RefreshDevices(null, EventArgs.Empty);
            await SaveSettingsIfDirtyAsync();
            SetStatus("Control active", true);
            DetailStatusText.Text = result.StatusMessage;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, false);
        }
        finally
        {
            operationInProgress = false;
            UpdateButtons();
        }
    }

    private void ClearActivityButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        websiteActivityIndex.Clear();
        WebsiteActivity.Clear();
        foreach (var group in websiteActivityGroupIndex.Values)
        {
            group.ClearDomains();
        }

        RefreshWebsiteActivityState();
        DetailStatusText.Text = "Website activity cleared from this session.";
    }

    private void Engine_OnDeviceDomainObserved(
        object? sender,
        DeviceDomainObservedEventArgs eventArgs)
    {
        _ = Dispatcher.BeginInvoke(
            () => ObserveWebsiteActivity(eventArgs),
            DispatcherPriority.Background);
    }

    private void ObserveWebsiteActivity(DeviceDomainObservedEventArgs eventArgs)
    {
        var macKey = TrafficPolicy.NormalizeMac(eventArgs.MacAddress.ToString());
        var deviceName = deviceIndex.TryGetValue(macKey, out var device)
            ? device.DisplayName
            : $"Device {FormatMac(macKey)}";
        var ipAddress = device?.IpAddress ?? "-";
        var group = GetOrCreateWebsiteActivityGroup(macKey, deviceName, ipAddress);
        var domainKey = $"{macKey}|{eventArgs.Observation.Domain}";
        if (websiteActivityIndex.TryGetValue(domainKey, out var existing))
        {
            existing.Observe(
                deviceName,
                ipAddress,
                eventArgs.Observation.Source,
                eventArgs.ObservedAt);
            existing.SetBlocked(
                eventArgs.Blocked || policy.ShouldBlockDomain(macKey, existing.Domain));
            group.TouchDomain(existing);
        }
        else
        {
            var activity = new DeviceActivityViewModel(
                macKey,
                deviceName,
                ipAddress,
                eventArgs.Observation.Domain,
                eventArgs.Observation.Source,
                eventArgs.ObservedAt);
            activity.SetBlocked(
                eventArgs.Blocked || policy.ShouldBlockDomain(macKey, activity.Domain));
            websiteActivityIndex[domainKey] = activity;
            WebsiteActivity.Add(activity);
            group.AddDomain(activity);
        }

        while (WebsiteActivity.Count > MaxWebsiteActivityRows)
        {
            var oldest = WebsiteActivity.MinBy(activity => activity.LastSeen);
            if (oldest is null)
            {
                break;
            }

            WebsiteActivity.Remove(oldest);
            websiteActivityIndex.Remove($"{oldest.MacKey}|{oldest.Domain}");
            if (websiteActivityGroupIndex.TryGetValue(oldest.MacKey, out var oldestGroup))
            {
                oldestGroup.RemoveDomain(oldest);
            }
        }

        CollectionViewSource.GetDefaultView(WebsiteActivity).Refresh();
        RefreshWebsiteActivityState();
    }

    private async void BlockVisitedDomainButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: DeviceActivityViewModel activity })
        {
            await AddDomainBlockAsync(activity.MacKey, activity.Domain);
        }
    }

    private async void AddDomainRuleButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (DomainRuleDeviceSelector.SelectedItem is not DeviceViewModel { CanControl: true } device)
        {
            DomainRuleValidationText.Text = "Choose a connected device.";
            DomainRuleValidationText.Visibility = Visibility.Visible;
            return;
        }

        string domain;
        try
        {
            domain = TrafficPolicy.NormalizeDomain(DomainRuleInput.Text);
        }
        catch (FormatException exception)
        {
            DomainRuleValidationText.Text = exception.Message;
            DomainRuleValidationText.Visibility = Visibility.Visible;
            DomainRuleInput.Focus();
            return;
        }

        DomainRuleValidationText.Visibility = Visibility.Collapsed;
        await AddDomainBlockAsync(device.MacKey, domain);
        DomainRuleInput.Clear();
    }

    private async void ApplyDomainPresetButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (DomainPresetDeviceSelector.SelectedItem is not DeviceViewModel { CanControl: true } device)
        {
            DomainPresetValidationText.Text = "Choose a connected device.";
            DomainPresetValidationText.Visibility = Visibility.Visible;
            return;
        }

        if (DomainPresetSelector.SelectedItem is not DomainBlockPreset preset)
        {
            DomainPresetValidationText.Text = "Choose an app preset.";
            DomainPresetValidationText.Visibility = Visibility.Visible;
            return;
        }

        DomainPresetValidationText.Visibility = Visibility.Collapsed;
        var macKey = TrafficPolicy.NormalizeMac(device.MacKey);
        if (!settings.AppliedDomainPresets.TryGetValue(macKey, out var appliedPresets))
        {
            appliedPresets = [];
            settings.AppliedDomainPresets[macKey] = appliedPresets;
        }

        if (!appliedPresets.Contains(preset.Name, StringComparer.OrdinalIgnoreCase))
        {
            appliedPresets.Add(preset.Name);
            appliedPresets.Sort(StringComparer.OrdinalIgnoreCase);
        }

        await AddDomainBlocksAsync(
            macKey,
            preset.Domains,
            $"Applied the {preset.Name} preset to {device.DisplayName}. " +
            "Reconnect the app so existing sessions close.");
    }

    private async void RemoveDomainRuleButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: DomainRuleViewModel rule })
        {
            await RemoveDomainBlockAsync(rule);
        }
    }

    private async void RemoveDomainPresetButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: DomainPresetRuleViewModel preset })
        {
            await RemoveDomainPresetAsync(preset);
        }
    }

    private async Task AddDomainBlockAsync(string macAddress, string domain)
    {
        var normalizedDomain = TrafficPolicy.NormalizeDomain(domain);
        await AddDomainBlocksAsync(
            macAddress,
            [normalizedDomain],
            $"Blocked {normalizedDomain} for {ResolveDeviceName(TrafficPolicy.NormalizeMac(macAddress))}. " +
            "New connections are filtered immediately.");
    }

    private async Task AddDomainBlocksAsync(
        string macAddress,
        IEnumerable<string> requestedDomains,
        string status)
    {
        var macKey = TrafficPolicy.NormalizeMac(macAddress);
        if (!settings.BlockedDomains.TryGetValue(macKey, out var domains))
        {
            domains = [];
            settings.BlockedDomains[macKey] = domains;
        }

        foreach (var requested in requestedDomains)
        {
            var normalized = TrafficPolicy.NormalizeDomain(requested);
            if (!domains.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                domains.Add(normalized);
            }
        }

        domains.Sort(StringComparer.OrdinalIgnoreCase);
        policy.SetBlockedDomains(macKey, domains);
        RebuildDomainRulePresentation();
        RefreshBlockedActivityState();
        await SaveDomainRulesAsync(status);
    }

    private async Task RemoveDomainBlockAsync(DomainRuleViewModel rule)
    {
        if (settings.BlockedDomains.TryGetValue(rule.MacKey, out var domains))
        {
            domains.RemoveAll(domain =>
                domain.Equals(rule.Domain, StringComparison.OrdinalIgnoreCase));
            if (domains.Count == 0)
            {
                settings.BlockedDomains.Remove(rule.MacKey);
            }
        }

        policy.SetBlockedDomains(
            rule.MacKey,
            settings.BlockedDomains.TryGetValue(rule.MacKey, out var remaining)
                ? remaining
                : []);
        RebuildDomainRulePresentation();
        RefreshBlockedActivityState();
        await SaveDomainRulesAsync($"Unblocked {rule.Domain} for {rule.DeviceName}.");
    }

    private async Task RemoveDomainPresetAsync(DomainPresetRuleViewModel preset)
    {
        if (settings.AppliedDomainPresets.TryGetValue(preset.MacKey, out var appliedPresets))
        {
            appliedPresets.RemoveAll(name =>
                name.Equals(preset.PresetName, StringComparison.OrdinalIgnoreCase));
            if (appliedPresets.Count == 0)
            {
                settings.AppliedDomainPresets.Remove(preset.MacKey);
            }
        }

        var domainsStillClaimed = settings.AppliedDomainPresets
            .GetValueOrDefault(preset.MacKey, [])
            .SelectMany(name => DomainBlockPresetCatalog.All
                .Where(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(candidate => candidate.Domains))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (settings.BlockedDomains.TryGetValue(preset.MacKey, out var blockedDomains))
        {
            blockedDomains.RemoveAll(domain =>
                preset.Domains.Contains(domain, StringComparer.OrdinalIgnoreCase) &&
                !domainsStillClaimed.Contains(domain));
            if (blockedDomains.Count == 0)
            {
                settings.BlockedDomains.Remove(preset.MacKey);
            }
        }

        policy.SetBlockedDomains(
            preset.MacKey,
            settings.BlockedDomains.GetValueOrDefault(preset.MacKey, []));
        RebuildDomainRulePresentation();
        RefreshBlockedActivityState();
        await SaveDomainRulesAsync($"Removed the {preset.PresetName} preset from {preset.DeviceName}.");
    }

    private async Task SaveDomainRulesAsync(string status)
    {
        try
        {
            await settingsStore.SaveAsync(settings);
            settingsDirty = false;
            RefreshDomainRulesState();
            DetailStatusText.Text = status;
        }
        catch (IOException exception)
        {
            DetailStatusText.Text = exception.Message;
        }
    }

    private void LoadDomainRulesFromSettings()
    {
        foreach (var pair in settings.BlockedDomains)
        {
            policy.SetBlockedDomains(pair.Key, pair.Value);
        }

        RebuildDomainRulePresentation();
        RefreshBlockedActivityState();
        RefreshDomainRulesState();
    }

    private void RebuildDomainRulePresentation()
    {
        DomainPresetRules.Clear();
        DomainRules.Clear();
        foreach (var pair in settings.BlockedDomains)
        {
            var presentation = DomainRulePresentationBuilder.Build(
                pair.Key,
                ResolveDeviceName(pair.Key),
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
    }

    private string ResolveDeviceName(string macKey)
    {
        if (deviceIndex.TryGetValue(macKey, out var device))
        {
            return device.DisplayName;
        }

        if (settings.Devices.TryGetValue(macKey, out var preferences))
        {
            if (!string.IsNullOrWhiteSpace(preferences.Alias))
            {
                return preferences.Alias;
            }

            if (!string.IsNullOrWhiteSpace(preferences.LearnedHostName))
            {
                return preferences.LearnedHostName;
            }
        }

        return $"Device {FormatMac(macKey)}";
    }

    private void RefreshBlockedActivityState()
    {
        foreach (var activity in WebsiteActivity)
        {
            activity.SetBlocked(policy.ShouldBlockDomain(activity.MacKey, activity.Domain));
        }
    }

    private void RefreshDomainRulesState()
    {
        DomainRulesEmptyState.Visibility = DomainRules.Count == 0 && DomainPresetRules.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        var presetLabel = $"{DomainPresetRules.Count} preset{(DomainPresetRules.Count == 1 ? string.Empty : "s")}";
        var ruleLabel = $"{DomainRules.Count} rule{(DomainRules.Count == 1 ? string.Empty : "s")}";
        DomainRuleCountText.Text = $"{presetLabel}  •  {ruleLabel}";
        CollectionViewSource.GetDefaultView(DomainPresetRules).Refresh();
        CollectionViewSource.GetDefaultView(DomainRules).Refresh();
    }

    private void RefreshWebsiteActivityState()
    {
        WebsiteActivityEmptyState.Visibility = DeviceActivityGroups.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        WebsiteActivityCountText.Text =
            $"{DeviceActivityGroups.Count} device{(DeviceActivityGroups.Count == 1 ? string.Empty : "s")}  •  " +
            $"{WebsiteActivity.Count} domain{(WebsiteActivity.Count == 1 ? string.Empty : "s")}";
    }

    private DeviceActivityGroupViewModel GetOrCreateWebsiteActivityGroup(
        string macKey,
        string deviceName,
        string ipAddress)
    {
        if (!websiteActivityGroupIndex.TryGetValue(macKey, out var group))
        {
            group = new DeviceActivityGroupViewModel(macKey, deviceName, ipAddress);
            websiteActivityGroupIndex[macKey] = group;
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

        RefreshWebsiteActivityState();
    }

    private void SyncDomainRuleDevices()
    {
        var available = Devices
            .Where(device => device.CanControl && device.IsOnline)
            .ToArray();
        foreach (var stale in DomainRuleDevices
                     .Where(device => !available.Contains(device))
                     .ToArray())
        {
            DomainRuleDevices.Remove(stale);
        }

        foreach (var device in available)
        {
            if (!DomainRuleDevices.Contains(device))
            {
                DomainRuleDevices.Add(device);
            }

            foreach (var rule in DomainRules.Where(rule =>
                         rule.MacKey.Equals(device.MacKey, StringComparison.OrdinalIgnoreCase)))
            {
                rule.UpdateDeviceName(device.DisplayName);
            }

            foreach (var preset in DomainPresetRules.Where(preset =>
                         preset.MacKey.Equals(device.MacKey, StringComparison.OrdinalIgnoreCase)))
            {
                preset.UpdateDeviceName(device.DisplayName);
            }
        }

        if (DomainRuleDeviceSelector.SelectedItem is null && DomainRuleDevices.Count > 0)
        {
            DomainRuleDeviceSelector.SelectedIndex = 0;
        }

        if (DomainPresetDeviceSelector.SelectedItem is null && DomainRuleDevices.Count > 0)
        {
            DomainPresetDeviceSelector.SelectedIndex = 0;
        }

        CollectionViewSource.GetDefaultView(DomainRuleDevices).Refresh();
        CollectionViewSource.GetDefaultView(DomainPresetRules).Refresh();
        CollectionViewSource.GetDefaultView(DomainRules).Refresh();
    }

    private void AdapterSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        UpdateAdapterSummary();
        StartButton.IsEnabled = !operationInProgress &&
                                !engine.IsRunning &&
                                AdapterSelector.SelectedItem is AdapterProfile;
    }

    private async Task StopEngineAsync()
    {
        if (operationInProgress)
        {
            return;
        }

        operationInProgress = true;
        refreshTimer.Stop();
        UpdateButtons();
        SetStatus("Restoring normal ARP mappings…", null);
        try
        {
            await engine.StopAsync();
            RefreshDevices(null, EventArgs.Empty);
            await SaveSettingsIfDirtyAsync();
            controlStartedAt = null;
            UptimeText.Text = "Idle";
            SetStatus("Stopped safely", null);
        }
        finally
        {
            operationInProgress = false;
            UpdateButtons();
        }
    }

    private void RefreshDevices(object? sender, EventArgs eventArgs)
    {
        var activeProfile = AdapterSelector.SelectedItem as AdapterProfile;
        var now = DateTimeOffset.UtcNow;
        var view = CollectionViewSource.GetDefaultView(Devices);
        var editableView = view as IEditableCollectionView;
        var canMutateView = DeviceListRefreshPolicy.ShouldRefresh(
            Keyboard.FocusedElement is TextBox,
            editableView?.IsAddingNew == true,
            editableView?.IsEditingItem == true);
        var visibleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in registry.TakeSnapshot(now))
        {
            if (activeProfile is not null &&
                (snapshot.IpAddress.Equals(activeProfile.LocalAddress) ||
                 snapshot.MacAddress.Equals(activeProfile.LocalMac)))
            {
                continue;
            }

            var key = snapshot.MacAddress.ToString();
            settings.Devices.TryGetValue(key, out var preferences);
            var isGateway = activeProfile is not null &&
                            snapshot.IpAddress.Equals(activeProfile.GatewayAddress);
            var presence = isGateway
                ? DevicePresence.Online
                : DevicePresencePolicy.Classify(snapshot.LastSeen, now);
            if (presence == DevicePresence.Hidden)
            {
                continue;
            }

            visibleKeys.Add(key);
            if (!isGateway)
            {
                preferences ??= settings.Devices[key] = new DevicePreferences();
                var currentIp = snapshot.IpAddress.ToString();
                if (!string.Equals(preferences.LastKnownIp, currentIp, StringComparison.Ordinal))
                {
                    preferences.LastKnownIp = currentIp;
                    settingsDirty = true;
                }
            }

            if (!deviceIndex.TryGetValue(key, out var viewModel))
            {
                if (!canMutateView)
                {
                    continue;
                }

                viewModel = new DeviceViewModel(OnDeviceRuleChangedAsync);
                viewModel.Initialize(
                    snapshot,
                    preferences,
                    isGateway,
                    isGateway ? "Gateway — protected" : "Online");
                deviceIndex[key] = viewModel;
                Devices.Add(viewModel);
            }
            else
            {
                viewModel.Update(snapshot, preferences?.Alias);
            }

            viewModel.SetPresence(presence == DevicePresence.Online);
        }

        if (canMutateView)
        {
            foreach (var stale in Devices
                         .Where(device => !visibleKeys.Contains(device.MacKey))
                         .ToArray())
            {
                deviceIndex.Remove(stale.MacKey);
                Devices.Remove(stale);
            }

            view.Refresh();
        }

        SyncWebsiteActivityGroups();
        SyncDomainRuleDevices();
        RefreshDashboardSummary();
        EmptyState.Visibility = Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeviceCountText.Text = $"{Devices.Count} device{(Devices.Count == 1 ? string.Empty : "s")}";
    }

    private async Task OnDeviceRuleChangedAsync(DeviceViewModel device)
    {
        if (!device.CanControl)
        {
            return;
        }

        var preferences = settings.Devices.TryGetValue(device.MacKey, out var existing)
            ? existing
            : settings.Devices[device.MacKey] = new DevicePreferences();
        var rule = new TrafficRule(
                device.PauseInternet,
                device.DownloadLimit,
                device.UploadLimit)
            .ForForwardingMode();
        var ruleChanged =
            preferences.DownloadKiloBytesPerSecond != rule.DownloadKiloBytesPerSecond ||
            preferences.UploadKiloBytesPerSecond != rule.UploadKiloBytesPerSecond ||
            preferences.PauseInternet != rule.PauseInternet;
        var aliasChanged = !string.Equals(
            preferences.Alias,
            device.Alias,
            StringComparison.Ordinal);
        preferences.DownloadKiloBytesPerSecond = rule.DownloadKiloBytesPerSecond;
        preferences.UploadKiloBytesPerSecond = rule.UploadKiloBytesPerSecond;
        preferences.PauseInternet = rule.PauseInternet;
        preferences.Alias = device.Alias;

        try
        {
            if (ruleChanged)
            {
                await engine.ApplyRuleAsync(device.MacKey, rule);
            }

            await settingsStore.SaveAsync(settings);
            settingsDirty = false;
            await Dispatcher.InvokeAsync(
                () =>
                {
                    DetailStatusText.Text =
                        aliasChanged && !ruleChanged
                            ? $"Device renamed to {device.DisplayName}."
                            : engine.IsRunning
                            ? $"Rule applied to {device.DisplayName}."
                            : $"Rule saved for {device.DisplayName}; it activates when control starts.";
                    RefreshDashboardSummary(addTrafficSample: false);
                });
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException)
        {
            await Dispatcher.InvokeAsync(() => DetailStatusText.Text = exception.Message);
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
                    pair.Value.UploadKiloBytesPerSecond)
                    .ForForwardingMode());
        }

        foreach (var pair in settings.BlockedDomains)
        {
            policy.SetBlockedDomains(pair.Key, pair.Value);
        }
    }

    private void UpdateKnownDeviceHints()
    {
        var hints = new List<KnownDeviceHint>();
        foreach (var pair in settings.Devices)
        {
            try
            {
                var mac = PhysicalAddress.Parse(pair.Key);
                var lastKnownIp = IPAddress.TryParse(pair.Value.LastKnownIp, out var parsed)
                    ? parsed
                    : null;
                hints.Add(new KnownDeviceHint(
                    mac,
                    lastKnownIp,
                    pair.Value.LearnedHostName));
            }
            catch (FormatException)
            {
            }
        }

        engine.ReplaceKnownDeviceHints(hints);
    }

    private async Task SaveSettingsIfDirtyAsync()
    {
        if (!settingsDirty)
        {
            return;
        }

        await settingsStore.SaveAsync(settings);
        settingsDirty = false;
    }

    private void Engine_OnStatusChanged(object? sender, string message)
    {
        _ = Dispatcher.InvokeAsync(() => DetailStatusText.Text = message);
    }

    private void Engine_OnDeviceIdentityLearned(
        object? sender,
        DeviceIdentityLearnedEventArgs eventArgs)
    {
        _ = Dispatcher
            .InvokeAsync(() => PersistLearnedIdentityAsync(eventArgs))
            .Task
            .Unwrap();
    }

    private async Task PersistLearnedIdentityAsync(
        DeviceIdentityLearnedEventArgs eventArgs)
    {
        await identitySaveSync.WaitAsync();
        try
        {
            var lastKnownIp = registry.Peek()
                .FirstOrDefault(device => device.MacAddress.Equals(eventArgs.MacAddress))?
                .IpAddress
                .ToString();
            var identity = DeviceIdentityTracker.Learn(
                settings,
                eventArgs.MacAddress.ToString(),
                eventArgs.HostName,
                lastKnownIp);
            if (identity.PreviousMacKey is not null)
            {
                policy.RemoveRule(identity.PreviousMacKey);
            }

            var rule = new TrafficRule(
                    identity.Preferences.PauseInternet,
                    identity.Preferences.DownloadKiloBytesPerSecond,
                    identity.Preferences.UploadKiloBytesPerSecond)
                .ForForwardingMode();
            await engine.ApplyRuleAsync(identity.MacKey, rule);
            UpdateKnownDeviceHints();
            await settingsStore.SaveAsync(settings);
            settingsDirty = false;
            DetailStatusText.Text = identity.PreviousMacKey is null
                ? $"Remembered {eventArgs.HostName}."
                : $"Recognized {eventArgs.HostName} after its private MAC changed.";
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException)
        {
            DetailStatusText.Text = exception.Message;
        }
        finally
        {
            identitySaveSync.Release();
        }
    }

    private async void MainWindow_OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (closeAfterStop || !engine.IsRunning)
        {
            return;
        }

        eventArgs.Cancel = true;
        IsEnabled = false;
        try
        {
            await StopEngineAsync();
            await engine.DisposeAsync();
        }
        finally
        {
            closeAfterStop = true;
            Close();
        }
    }

    private void UpdateButtons()
    {
        StartButton.IsEnabled = !operationInProgress &&
                                !engine.IsRunning &&
                                AdapterSelector.SelectedItem is AdapterProfile;
        StopButton.IsEnabled = !operationInProgress && engine.IsRunning;
        ScanButton.IsEnabled = !operationInProgress && engine.IsRunning;
        AdapterSelector.IsEnabled = !operationInProgress && !engine.IsRunning;
        StartButton.Visibility = engine.IsRunning ? Visibility.Collapsed : Visibility.Visible;
        ScanButton.Visibility = engine.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Visibility = engine.IsRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MinimizeWindowButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindowButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private void NavigationButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string targetName } ||
            FindName(targetName) is not FrameworkElement target)
        {
            return;
        }

        SetVisiblePage(target);
        MainScrollViewer.ScrollToTop();
        if (target == OverviewSection)
        {
            target.BringIntoView();
        }

        if (target == AdapterStrip)
        {
            AdapterSelector.Focus();
        }
    }

    private void SetVisiblePage(FrameworkElement target)
    {
        var showOverview = target == OverviewSection;
        var overviewVisibility = showOverview ? Visibility.Visible : Visibility.Collapsed;
        OverviewSection.Visibility = overviewVisibility;
        AdapterStrip.Visibility = overviewVisibility;
        MetricsPanel.Visibility = overviewVisibility;
        ActivitySection.Visibility = overviewVisibility;
        DeviceSection.Visibility = overviewVisibility;
        WebsiteActivitySection.Visibility = target == WebsiteActivitySection
            ? Visibility.Visible
            : Visibility.Collapsed;
        DomainRulesSection.Visibility = target == DomainRulesSection
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateAdapterSummary()
    {
        if (AdapterSelector.SelectedItem is not AdapterProfile selected)
        {
            LocalIpText.Text = "-";
            GatewayIpText.Text = "-";
            return;
        }

        LocalIpText.Text = selected.LocalAddress.ToString();
        GatewayIpText.Text = selected.GatewayAddress.ToString();
    }

    private void RefreshDashboardSummary(bool addTrafficSample = true)
    {
        var summary = DashboardSummary.From(Devices);
        ConnectedDevicesMetric.Text = summary.ConnectedDevices.ToString();
        DownloadMetric.Text = DeviceViewModel.FormatRate(summary.DownloadBytesPerSecond);
        UploadMetric.Text = DeviceViewModel.FormatRate(summary.UploadBytesPerSecond);
        ActiveRulesMetric.Text = summary.ActiveRules.ToString();

        if (addTrafficSample && engine.IsRunning)
        {
            var sample = new TrafficSample(
                DateTimeOffset.UtcNow,
                summary.DownloadBytesPerSecond,
                summary.UploadBytesPerSecond,
                summary.TopDeviceName,
                summary.TopDeviceDownloadBytesPerSecond,
                summary.TopDeviceUploadBytesPerSecond,
                summary.DeviceTraffic);
            if (trafficHistory.TryAdd(sample, TrafficSamplingProfile.Interval))
            {
                TrafficChart.Samples = trafficHistory.Samples;
                ChartDeviceSummaryText.Text = TrafficChartPresentation.BuildLatestSummary(sample);
            }
        }

        if (controlStartedAt is { } startedAt)
        {
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            UptimeText.Text = $"Active {elapsed:hh\\:mm\\:ss}";
        }
    }

    private static string FormatMac(string macKey) =>
        string.Join(
            ":",
            Enumerable.Range(0, 6).Select(index => macKey.Substring(index * 2, 2)));

    private void SetStatus(string message, bool? active)
    {
        TopStatusText.Text = active switch
        {
            true => "Active",
            false => "Needs attention",
            null => engine.IsRunning ? "Working" : "Idle",
        };
        DetailStatusText.Text = message;
        StatusDot.Fill = new SolidColorBrush(
            active switch
            {
                true => Color.FromRgb(104, 192, 138),
                false => Color.FromRgb(240, 100, 115),
                null => Color.FromRgb(168, 144, 149),
            });
    }
}
