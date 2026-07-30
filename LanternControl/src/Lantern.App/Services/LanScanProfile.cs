namespace Lantern.App.Services;

public sealed record LanScanProfile(
    TimeSpan ProbeInterval,
    TimeSpan AutomaticRescanInterval)
{
    public static LanScanProfile HomeRouterSafe { get; } =
        new(
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMinutes(1));
}
