using HomeTracker.Core.ShelfLife;

namespace HomeTracker.Core.ExpiryEngine;

/// <summary>Everything known about an item's dates. Any combination may be present.</summary>
public sealed record ExpiryInputs
{
    /// <summary>Printed "EXP / USE BY / BEST BEFORE" date, already resolved to its last usable day.</summary>
    public DateOnly? PrintedExpiry { get; init; }

    public DateOnly? ManufactureDate { get; init; }

    /// <summary>"Best before N months from manufacture".</summary>
    public ShelfLifeDuration? BestBeforeFromManufacture { get; init; }

    public DateOnly? AddedDate { get; init; }

    /// <summary>Custom "good for N days" for unlabelled items, counted from <see cref="AddedDate"/>.</summary>
    public ShelfLifeDuration? ShelfLifeFromAdded { get; init; }

    public DateOnly? OpenedDate { get; init; }

    public ShelfLifeDuration? AfterOpening { get; init; }
}
