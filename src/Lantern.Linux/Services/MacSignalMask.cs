using System.Runtime.InteropServices;

namespace Lantern.Linux.Services;

public static class MacSignalMask
{
    private const int DarwinSigSetMask = 3;

    [DllImport("libc", EntryPoint = "pthread_sigmask")]
    private static extern int SetThreadSignalMask(int how, in uint set, IntPtr oldSet);

    // The macOS administrator prompt starts the app with SIGCHLD blocked. .NET needs
    // SIGCHLD to notice child processes exit, so every ifconfig/sysctl wait would hang.
    public static void UnblockAll() => SetThreadSignalMask(DarwinSigSetMask, 0, IntPtr.Zero);
}
