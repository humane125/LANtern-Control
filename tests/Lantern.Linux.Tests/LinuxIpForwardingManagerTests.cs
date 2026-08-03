using Lantern.Linux.Services;

namespace Lantern.Linux.Tests;

public sealed class LinuxIpForwardingManagerTests
{
    [Fact]
    public async Task EnabledKernelForwarding_IsDisabledAndRestored()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "1\n");

            await using (await LinuxIpForwardingManager.DisableAsync(
                             path,
                             CancellationToken.None))
            {
                Assert.Equal("0", (await File.ReadAllTextAsync(path)).Trim());
            }

            Assert.Equal("1", (await File.ReadAllTextAsync(path)).Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DisabledKernelForwarding_RemainsDisabledAfterRestore()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "0\n");

            await using (await LinuxIpForwardingManager.DisableAsync(
                             path,
                             CancellationToken.None))
            {
                Assert.Equal("0", (await File.ReadAllTextAsync(path)).Trim());
            }

            Assert.Equal("0", (await File.ReadAllTextAsync(path)).Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UnexpectedKernelValue_IsRejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "unexpected\n");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => LinuxIpForwardingManager.DisableAsync(
                    path,
                    CancellationToken.None));

            Assert.Contains("unexpected", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
