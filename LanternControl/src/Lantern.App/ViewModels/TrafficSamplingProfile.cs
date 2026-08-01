namespace Lantern.App.ViewModels;

public static class TrafficSamplingProfile
{
    public static TimeSpan Interval { get; } = TimeSpan.FromSeconds(1);

    public static TimeSpan Retention { get; } = TimeSpan.FromHours(1);

    public const int Capacity = 3_600;
}
