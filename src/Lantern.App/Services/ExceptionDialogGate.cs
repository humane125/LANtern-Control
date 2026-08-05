namespace Lantern.App.Services;

public sealed class ExceptionDialogGate
{
    private int entered;

    public bool TryEnter() => Interlocked.Exchange(ref entered, 1) == 0;
}
