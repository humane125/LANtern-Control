using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using Lantern.Core.Control;

namespace Lantern.Core.Devices;

public sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<string, DeviceRecord> devices =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object snapshotSync = new();
    private DateTimeOffset lastSnapshot;

    public DeviceRegistry(DateTimeOffset? startedAt = null)
    {
        lastSnapshot = startedAt ?? DateTimeOffset.UtcNow;
    }

    public void Observe(
        IPAddress ipAddress,
        PhysicalAddress macAddress,
        DateTimeOffset seenAt,
        string? hostName = null) =>
        AddOrUpdate(ipAddress, macAddress, seenAt, hostName, refreshLastSeen: true);

    public void Remember(
        IPAddress ipAddress,
        PhysicalAddress macAddress,
        DateTimeOffset rememberedAt,
        string? hostName = null) =>
        AddOrUpdate(ipAddress, macAddress, rememberedAt, hostName, refreshLastSeen: false);

    private void AddOrUpdate(
        IPAddress ipAddress,
        PhysicalAddress macAddress,
        DateTimeOffset timestamp,
        string? hostName,
        bool refreshLastSeen)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);
        ArgumentNullException.ThrowIfNull(macAddress);
        var key = TrafficPolicy.NormalizeMac(macAddress.ToString());
        devices.AddOrUpdate(
            key,
            _ => new DeviceRecord
            {
                MacAddress = macAddress,
                IpAddress = ipAddress,
                HostName = hostName,
                FirstSeen = timestamp,
                LastSeen = timestamp,
            },
            (_, existing) =>
            {
                existing.IpAddress = ipAddress;
                if (refreshLastSeen)
                {
                    existing.LastSeen = timestamp;
                }

                if (!string.IsNullOrWhiteSpace(hostName))
                {
                    existing.HostName = hostName;
                }

                return existing;
            });
    }

    public void SetHostName(PhysicalAddress macAddress, string? hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return;
        }

        var key = TrafficPolicy.NormalizeMac(macAddress.ToString());
        if (devices.TryGetValue(key, out var record))
        {
            record.HostName = hostName;
        }
    }

    public void RecordTraffic(
        PhysicalAddress macAddress,
        TrafficDirection direction,
        int byteCount,
        DateTimeOffset? seenAt = null)
    {
        if (byteCount <= 0)
        {
            return;
        }

        var key = TrafficPolicy.NormalizeMac(macAddress.ToString());
        if (!devices.TryGetValue(key, out var record))
        {
            return;
        }

        record.LastSeen = seenAt ?? DateTimeOffset.UtcNow;

        if (direction == TrafficDirection.Download)
        {
            Interlocked.Add(ref record.DownloadBytes, byteCount);
        }
        else
        {
            Interlocked.Add(ref record.UploadBytes, byteCount);
        }
    }

    public IReadOnlyList<DeviceSnapshot> TakeSnapshot(DateTimeOffset now)
    {
        lock (snapshotSync)
        {
            var seconds = Math.Max(0.001, (now - lastSnapshot).TotalSeconds);
            lastSnapshot = now;
            var snapshots = new List<DeviceSnapshot>(devices.Count);
            foreach (var record in devices.Values)
            {
                var download = Interlocked.Exchange(ref record.DownloadBytes, 0);
                var upload = Interlocked.Exchange(ref record.UploadBytes, 0);
                snapshots.Add(
                    new DeviceSnapshot(
                        record.MacAddress,
                        record.IpAddress,
                        record.HostName,
                        record.FirstSeen,
                        record.LastSeen,
                        download / seconds,
                        upload / seconds));
            }

            return snapshots
                .OrderByDescending(device => device.TotalBytesPerSecond)
                .ThenBy(device => device.IpAddress.ToString(), StringComparer.Ordinal)
                .ToArray();
        }
    }

    public IReadOnlyList<DeviceSnapshot> Peek()
    {
        return devices.Values
            .Select(
                record => new DeviceSnapshot(
                    record.MacAddress,
                    record.IpAddress,
                    record.HostName,
                    record.FirstSeen,
                    record.LastSeen,
                    0,
                    0))
            .ToArray();
    }
}
