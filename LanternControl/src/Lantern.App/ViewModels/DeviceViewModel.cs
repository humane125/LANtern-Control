using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lantern.Core.Devices;
using Lantern.Core.Settings;

namespace Lantern.App.ViewModels;

public sealed class DeviceViewModel : INotifyPropertyChanged
{
    private readonly Func<DeviceViewModel, Task> changed;
    private string ipAddress = string.Empty;
    private string macAddress = string.Empty;
    private string displayName = "Unknown device";
    private string downloadRate = "0 B/s";
    private string uploadRate = "0 B/s";
    private double downloadBytesPerSecond;
    private double uploadBytesPerSecond;
    private double totalRate;
    private int downloadLimit;
    private int uploadLimit;
    private bool pauseInternet;
    private bool isProtected;
    private string protectedReason = string.Empty;

    public DeviceViewModel(Func<DeviceViewModel, Task> changed)
    {
        this.changed = changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string IpAddress
    {
        get => ipAddress;
        private set => SetField(ref ipAddress, value);
    }

    public string MacAddress
    {
        get => macAddress;
        private set => SetField(ref macAddress, value);
    }

    public string MacKey { get; private set; } = string.Empty;

    public string DisplayName
    {
        get => displayName;
        private set => SetField(ref displayName, value);
    }

    public string DownloadRate
    {
        get => downloadRate;
        private set => SetField(ref downloadRate, value);
    }

    public string UploadRate
    {
        get => uploadRate;
        private set => SetField(ref uploadRate, value);
    }

    public double DownloadBytesPerSecond
    {
        get => downloadBytesPerSecond;
        private set => SetField(ref downloadBytesPerSecond, value);
    }

    public double UploadBytesPerSecond
    {
        get => uploadBytesPerSecond;
        private set => SetField(ref uploadBytesPerSecond, value);
    }

    public double TotalRate
    {
        get => totalRate;
        private set => SetField(ref totalRate, value);
    }

    public int DownloadLimit
    {
        get => downloadLimit;
        set
        {
            if (SetField(ref downloadLimit, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(HasActiveRule));
                _ = changed(this);
            }
        }
    }

    public int UploadLimit
    {
        get => uploadLimit;
        set
        {
            if (SetField(ref uploadLimit, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(HasActiveRule));
                _ = changed(this);
            }
        }
    }

    public bool PauseInternet
    {
        get => pauseInternet;
        set
        {
            if (SetField(ref pauseInternet, value))
            {
                OnPropertyChanged(nameof(HasActiveRule));
                _ = changed(this);
            }
        }
    }

    public bool IsProtected
    {
        get => isProtected;
        private set
        {
            if (SetField(ref isProtected, value))
            {
                OnPropertyChanged(nameof(CanControl));
                OnPropertyChanged(nameof(HasActiveRule));
            }
        }
    }

    public bool CanControl => !IsProtected;

    public bool HasActiveRule =>
        CanControl && (PauseInternet || DownloadLimit > 0 || UploadLimit > 0);

    public string ProtectedReason
    {
        get => protectedReason;
        private set => SetField(ref protectedReason, value);
    }

    public void Initialize(
        DeviceSnapshot snapshot,
        DevicePreferences? preferences,
        bool isProtectedDevice,
        string protectedDeviceReason)
    {
        MacKey = snapshot.MacAddress.ToString();
        MacAddress = FormatMac(MacKey);
        downloadLimit = Math.Max(0, preferences?.DownloadKiloBytesPerSecond ?? 0);
        uploadLimit = Math.Max(0, preferences?.UploadKiloBytesPerSecond ?? 0);
        pauseInternet = !isProtectedDevice && (preferences?.PauseInternet ?? false);
        OnPropertyChanged(nameof(DownloadLimit));
        OnPropertyChanged(nameof(UploadLimit));
        OnPropertyChanged(nameof(PauseInternet));
        OnPropertyChanged(nameof(HasActiveRule));
        IsProtected = isProtectedDevice;
        ProtectedReason = protectedDeviceReason;
        Update(snapshot, preferences?.Alias);
    }

    public void Update(DeviceSnapshot snapshot, string? alias)
    {
        IpAddress = snapshot.IpAddress.ToString();
        DisplayName = !string.IsNullOrWhiteSpace(alias)
            ? alias
            : !string.IsNullOrWhiteSpace(snapshot.HostName)
                ? snapshot.HostName
                : $"Device {IpAddress}";
        DownloadBytesPerSecond = snapshot.DownloadBytesPerSecond;
        UploadBytesPerSecond = snapshot.UploadBytesPerSecond;
        DownloadRate = FormatRate(snapshot.DownloadBytesPerSecond);
        UploadRate = FormatRate(snapshot.UploadBytesPerSecond);
        TotalRate = snapshot.TotalBytesPerSecond;
    }

    public static string FormatRate(double bytesPerSecond)
    {
        return bytesPerSecond switch
        {
            >= 1_000_000 => $"{bytesPerSecond / 1_000_000:0.0} MB/s",
            >= 1_000 => $"{bytesPerSecond / 1_000:0.0} KB/s",
            _ => $"{bytesPerSecond:0} B/s",
        };
    }

    private static string FormatMac(string value) =>
        string.Join(":", Enumerable.Range(0, 6).Select(index => value.Substring(index * 2, 2)));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
