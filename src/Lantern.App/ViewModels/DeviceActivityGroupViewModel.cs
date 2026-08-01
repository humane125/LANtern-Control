using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lantern.App.ViewModels;

public sealed class DeviceActivityGroupViewModel : INotifyPropertyChanged
{
    private string deviceName;
    private string ipAddress;
    private bool isExpanded;

    public DeviceActivityGroupViewModel(string macKey, string deviceName, string ipAddress)
    {
        MacKey = macKey;
        this.deviceName = deviceName;
        this.ipAddress = ipAddress;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string MacKey { get; }

    public string DeviceName
    {
        get => deviceName;
        private set => SetField(ref deviceName, value);
    }

    public string IpAddress
    {
        get => ipAddress;
        private set => SetField(ref ipAddress, value);
    }

    public ObservableCollection<DeviceActivityViewModel> Domains { get; } = [];

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetField(ref isExpanded, value);
    }

    public bool HasDomains => Domains.Count > 0;

    public string DomainCountText => Domains.Count switch
    {
        0 => "No domains yet",
        1 => "1 domain",
        _ => $"{Domains.Count} domains",
    };

    public string LastSeenText => Domains.Count == 0
        ? "Waiting for activity"
        : $"Last seen {Domains.Max(activity => activity.LastSeen).ToLocalTime():HH:mm:ss}";

    public void UpdateIdentity(string latestDeviceName, string latestIpAddress)
    {
        DeviceName = latestDeviceName;
        IpAddress = latestIpAddress;
    }

    public void AddDomain(DeviceActivityViewModel activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        Domains.Insert(0, activity);
        RaiseDomainSummaryChanged();
    }

    public void TouchDomain(DeviceActivityViewModel activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var index = Domains.IndexOf(activity);
        if (index > 0)
        {
            Domains.Move(index, 0);
        }

        RaiseDomainSummaryChanged();
    }

    public void RemoveDomain(DeviceActivityViewModel activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (Domains.Remove(activity))
        {
            RaiseDomainSummaryChanged();
        }
    }

    public void ClearDomains()
    {
        if (Domains.Count == 0)
        {
            return;
        }

        Domains.Clear();
        RaiseDomainSummaryChanged();
    }

    private void RaiseDomainSummaryChanged()
    {
        OnPropertyChanged(nameof(HasDomains));
        OnPropertyChanged(nameof(DomainCountText));
        OnPropertyChanged(nameof(LastSeenText));
    }

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
