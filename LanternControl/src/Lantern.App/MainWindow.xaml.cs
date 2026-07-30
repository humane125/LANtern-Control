using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    private PcapLanEngine engine;
    private AppSettings settings = new();
    private bool operationInProgress;
    private bool closeAfterStop;

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
            await engine.StartAsync(selected, CancellationToken.None);
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
            SetStatus("Scanning this LAN…", null);
            await engine.ScanAsync();
            SetStatus("Control active", true);
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
        foreach (var snapshot in registry.TakeSnapshot(DateTimeOffset.UtcNow))
        {
            var key = snapshot.MacAddress.ToString();
            settings.Devices.TryGetValue(key, out var preferences);
            var isGateway = activeProfile is not null &&
                            snapshot.IpAddress.Equals(activeProfile.GatewayAddress);
            if (!deviceIndex.TryGetValue(key, out var viewModel))
            {
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
        }

        CollectionViewSource.GetDefaultView(Devices).Refresh();
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
        preferences.DownloadKiloBytesPerSecond = device.DownloadLimit;
        preferences.UploadKiloBytesPerSecond = device.UploadLimit;
        preferences.PauseInternet = device.PauseInternet;

        try
        {
            await engine.ApplyRuleAsync(
                device.MacKey,
                new TrafficRule(
                    device.PauseInternet,
                    device.DownloadLimit,
                    device.UploadLimit));
            await settingsStore.SaveAsync(settings);
            await Dispatcher.InvokeAsync(
                () => DetailStatusText.Text =
                    engine.IsRunning
                        ? $"Rule applied to {device.DisplayName}."
                        : $"Rule saved for {device.DisplayName}; it activates when control starts.");
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
                    pair.Value.UploadKiloBytesPerSecond));
        }
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
                true => Color.FromRgb(73, 222, 129),
                false => Color.FromRgb(255, 111, 125),
                null => Color.FromRgb(152, 174, 194),
            });
    }
}
