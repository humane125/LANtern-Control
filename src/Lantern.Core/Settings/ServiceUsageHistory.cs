namespace Lantern.Core.Settings;

public sealed class ServiceUsageHistory
{
    public int Version { get; init; } = 1;

    public List<ServiceUsageDay> Days { get; init; } = [];

    public ServiceUsageAggregate? Find(DateOnly date, string macKey, string serviceId) =>
        Days.FirstOrDefault(day => day.Date == date)?.Services.FirstOrDefault(service =>
            service.MacKey.Equals(macKey, StringComparison.OrdinalIgnoreCase) &&
            service.ServiceId.Equals(serviceId, StringComparison.OrdinalIgnoreCase));
}

public sealed class ServiceUsageDay
{
    public DateOnly Date { get; init; }

    public List<ServiceUsageAggregate> Services { get; init; } = [];
}

public sealed class ServiceUsageAggregate
{
    public string MacKey { get; init; } = string.Empty;

    public string ServiceId { get; init; } = string.Empty;

    public string ServiceName { get; set; } = string.Empty;

    public long DownloadBytes { get; set; }

    public long UploadBytes { get; set; }

    public TimeSpan ActiveDuration { get; set; }

    public int SessionCount { get; set; }

    public DateTimeOffset LastActivity { get; set; }
}
