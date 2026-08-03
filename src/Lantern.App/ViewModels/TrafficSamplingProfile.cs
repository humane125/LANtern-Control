namespace Lantern.App.ViewModels;

public static class TrafficSamplingProfile
{
    public static TimeSpan Interval { get; } = TimeSpan.FromMilliseconds(2500);

    public static TimeSpan Retention { get; } = TimeSpan.FromMinutes(10);

    public const int Capacity = 240;
}
