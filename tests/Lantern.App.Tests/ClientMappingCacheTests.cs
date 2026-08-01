using System.Net;
using System.Net.NetworkInformation;
using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class ClientMappingCacheTests
{
    [Fact]
    public void BeginAdapter_RetainsKnownClientsWhenRestartingSameAdapter()
    {
        var cache = new ClientMappingCache();
        var address = IPAddress.Parse("192.168.31.61");
        var mac = PhysicalAddress.Parse("E261190DBD54");

        cache.BeginAdapter("wifi-adapter");
        cache.Mappings[address] = mac;
        cache.BeginAdapter("wifi-adapter");

        Assert.Equal(mac, cache.Mappings[address]);
    }

    [Fact]
    public void BeginAdapter_ClearsKnownClientsWhenAdapterChanges()
    {
        var cache = new ClientMappingCache();
        cache.BeginAdapter("wifi-adapter");
        cache.Mappings[IPAddress.Parse("192.168.31.61")] =
            PhysicalAddress.Parse("E261190DBD54");

        cache.BeginAdapter("ethernet-adapter");

        Assert.Empty(cache.Mappings);
    }
}
