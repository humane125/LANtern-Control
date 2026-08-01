using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;

namespace Lantern.App.Services;

public sealed class ClientMappingCache
{
    private string? adapterId;

    public ConcurrentDictionary<IPAddress, PhysicalAddress> Mappings { get; } = new();

    public void BeginAdapter(string id)
    {
        if (adapterId is not null &&
            !string.Equals(adapterId, id, StringComparison.OrdinalIgnoreCase))
        {
            Mappings.Clear();
        }

        adapterId = id;
    }
}
