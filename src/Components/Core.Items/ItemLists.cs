using HomeTracker.Core.ExpiryEngine;
using HomeTracker.Core.ShelfLife;

namespace HomeTracker.Core.Items;

public sealed record HomeSections(
    IReadOnlyList<ItemEvaluation> Expired,
    IReadOnlyList<ItemEvaluation> UseSoon,
    IReadOnlyList<ItemEvaluation> Fresh,
    IReadOnlyList<ItemEvaluation> NoDate);

public static class ItemLists
{
    /// <summary>Home screen order: expired first, then use soon, then fresh; each by date.</summary>
    public static HomeSections ForHome(IEnumerable<ItemEvaluation> evaluations, CategoryGroup? group = null)
    {
        var open = evaluations
            .Where(e => !e.Item.IsClosed && !e.Item.IsDeleted)
            .Where(e => group is null || e.Category?.Group == group)
            .ToList();

        List<ItemEvaluation> InState(ExpiryState state) => open
            .Where(e => e.Status.State == state)
            .OrderBy(e => e.Expiry.LastUsableDay)
            .ThenBy(e => e.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new HomeSections(
            InState(ExpiryState.Expired),
            InState(ExpiryState.UseSoon),
            InState(ExpiryState.Fresh),
            InState(ExpiryState.Unknown));
    }

    /// <summary>
    /// Items to include in today's reminder: inside their reminder window (up to and including the last day),
    /// plus a one-time "expired, throw it away" the day after. Snoozed items wait until their snooze day.
    /// </summary>
    public static IReadOnlyList<ItemEvaluation> DueForReminder(IEnumerable<ItemEvaluation> evaluations, DateOnly today) =>
        evaluations
            .Where(e => !e.Item.IsClosed && !e.Item.IsDeleted)
            .Where(e => e.Item.SnoozedUntil is not { } snooze || snooze <= today)
            .Where(e => e.Status.State == ExpiryState.UseSoon
                || (e.Status.State == ExpiryState.Expired && e.Status.DaysLeft == -1))
            .OrderBy(e => e.Expiry.LastUsableDay)
            .ToList();
}
