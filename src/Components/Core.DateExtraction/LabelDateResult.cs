using HomeTracker.Core.ShelfLife;

namespace HomeTracker.Core.DateExtraction;

public enum ExtractionConfidence
{
    /// <summary>Nothing usable found; ask the user.</summary>
    None,
    /// <summary>A guess (e.g. unlabelled dates). Show it, but expect corrections.</summary>
    Low,
    /// <summary>Calculated from manufacture date + "best before N months", or read with a day/month swap.</summary>
    Medium,
    /// <summary>A clearly labelled expiry date.</summary>
    High,
}

/// <summary>
/// What was read from a label. The UI must always ask the user to confirm
/// <see cref="ExpiryLastUsableDay"/>; even <see cref="ExtractionConfidence.High"/> results can be misread.
/// </summary>
public sealed record LabelDateResult
{
    public static readonly LabelDateResult Empty = new();

    /// <summary>Last day the item may be used, or null if it couldn't be worked out.</summary>
    public DateOnly? ExpiryLastUsableDay { get; init; }

    public ExtractionConfidence Confidence { get; init; }

    public LabelDate? Expiry { get; init; }

    public LabelDate? Manufacture { get; init; }

    /// <summary>"Best before N months from manufacture/packing" or "shelf life N months".</summary>
    public ShelfLifeDuration? BestBeforeFromManufacture { get; init; }

    /// <summary>"Use within N days of opening".</summary>
    public ShelfLifeDuration? AfterOpening { get; init; }

    public IReadOnlyList<LabelDate> AllDates { get; init; } = [];
}
