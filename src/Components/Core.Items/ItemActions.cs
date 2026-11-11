using HomeTracker.Core.ShelfLife;

namespace HomeTracker.Core.Items;

/// <summary>State changes on an item. Each returns a new copy with <see cref="TrackedItem.UpdatedAt"/> set.</summary>
public static class ItemActions
{
    /// <param name="afterOpening">Rule chosen by the user; null keeps the category's rule.</param>
    public static TrackedItem MarkOpened(TrackedItem item, DateOnly today, ShelfLifeDuration? afterOpening, DateTimeOffset now) =>
        item with
        {
            OpenedDate = today,
            AfterOpening = afterOpening ?? item.AfterOpening,
            SnoozedUntil = null,
            UpdatedAt = now,
        };

    public static TrackedItem Close(TrackedItem item, ClosedReason reason, DateOnly today, DateTimeOffset now) =>
        item with { ClosedDate = today, ClosedReason = reason, UpdatedAt = now };

    /// <summary>Undo of <see cref="Close"/>.</summary>
    public static TrackedItem Reopen(TrackedItem item, DateTimeOffset now) =>
        item with { ClosedDate = null, ClosedReason = null, UpdatedAt = now };

    /// <summary>"Remind tomorrow".</summary>
    public static TrackedItem SnoozeUntilTomorrow(TrackedItem item, DateOnly today, DateTimeOffset now) =>
        item with { SnoozedUntil = today.AddDays(1), UpdatedAt = now };

    public static TrackedItem Delete(TrackedItem item, DateTimeOffset now) =>
        item with { IsDeleted = true, UpdatedAt = now };
}
