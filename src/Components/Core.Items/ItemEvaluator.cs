using HomeTracker.Core.ExpiryEngine;
using HomeTracker.Core.ShelfLife;

namespace HomeTracker.Core.Items;

/// <param name="Category">Null when the item's category no longer exists.</param>
/// <param name="ReminderDay">Day the "use soon" reminder is due, or null when none is needed.</param>
public sealed record ItemEvaluation(
    TrackedItem Item,
    ShelfLifeCategory? Category,
    ExpiryResult Expiry,
    ExpiryStatus Status,
    int ReminderLeadDays,
    DateOnly? ReminderDay);

/// <summary>Works out an item's real expiry and status from its own dates and its category's rules.</summary>
public sealed class ItemEvaluator(ShelfLifeCatalog catalog)
{
    public ItemEvaluation Evaluate(TrackedItem item, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(item);

        var category = catalog.Find(item.CategoryId);
        var afterOpening = item.AfterOpening ?? category?.AfterOpening;

        var expiry = ExpiryCalculator.Calculate(new ExpiryInputs
        {
            PrintedExpiry = item.PrintedExpiry,
            ManufactureDate = item.ManufactureDate,
            BestBeforeFromManufacture = item.BestBeforeFromManufacture,
            AddedDate = item.AddedDate,
            ShelfLifeFromAdded = item.ShelfLifeFromAdded,
            OpenedDate = item.OpenedDate,
            AfterOpening = afterOpening,
        });

        var effectiveShelfLife = item.OpenedDate is not null ? afterOpening : item.ShelfLifeFromAdded;
        var leadDays = item.ReminderLeadDays ?? ReminderLeadTime.For(category, effectiveShelfLife);

        var status = ExpiryStatusEvaluator.Evaluate(expiry.LastUsableDay, today, leadDays, item.IsClosed);
        var reminderDay = ExpiryStatusEvaluator.ReminderDay(expiry.LastUsableDay, today, leadDays, item.IsClosed);

        return new ItemEvaluation(item, category, expiry, status, leadDays, reminderDay);
    }

    public IReadOnlyList<ItemEvaluation> EvaluateAll(IEnumerable<TrackedItem> items, DateOnly today) =>
        items.Where(i => !i.IsDeleted).Select(i => Evaluate(i, today)).ToList();
}
