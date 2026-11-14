namespace HomeTracker.Core.Medicines;

public enum DoseState
{
    /// <summary>Later today.</summary>
    Upcoming,

    /// <summary>Time to take it; not marked yet.</summary>
    Due,

    Taken,
    Skipped,

    /// <summary>Not marked within an hour (or half an hour after "Later").</summary>
    Missed,
}

/// <summary>One scheduled dose and where it stands.</summary>
/// <param name="MissedAt">When the dose counts as missed if nothing is marked.</param>
public sealed record DoseSlot(Medicine Medicine, DateTime ScheduledAt, DoseState State, DoseEvent? Event, DateTime MissedAt)
{
    public string Id => DoseEvent.IdFor(Medicine.Id, ScheduledAt);
}

/// <summary>
/// The dose rules: remind at the dose time and again after 10 and 30 minutes; "Later" reminds again after 15 minutes;
/// a dose not marked within 60 minutes (or 30 minutes after a "Later" reminder) is missed.
/// </summary>
public static class DoseRules
{
    public static readonly TimeSpan[] RemindAgainAfter = [TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];
    public static readonly TimeSpan MissedAfter = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan LaterBy = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MissedAfterLater = TimeSpan.FromMinutes(30);

    public static DoseSlot Evaluate(Medicine medicine, DateTime scheduledAt, DoseEvent? doseEvent, DateTime now)
    {
        var missedAt = scheduledAt + MissedAfter;
        if (doseEvent is { Status: DoseStatus.Later, LaterUntil: { } until } && until + MissedAfterLater > missedAt)
        {
            missedAt = until + MissedAfterLater;
        }

        var state = doseEvent?.Status switch
        {
            DoseStatus.Taken => DoseState.Taken,
            DoseStatus.Skipped => DoseState.Skipped,
            DoseStatus.Missed => DoseState.Missed,
            _ when now < scheduledAt => DoseState.Upcoming,
            _ when now >= missedAt => DoseState.Missed,
            _ => DoseState.Due,
        };

        return new DoseSlot(medicine, scheduledAt, state, doseEvent, missedAt);
    }

    /// <summary>Scheduled dose times of a medicine between two moments (inclusive start, exclusive end).</summary>
    public static IEnumerable<DateTime> Occurrences(Medicine medicine, DateTime from, DateTime to)
    {
        if (medicine.Schedule.Kind == ScheduleKind.AsNeeded)
        {
            yield break;
        }

        for (var day = DateOnly.FromDateTime(from); day <= DateOnly.FromDateTime(to); day = day.AddDays(1))
        {
            if (!medicine.IsActiveOn(day) || !medicine.Schedule.IsDoseDay(day, medicine.StartDate))
            {
                continue;
            }

            foreach (var time in medicine.Schedule.Times.Distinct().Order())
            {
                var at = day.ToDateTime(time);
                if (at >= from && at < to && (medicine.FirstDoseFrom is not { } first || at >= first))
                {
                    yield return at;
                }
            }
        }
    }

    /// <summary>
    /// Today's doses for the given medicines, plus doses from late last night that are still within their hour,
    /// in time order.
    /// </summary>
    public static IReadOnlyList<DoseSlot> Today(IEnumerable<Medicine> medicines, IEnumerable<DoseEvent> events, DateTime now)
    {
        var byId = events.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.MaxBy(e => e.RecordedAt)!);
        var from = Min(now.Date, now - MissedAfter - MissedAfterLater);
        var to = now.Date.AddDays(1);
        return medicines
            .Where(m => !m.IsDeleted)
            .SelectMany(m => Occurrences(m, from, to)
                .Where(at => at >= now.Date || now - at < MissedAfter + MissedAfterLater)
                .Select(at => Evaluate(m, at, byId.GetValueOrDefault(DoseEvent.IdFor(m.Id, at)), now)))
            .OrderBy(s => s.ScheduledAt)
            .ThenBy(s => s.Medicine.Person, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// When the phone that rings for these medicines should wake up next: the next dose time, a "remind again"
    /// moment, the end of a "Later", or the moment a dose becomes missed. Null when nothing is scheduled in the next two days.
    /// </summary>
    public static DateTime? NextWake(IEnumerable<Medicine> medicines, IEnumerable<DoseEvent> events, DateTime now)
    {
        var list = medicines.Where(m => !m.IsDeleted).ToList();
        var byId = events.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.MaxBy(e => e.RecordedAt)!);
        var candidates = new List<DateTime>();

        // Doses that are due now: their reminders and their missed moment.
        foreach (var m in list)
        {
            foreach (var at in Occurrences(m, now - MissedAfter - MissedAfterLater - TimeSpan.FromHours(1), now.AddSeconds(1)))
            {
                var slot = Evaluate(m, at, byId.GetValueOrDefault(DoseEvent.IdFor(m.Id, at)), now);
                if (slot.State != DoseState.Due)
                {
                    continue;
                }

                candidates.AddRange(RemindAgainAfter.Select(after => at + after));
                if (slot.Event is { Status: DoseStatus.Later, LaterUntil: { } until })
                {
                    candidates.Add(until);
                }

                candidates.Add(slot.MissedAt);
            }

            // The next dose time.
            var next = Occurrences(m, now.AddSeconds(1), now.AddDays(2)).FirstOrDefault();
            if (next != default)
            {
                candidates.Add(next);
            }
        }

        var future = candidates.Where(c => c > now).ToList();
        return future.Count > 0 ? future.Min() : null;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}

/// <summary>Stock: how long it lasts and when to buy more.</summary>
public static class DoseStock
{
    /// <summary>Whole days the stock lasts at the scheduled rate; null when unknown (no count, or only taken when needed).</summary>
    public static int? DaysLeft(Medicine medicine) =>
        medicine is { Stock: { } stock } && medicine.Schedule.DosesPerDay is > 0 and var perDay && medicine.DoseAmount > 0
            ? (int)Math.Floor(stock / (perDay * medicine.DoseAmount))
            : null;

    public static bool NeedsRefill(Medicine medicine) =>
        DaysLeft(medicine) is { } days ? days <= medicine.RefillWarnDays : medicine.Stock is { } s && s <= 0;

    /// <summary>One dose taken: the stock goes down (never below zero).</summary>
    public static Medicine AfterTaken(Medicine medicine) =>
        medicine.Stock is { } stock ? medicine with { Stock = Math.Max(0, stock - medicine.DoseAmount) } : medicine;
}
