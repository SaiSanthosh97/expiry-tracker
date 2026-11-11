namespace HomeTracker.Core.ShelfLife;

public enum CategoryGroup
{
    Food,
    Medicine,
    Other,
}

/// <summary>
/// A kind of item with default shelf-life rules. All values are editable defaults,
/// never safety advice; the pack label always takes priority.
/// </summary>
/// <param name="Id">Stable identifier, e.g. "sweets". Also used as the localization key.</param>
/// <param name="Group">Used for filtering (Food / Medicines / Other).</param>
/// <param name="ShelfLife">How long an unlabelled item lasts from the day it is added, if known.</param>
/// <param name="AfterOpening">How long it lasts once opened, if known.</param>
/// <param name="ReminderLeadDays">
/// Days before the last usable day to remind. Null means "use the default rule"
/// (see <see cref="ReminderLeadTime"/>).
/// </param>
public sealed record ShelfLifeCategory(
    string Id,
    CategoryGroup Group,
    ShelfLifeDuration? ShelfLife = null,
    ShelfLifeDuration? AfterOpening = null,
    int? ReminderLeadDays = null);
