using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Lantern.App.Services;
using Lantern.Core.Networking;
using SharpPcap;
using SharpPcap.LibPcap;

internal static class DiscoveryOnlyProbe
{
    public static async Task<int> RunAsync(
        IPAddress expectedGateway,
        string reportPath,
        IPAddress? targetIp = null,
        PhysicalAddress? targetMac = null)
    {
        var adapter = WindowsAdapterService.GetUsableAdapters()
            .FirstOrDefault(candidate => candidate.GatewayAddress.Equals(expectedGateway))
            ?? throw new InvalidOperationException(
                $"No active adapter using gateway {expectedGateway} was found.");
        var observations = new ConcurrentDictionary<string, ArpObservation>(StringComparer.OrdinalIgnoreCase);
        var ipObservations = new ConcurrentDictionary<string, IpObservation>(StringComparer.OrdinalIgnoreCase);
        var captureDevice = LibPcapLiveDeviceList.Instance
            .FirstOrDefault(candidate => candidate.MacAddress?.Equals(adapter.LocalMac) == true)
            ?? throw new InvalidOperationException($"Npcap could not match {adapter.Name}.");

        captureDevice.OnPacketArrival += OnPacketArrival;
        try
        {
            captureDevice.Open(PcapCaptureConfiguration.CreateForArpDiscovery());
            captureDevice.Filter = "arp or ip";
            captureDevice.StartCapture();
            if (targetIp is not null && targetMac is not null)
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    captureDevice.SendPacket(BuildUnicastArpRequest(
                        adapter.LocalMac,
                        adapter.LocalAddress,
                        targetIp,
                        targetMac));
                    await Task.Delay(TimeSpan.FromMilliseconds(150));
                }
            }

            await SendMulticastQueriesAsync(adapter.LocalAddress);

            var probesSent = 0;
            foreach (var address in IPv4DiscoveryRange.EnumerateHosts(
                         adapter.LocalAddress,
                         adapter.PrefixLength))
            {
                captureDevice.SendPacket(
                    EthernetFrameCodec.BuildArpRequest(
                        adapter.LocalMac,
                        adapter.LocalAddress,
                        address));
                probesSent++;

                await Task.Delay(TimeSpan.FromMilliseconds(40));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(800));
            await SendMulticastQueriesAsync(adapter.LocalAddress);
            await Task.Delay(TimeSpan.FromSeconds(3));
            var report = new
            {
                adapter = adapter.Name,
                localAddress = adapter.LocalAddress.ToString(),
                localMac = adapter.LocalMac.ToString(),
                gateway = adapter.GatewayAddress.ToString(),
                targetedIp = targetIp?.ToString(),
                targetedMac = targetMac?.ToString(),
                probesSent,
                observationCount = observations.Count,
                observations = observations
                    .OrderBy(pair => IPAddress.Parse(pair.Key), new IpAddressComparer())
                    .Select(pair => new
                    {
                        ip = pair.Key,
                        mac = pair.Value.Mac,
                        operation = pair.Value.Operation.ToString(),
                    })
                    .ToArray(),
                ipObservationCount = ipObservations.Count,
                ipObservations = ipObservations
                    .OrderBy(pair => IPAddress.Parse(pair.Key), new IpAddressComparer())
                    .Select(pair => new
                    {
                        ip = pair.Key,
                        sourceMac = pair.Value.SourceMac,
                        destinationMac = pair.Value.DestinationMac,
                        destination = pair.Value.Destination,
                    })
                    .ToArray(),
            };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            Console.WriteLine(json);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, json);
            return observations.Any(pair => pair.Key != adapter.GatewayAddress.ToString()) ||
                   ipObservations.Count > 0
                ? 0
                : 2;
        }
        finally
        {
            try
            {
                captureDevice.StopCapture();
            }
            catch (InvalidOperationException)
            {
            }

            captureDevice.OnPacketArrival -= OnPacketArrival;
            captureDevice.Close();
        }

        void OnPacketArrival(object sender, PacketCapture capture)
        {
            if (EthernetFrameCodec.TryParseArp(capture.Data, out var arp))
            {
                if (!arp.SenderIp.Equals(adapter.LocalAddress) &&
                    !arp.SenderMac.Equals(adapter.LocalMac))
                {
                    observations[arp.SenderIp.ToString()] =
                        new ArpObservation(arp.SenderMac.ToString(), arp.Operation);
                }

                return;
            }

            if (EthernetFrameCodec.TryParseIpv4(capture.Data, out var ipv4) &&
                !ipv4.Source.Equals(adapter.LocalAddress) &&
                !ipv4.Source.Equals(adapter.GatewayAddress) &&
                !ipv4.SourceMac.Equals(adapter.LocalMac))
            {
                ipObservations[ipv4.Source.ToString()] =
                    new IpObservation(
                        ipv4.SourceMac.ToString(),
                        ipv4.DestinationMac.ToString(),
                        ipv4.Destination.ToString());
            }
        }
    }

    private sealed record ArpObservation(string Mac, ArpOperation Operation);
    private sealed record IpObservation(
        string SourceMac,
        string DestinationMac,
        string Destination);

    private static async Task SendMulticastQueriesAsync(IPAddress localAddress)
    {
        using var client = new UdpClient(new IPEndPoint(localAddress, 0));
        client.MulticastLoopback = false;

        var mdnsQuery = Convert.FromHexString(
            "000000000001000000000000" +
            "095F7365727669636573" +
            "075F646E732D7364" +
            "045F756470" +
            "056C6F63616C" +
            "00" +
            "000C0001");
        await client.SendAsync(
            mdnsQuery,
            new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));

        var ssdpQuery = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            "ST: ssdp:all\r\n\r\n");
        await client.SendAsync(
            ssdpQuery,
            new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900));
    }

    private static byte[] BuildUnicastArpRequest(
        PhysicalAddress localMac,
        IPAddress localAddress,
        IPAddress targetIp,
        PhysicalAddress targetMac)
    {
        var frame = EthernetFrameCodec.BuildArpRequest(
            localMac,
            localAddress,
            targetIp);
        var targetBytes = targetMac.GetAddressBytes();
        targetBytes.CopyTo(frame, 0);
        targetBytes.CopyTo(frame, 32);
        return frame;
    }

    private sealed class IpAddressComparer : IComparer<IPAddress>
    {
        public int Compare(IPAddress? left, IPAddress? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var leftBytes = left.GetAddressBytes();
            var rightBytes = right.GetAddressBytes();
            for (var index = 0; index < Math.Min(leftBytes.Length, rightBytes.Length); index++)
            {
                var comparison = leftBytes[index].CompareTo(rightBytes[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return leftBytes.Length.CompareTo(rightBytes.Length);
        }
    }
}
