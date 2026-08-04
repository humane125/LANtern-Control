using System.ComponentModel;

namespace Lantern.App.ViewModels;

public sealed class DeviceServiceGroupViewModel(
    string macKey,
    string deviceName,
    string ipAddress,
    IReadOnlyList<ServiceSessionViewModel> services,
    bool isExpanded) : INotifyPropertyChanged
{
    private bool isExpanded = isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string MacKey { get; } = macKey;

    public string DeviceName { get; } = deviceName;

    public string IpAddress { get; } = ipAddress;

    public IReadOnlyList<ServiceSessionViewModel> Services { get; } = services;

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value)
            {
                return;
            }

            isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public string ServiceCountText => Services.Count switch
    {
        0 => "No services yet",
        1 => "1 service",
        _ => $"{Services.Count} services",
    };

    public string LastActivityText => Services.Count == 0
        ? "Waiting for activity"
        : $"Last activity {Services.Max(service => service.LastActivityText)}";
}
