using System.Runtime.ExceptionServices;

namespace Lantern.App.Services;

public static class ForwardingShutdown
{
    public static async Task RunAsync(
        Func<Task> restorePeers,
        Action stopForwarding,
        Func<Task> awaitWorkers)
    {
        Exception? restorationFailure = null;
        try
        {
            await restorePeers();
        }
        catch (Exception exception)
        {
            restorationFailure = exception;
        }
        finally
        {
            stopForwarding();
            await awaitWorkers();
        }

        if (restorationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(restorationFailure).Throw();
        }
    }
}
