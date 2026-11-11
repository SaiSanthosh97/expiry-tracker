namespace HomeTracker.Core.ShelfLife;

public enum ShelfLifeUnit
{
    Days,
    Weeks,
    Months,
}

/// <summary>
/// How long something stays usable, e.g. "3 days" or "12 months".
/// The start day counts as the first usable day, so "good for 3 days" from Monday
/// means Monday, Tuesday and Wednesday. This is the conservative reading.
/// </summary>
public readonly record struct ShelfLifeDuration
{
    public ShelfLifeDuration(int amount, ShelfLifeUnit unit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);
        Amount = amount;
        Unit = unit;
    }

    public int Amount { get; }

    public ShelfLifeUnit Unit { get; }

    public static ShelfLifeDuration Days(int amount) => new(amount, ShelfLifeUnit.Days);

    public static ShelfLifeDuration Weeks(int amount) => new(amount, ShelfLifeUnit.Weeks);

    public static ShelfLifeDuration Months(int amount) => new(amount, ShelfLifeUnit.Months);

    /// <summary>Last day the item is still usable when the period starts on <paramref name="start"/>.</summary>
    public DateOnly LastUsableDay(DateOnly start) => Unit switch
    {
        ShelfLifeUnit.Days => start.AddDays(Amount - 1),
        ShelfLifeUnit.Weeks => start.AddDays((Amount * 7) - 1),
        ShelfLifeUnit.Months => start.AddMonths(Amount).AddDays(-1),
        _ => throw new InvalidOperationException($"Unknown unit {Unit}."),
    };

    /// <summary>Rough length in days, used only to pick a reminder lead time.</summary>
    public int ApproximateDays => Unit switch
    {
        ShelfLifeUnit.Days => Amount,
        ShelfLifeUnit.Weeks => Amount * 7,
        ShelfLifeUnit.Months => Amount * 30,
        _ => throw new InvalidOperationException($"Unknown unit {Unit}."),
    };

    public override string ToString() => $"{Amount} {Unit.ToString().ToLowerInvariant()}";
}
