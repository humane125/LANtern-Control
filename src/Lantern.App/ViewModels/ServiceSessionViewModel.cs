using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lantern.Core.Control;

namespace Lantern.App.ViewModels;

public sealed class ServiceSessionViewModel : INotifyPropertyChanged
{
    private readonly Action<ServiceTrafficRule>? ruleChanged;
    private int downloadLimit;
    private int uploadLimit;

    public ServiceSessionViewModel(
        string serviceId,
        string serviceName,
        bool isActive,
        string statusText,
        string downloadRateText,
        string uploadRateText,
        string sessionDownloadText,
        string sessionUploadText,
        string todayDownloadText,
        string todayUploadText,
        string durationText,
        string connectionCountText,
        string firstSeenText,
        string lastActivityText,
        double totalRate,
        int downloadLimit = 0,
        int uploadLimit = 0,
        Action<ServiceTrafficRule>? ruleChanged = null)
    {
        ServiceId = serviceId;
        ServiceName = serviceName;
        IsActive = isActive;
        StatusText = statusText;
        DownloadRateText = downloadRateText;
        UploadRateText = uploadRateText;
        SessionDownloadText = sessionDownloadText;
        SessionUploadText = sessionUploadText;
        TodayDownloadText = todayDownloadText;
        TodayUploadText = todayUploadText;
        DurationText = durationText;
        ConnectionCountText = connectionCountText;
        FirstSeenText = firstSeenText;
        LastActivityText = lastActivityText;
        TotalRate = totalRate;
        this.downloadLimit = Math.Max(0, downloadLimit);
        this.uploadLimit = Math.Max(0, uploadLimit);
        this.ruleChanged = ruleChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ServiceId { get; }
    public string ServiceName { get; }
    public bool IsActive { get; }
    public string StatusText { get; }
    public string DownloadRateText { get; }
    public string UploadRateText { get; }
    public string SessionDownloadText { get; }
    public string SessionUploadText { get; }
    public string TodayDownloadText { get; }
    public string TodayUploadText { get; }
    public string DurationText { get; }
    public string ConnectionCountText { get; }
    public string FirstSeenText { get; }
    public string LastActivityText { get; }
    public double TotalRate { get; }

    public int DownloadLimit
    {
        get => downloadLimit;
        set => SetLimit(ref downloadLimit, value);
    }

    public int UploadLimit
    {
        get => uploadLimit;
        set => SetLimit(ref uploadLimit, value);
    }

    public bool IsConfigured => downloadLimit > 0 || uploadLimit > 0;

    public string LimitStatusText => IsConfigured
        ? "Limited"
        : "0 = unlimited";

    private void SetLimit(ref int field, int value, [CallerMemberName] string? propertyName = null)
    {
        var normalized = Math.Max(0, value);
        if (field == normalized)
        {
            return;
        }

        field = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConfigured)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LimitStatusText)));
        ruleChanged?.Invoke(new ServiceTrafficRule(downloadLimit, uploadLimit));
    }
}
