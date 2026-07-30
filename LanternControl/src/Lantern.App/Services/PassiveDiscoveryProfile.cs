namespace Lantern.App.Services;

public sealed record PassiveDiscoveryProfile(TimeSpan RefreshInterval)
{
    public static PassiveDiscoveryProfile Default { get; } =
        new(TimeSpan.FromSeconds(5));
}
