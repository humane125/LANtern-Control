using System.Net;

namespace Lantern.Linux.Services;

public static class HostArpCache
{
    public static Task<IReadOnlyList<ArpCacheEntry>> ReadAsync(
        string interfaceName,
        IPAddress localAddress,
        int prefixLength,
        CancellationToken cancellationToken) =>
        OperatingSystem.IsMacOS()
            ? MacArpCache.ReadAsync(interfaceName, localAddress, prefixLength, cancellationToken)
            : LinuxArpCache.ReadAsync(interfaceName, localAddress, prefixLength, cancellationToken);
}

public static class HostIpForwarding
{
    public static async Task<IAsyncDisposable> DisableAsync(CancellationToken cancellationToken) =>
        OperatingSystem.IsMacOS()
            ? await MacIpForwardingManager.DisableAsync(cancellationToken)
            : await LinuxIpForwardingManager.DisableAsync(cancellationToken);
}

public static class HostOffloads
{
    public static async Task<IAsyncDisposable> DisableAsync(
        string interfaceName,
        CancellationToken cancellationToken) =>
        OperatingSystem.IsMacOS()
            ? await MacOffloadManager.DisableAsync(interfaceName, cancellationToken)
            : await LinuxOffloadManager.DisableAsync(interfaceName, cancellationToken);
}
