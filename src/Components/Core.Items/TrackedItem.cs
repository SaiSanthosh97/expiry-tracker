using HomeTracker.Core.ShelfLife;

namespace HomeTracker.Core.Items;

public enum ClosedReason
{
    UsedUp,
    ThrownAway,
}

/// <summary>Something in the house that can expire: a strip of tablets, a box of sweets, a bottle of ketchup.</summary>
public sealed record TrackedItem
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>A <see cref="ShelfLifeCategory.Id"/>.</summary>
    public required string CategoryId { get; init; }

    public int Quantity { get; init; } = 1;

    public DateOnly AddedDate { get; init; }

    /// <summary>Printed expiry, already resolved to its last usable day.</summary>
    public DateOnly? PrintedExpiry { get; init; }

    public DateOnly? ManufactureDate { get; init; }

    public ShelfLifeDuration? BestBeforeFromManufacture { get; init; }

    /// <summary>"Good for N days" for items without a printed date.</summary>
    public ShelfLifeDuration? ShelfLifeFromAdded { get; init; }

    public DateOnly? OpenedDate { get; init; }

    /// <summary>After-opening rule chosen for this item. When null, the category's rule applies.</summary>
    public ShelfLifeDuration? AfterOpening { get; init; }

    /// <summary>Overrides the category's reminder lead time for this item.</summary>
    public int? ReminderLeadDays { get; init; }

    /// <summary>"Remind tomorrow": no reminder before this day.</summary>
    public DateOnly? SnoozedUntil { get; init; }

    public DateOnly? ClosedDate { get; init; }

    public ClosedReason? ClosedReason { get; init; }

    public string? PhotoPath { get; init; }

    /// <summary>Last change time; the newer copy wins when merging imports (and, later, sync).</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    public bool IsDeleted { get; init; }

    public bool IsClosed => ClosedDate is not null;

    public static string NewId() => Guid.NewGuid().ToString("N");
}
