namespace Lantern.Core.Services;

public sealed record ServiceSessionSnapshot(
    string MacKey,
    string ServiceId,
    string ServiceName,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastActivity,
    TimeSpan ActiveDuration,
    long DownloadBytes,
    long UploadBytes,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    int ActiveConnections,
    bool IsActive);

public sealed record CompletedServiceSession(
    string MacKey,
    string ServiceId,
    string ServiceName,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    long DownloadBytes,
    long UploadBytes,
    int ConnectionCount)
{
    public TimeSpan ActiveDuration => EndedAt - StartedAt;
}
