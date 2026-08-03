using Lantern.Linux.Services;

namespace Lantern.Linux.Tests;

public sealed class LinuxOffloadManagerTests
{
    [Fact]
    public void ParseEnabledFeatures_ReturnsOnlyMutableFrameCoalescingFeatures()
    {
        const string output = """
            Features for wlp2s0:
            rx-checksumming: on
            tcp-segmentation-offload: on
                tx-tcp-segmentation: on
            generic-segmentation-offload: on
            generic-receive-offload: on
            large-receive-offload: off [fixed]
            rx-gro-list: on
            rx-udp-gro-forwarding: on
            """;

        var features = LinuxOffloadManager.ParseEnabledFeatures(output);

        Assert.Equal(["tso", "gso", "gro", "rx-gro-list", "rx-udp-gro-forwarding"], features);
    }

    [Fact]
    public void ParseEnabledFeatures_IgnoresFeaturesThatAreOffOrFixed()
    {
        const string output = """
            tcp-segmentation-offload: off
            generic-segmentation-offload: off
            generic-receive-offload: on [fixed]
            large-receive-offload: off [fixed]
            """;

        Assert.Empty(LinuxOffloadManager.ParseEnabledFeatures(output));
    }
}
