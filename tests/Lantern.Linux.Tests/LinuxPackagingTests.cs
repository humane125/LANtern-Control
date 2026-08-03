namespace Lantern.Linux.Tests;

public sealed class LinuxPackagingTests
{
    [Fact]
    public void PrivilegedInstaller_PrefersTargetSystemsCapabilityTools()
    {
        var script = ReadPackagingFile("install-privileged.sh");

        Assert.True(
            script.IndexOf("setcap_tool=$(command -v setcap", StringComparison.Ordinal) <
            script.IndexOf("setcap_tool=\"$stage_dir/usr/bin/setcap\"", StringComparison.Ordinal));
        Assert.True(
            script.IndexOf("getcap_tool=$(command -v getcap", StringComparison.Ordinal) <
            script.IndexOf("getcap_tool=\"$stage_dir/usr/bin/getcap\"", StringComparison.Ordinal));
        Assert.DoesNotContain("LD_LIBRARY_PATH=", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRun_ShowsThePrivilegedInstallFailureDetails()
    {
        var script = ReadPackagingFile("AppRun");

        Assert.Contains("setup_log=$(mktemp", script, StringComparison.Ordinal);
        Assert.Contains("setup_failure=$(tail", script, StringComparison.Ordinal);
        Assert.Contains("Administrator setup failed:", script, StringComparison.Ordinal);
    }

    private static string ReadPackagingFile(string name) =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "packaging", "linux", name));
}
