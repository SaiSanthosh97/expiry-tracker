namespace HomeTracker.Core.ShelfLife;

/// <summary>
/// Default rule for how many days before expiry to remind:
/// 1 day for short-life items (a week or less), 2 days for everything else.
/// </summary>
public static class ReminderLeadTime
{
    public const int ShortLifeDays = 1;
    public const int DefaultDays = 2;
    public const int ShortLifeThresholdDays = 7;

    public static int For(ShelfLifeCategory? category, ShelfLifeDuration? effectiveShelfLife)
    {
        if (category?.ReminderLeadDays is int explicitDays)
        {
            return explicitDays;
        }

        var shelfLife = effectiveShelfLife ?? category?.ShelfLife;
        return shelfLife is { } duration && duration.ApproximateDays <= ShortLifeThresholdDays
            ? ShortLifeDays
            : DefaultDays;
    }
}
