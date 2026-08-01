namespace Lantern.Core.Control;

public readonly record struct InterceptionTransition(
    InterceptionTargets Restore,
    InterceptionTargets Poison)
{
    public static InterceptionTransition Between(
        InterceptionTargets previous,
        InterceptionTargets current) =>
        new(previous & ~current, current);
}
