namespace Lantern.App.ViewModels;

public sealed record ServiceSessionViewModel(
    string ServiceId,
    string ServiceName,
    bool IsActive,
    string StatusText,
    string DownloadRateText,
    string UploadRateText,
    string SessionDownloadText,
    string SessionUploadText,
    string TodayDownloadText,
    string TodayUploadText,
    string DurationText,
    string ConnectionCountText,
    string FirstSeenText,
    string LastActivityText,
    double TotalRate);
