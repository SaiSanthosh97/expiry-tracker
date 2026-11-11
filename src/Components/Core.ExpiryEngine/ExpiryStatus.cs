namespace HomeTracker.Core.ExpiryEngine;

public enum ExpiryState
{
    /// <summary>No date known; the item cannot be tracked.</summary>
    Unknown,
    Fresh,
    UseSoon,
    Expired,
    /// <summary>Used up or thrown away.</summary>
    Closed,
}

/// <param name="State">Overall state (drives colour: green / orange / red).</param>
/// <param name="DaysLeft">Days until the last usable day: 0 = last day today, negative = expired that many days ago.</param>
public sealed record ExpiryStatus(ExpiryState State, int? DaysLeft);

public static class ExpiryStatusEvaluator
{
    public static ExpiryStatus Evaluate(DateOnly? lastUsableDay, DateOnly today, int reminderLeadDays, bool isClosed = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reminderLeadDays);

        if (isClosed)
        {
            return new ExpiryStatus(ExpiryState.Closed, null);
        }

        if (lastUsableDay is not { } last)
        {
            return new ExpiryStatus(ExpiryState.Unknown, null);
        }

        var daysLeft = last.DayNumber - today.DayNumber;
        var state = daysLeft switch
        {
            < 0 => ExpiryState.Expired,
            _ when daysLeft <= reminderLeadDays => ExpiryState.UseSoon,
            _ => ExpiryState.Fresh,
        };
        return new ExpiryStatus(state, daysLeft);
    }

    /// <summary>
    /// Day the "use soon" reminder should fire: <paramref name="reminderLeadDays"/> before the last usable day,
    /// or today if that day has already passed. Null when there is nothing to remind about.
    /// </summary>
    public static DateOnly? ReminderDay(DateOnly? lastUsableDay, DateOnly today, int reminderLeadDays, bool isClosed = false)
    {
        if (isClosed || lastUsableDay is not { } last || last < today)
        {
            return null;
        }

        var reminder = last.AddDays(-reminderLeadDays);
        return reminder < today ? today : reminder;
    }
}
