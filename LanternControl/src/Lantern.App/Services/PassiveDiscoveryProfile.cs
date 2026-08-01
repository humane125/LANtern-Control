namespace Lantern.App.Services;

public sealed record PassiveDiscoveryProfile(
    TimeSpan RefreshInterval,
    bool ProbeSubnetOnRefresh)
{
    public static PassiveDiscoveryProfile Default { get; } =
        new(TimeSpan.FromSeconds(5), false);
}
