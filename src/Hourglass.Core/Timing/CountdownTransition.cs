namespace Hourglass.Timing;

public sealed record CountdownTransition(
    CountdownState State,
    CountdownEffects Effects,
    bool Succeeded)
{
    public static CountdownTransition Success(CountdownState state, CountdownEffects effects)
    {
        return new CountdownTransition(state, effects, true);
    }

    public static CountdownTransition Invalid(CountdownState state)
    {
        return new CountdownTransition(state, CountdownEffects.None, false);
    }

    public static CountdownTransition Unchanged(CountdownState state)
    {
        return Success(state, CountdownEffects.None);
    }
}
