namespace HomeTracker.Core.DateExtraction;

public enum DateRole
{
    /// <summary>No label found next to the date.</summary>
    Unknown,
    Expiry,
    Manufacture,
}

public enum DatePrecision
{
    Day,
    /// <summary>Only month and year printed, e.g. "05/2027".</summary>
    Month,
}

/// <summary>A date found in label text.</summary>
/// <param name="Date">The date; for <see cref="DatePrecision.Month"/> this is the first day of the month.</param>
/// <param name="Index">Position in the original text, for highlighting.</param>
/// <param name="Text">The exact text that was read as a date.</param>
/// <param name="IsDayMonthSwapped">True when the date was read as month/day/year because the middle number was over 12.</param>
public sealed record LabelDate(
    DateRole Role,
    DateOnly Date,
    DatePrecision Precision,
    int Index,
    string Text,
    bool IsDayMonthSwapped = false)
{
    /// <summary>Last day covered by this date: the day itself, or the last day of the month.</summary>
    public DateOnly LastDay => Precision == DatePrecision.Day
        ? Date
        : new DateOnly(Date.Year, Date.Month, DateTime.DaysInMonth(Date.Year, Date.Month));
}
