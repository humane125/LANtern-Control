using System.Net;
using System.Net.NetworkInformation;
using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class ClientMappingCacheTests
{
    [Fact]
    public void BeginAdapter_ClearsKnownClientsWhenRestartingSameAdapter()
    {
        var cache = new ClientMappingCache();
        var address = IPAddress.Parse("192.168.31.61");
        var mac = PhysicalAddress.Parse("E261190DBD54");

        cache.BeginAdapter("wifi-adapter");
        cache.Mappings[address] = mac;
        cache.BeginAdapter("wifi-adapter");

        Assert.Empty(cache.Mappings);
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

    [Fact]
    public void Upsert_MovesOneMacToItsLatestAddress()
    {
        var cache = new ClientMappingCache();
        var mac = PhysicalAddress.Parse("E261190DBD54");
        var oldAddress = IPAddress.Parse("192.168.31.61");
        var newAddress = IPAddress.Parse("192.168.31.213");

        cache.Upsert(oldAddress, mac);
        cache.Upsert(newAddress, mac);

        Assert.False(cache.Mappings.ContainsKey(oldAddress));
        Assert.Equal(mac, cache.Mappings[newAddress]);
    }
}
