namespace HomeTracker.Core.ExpiryEngine;

public enum ExpirySource
{
    Printed,
    ManufacturePlusShelfLife,
    AddedPlusShelfLife,
    AfterOpening,
}

/// <param name="LastUsableDay">Last day the item may be used. Null when no date information exists.</param>
/// <param name="Source">Which rule produced the date (shown to the user as the reason).</param>
public sealed record ExpiryResult(DateOnly? LastUsableDay, ExpirySource? Source)
{
    public static readonly ExpiryResult Unknown = new(null, null);
}

public static class ExpiryCalculator
{
    /// <summary>The effective expiry is the earliest of every date rule that applies.</summary>
    public static ExpiryResult Calculate(ExpiryInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var candidates = new List<(DateOnly Day, ExpirySource Source)>(4);

        if (inputs.PrintedExpiry is { } printed)
        {
            candidates.Add((printed, ExpirySource.Printed));
        }

        if (inputs is { ManufactureDate: { } made, BestBeforeFromManufacture: { } bestBefore })
        {
            candidates.Add((bestBefore.LastUsableDay(made), ExpirySource.ManufacturePlusShelfLife));
        }

        if (inputs is { AddedDate: { } added, ShelfLifeFromAdded: { } shelfLife })
        {
            candidates.Add((shelfLife.LastUsableDay(added), ExpirySource.AddedPlusShelfLife));
        }

        if (inputs is { OpenedDate: { } opened, AfterOpening: { } afterOpening })
        {
            candidates.Add((afterOpening.LastUsableDay(opened), ExpirySource.AfterOpening));
        }

        if (candidates.Count == 0)
        {
            return ExpiryResult.Unknown;
        }

        var earliest = candidates.MinBy(c => c.Day);
        return new ExpiryResult(earliest.Day, earliest.Source);
    }
}
