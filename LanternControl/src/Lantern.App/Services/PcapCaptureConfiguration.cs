using SharpPcap;

namespace Lantern.App.Services;

public static class PcapCaptureConfiguration
{
    public static DeviceConfiguration CreateForArpDiscovery() =>
        Create(DeviceModes.Promiscuous);

    public static DeviceConfiguration CreateForForwarding() =>
        Create(DeviceModes.None);

    private static DeviceConfiguration Create(DeviceModes mode) =>
        new()
        {
            Mode = mode,
            ReadTimeout = 1,
            Snaplen = 65_536,
            BufferSize = 8 * 1024 * 1024,
            KernelBufferSize = 8 * 1024 * 1024,
            MinToCopy = 0,
            Immediate = true,
        };
}
