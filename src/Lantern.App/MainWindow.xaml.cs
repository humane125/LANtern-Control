using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lantern.App.Services;
using Lantern.App.ViewModels;
using Lantern.Core.Control;
using Lantern.Core.Devices;
using Lantern.Core.Networking;
using Lantern.Core.Settings;

namespace Lantern.App;

public partial class MainWindow : Window
{
    private readonly DeviceRegistry registry = new();
    private readonly TrafficPolicy policy = new();
    private readonly SettingsStore settingsStore = new();
    private readonly DispatcherTimer refreshTimer;
    private readonly Dictionary<string, DeviceViewModel> deviceIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TrafficHistory trafficHistory = new(TrafficSamplingProfile.Capacity, TrafficSamplingProfile.Retention);
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
        engine = new PcapLanEngine(registry, policy);
        engine.StatusChanged += Engine_OnStatusChanged;
        refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, RefreshDevices, Dispatcher);
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;

        var view = CollectionViewSource.GetDefaultView(Devices);
        view.SortDescriptions.Add(
            new SortDescription(nameof(DeviceViewModel.TotalRate), ListSortDirection.Descending));
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        settings = await settingsStore.LoadAsync();
        var adapters = WindowsAdapterService.GetUsableAdapters();
        AdapterSelector.ItemsSource = adapters;
        AdapterSelector.SelectedIndex = adapters.Count > 0 ? 0 : -1;
        UpdateAdapterSummary();
        if (adapters.Count == 0)
        {
            SetStatus("No active IPv4 adapter with a gateway was found.", false);
            StartButton.IsEnabled = false;
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
                hints.Add(new KnownDeviceHint(mac, lastKnownIp));
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

    private void NavigationButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string targetName } ||
            FindName(targetName) is not FrameworkElement target)
        {
            return;
        }

        target.BringIntoView();
        if (target == AdapterStrip)
        {
            AdapterSelector.Focus();
        }
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
                summary.TopDeviceUploadBytesPerSecond);
            if (trafficHistory.TryAdd(sample, TrafficSamplingProfile.Interval))
            {
                TrafficChart.Samples = trafficHistory.Samples;
                ChartTopDeviceText.Text = string.IsNullOrWhiteSpace(sample.TopDevice)
                    ? "No active device traffic"
                    : $"{sample.TopDevice}  ↓ {DeviceViewModel.FormatRate(sample.TopDeviceDownloadBytesPerSecond)}  " +
                      $"↑ {DeviceViewModel.FormatRate(sample.TopDeviceUploadBytesPerSecond)}";
            }
        }

        if (controlStartedAt is { } startedAt)
        {
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            UptimeText.Text = $"Active {elapsed:hh\\:mm\\:ss}";
        }
    }

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
