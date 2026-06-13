namespace Hourglass.Timing;

public readonly record struct CountdownEffects(
    CountdownEffect First,
    CountdownEffect Second,
    CountdownEffect Third,
    CountdownEffect Fourth)
{
    public static CountdownEffects None { get; } = new(
        CountdownEffect.None,
        CountdownEffect.None,
        CountdownEffect.None,
        CountdownEffect.None);

    public bool IsEmpty =>
        this.First == CountdownEffect.None
        && this.Second == CountdownEffect.None
        && this.Third == CountdownEffect.None
        && this.Fourth == CountdownEffect.None;

    public CountdownEffects Append(CountdownEffect effect)
    {
        if (effect == CountdownEffect.None)
        {
            return this;
        }

        if (this.First == CountdownEffect.None)
        {
            return this with { First = effect };
        }

        if (this.Second == CountdownEffect.None)
        {
            return this with { Second = effect };
        }

        if (this.Third == CountdownEffect.None)
        {
            return this with { Third = effect };
        }

        if (this.Fourth == CountdownEffect.None)
        {
            return this with { Fourth = effect };
        }

        throw new InvalidOperationException("Countdown transition emitted more effects than the fixed effect buffer supports.");
    }

    public CountdownEffects Append(CountdownEffects effects)
    {
        return this
            .Append(effects.First)
            .Append(effects.Second)
            .Append(effects.Third)
            .Append(effects.Fourth);
    }
}
