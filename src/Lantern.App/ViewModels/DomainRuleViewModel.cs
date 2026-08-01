using System.ComponentModel;

namespace Lantern.App.ViewModels;

public sealed class DomainRuleViewModel : INotifyPropertyChanged
{
    private string deviceName;

    public DomainRuleViewModel(string macKey, string deviceName, string domain)
    {
        MacKey = macKey;
        this.deviceName = deviceName;
        Domain = domain;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string MacKey { get; }

    public string DeviceName
    {
        get => deviceName;
        private set
        {
            if (string.Equals(deviceName, value, StringComparison.Ordinal))
            {
                return;
            }

            deviceName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeviceName)));
        }
    }

    public string Domain { get; }

    public string ScopeText => "Includes subdomains";

    public void UpdateDeviceName(string value)
    {
        DeviceName = value;
    }
}
