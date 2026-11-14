using System.Globalization;

namespace HomeTracker.Core.Medicines;

public enum ScheduleKind
{
    /// <summary>At the same times every day.</summary>
    Daily,

    /// <summary>At the same times every N days, counted from the start date (N = 2: alternate days).</summary>
    EveryNDays,

    /// <summary>At the same times on chosen days of the week.</summary>
    Weekdays,

    /// <summary>Only when needed ("SOS"): no reminders, taken doses are still recorded.</summary>
    AsNeeded,
}

/// <summary>When a medicine is taken.</summary>
/// <param name="Times">Clock times on a dose day, e.g. 09:00 and 21:00. Empty for <see cref="ScheduleKind.AsNeeded"/>.</param>
/// <param name="IntervalDays">For <see cref="ScheduleKind.EveryNDays"/>.</param>
/// <param name="Days">For <see cref="ScheduleKind.Weekdays"/>.</param>
public sealed record DoseSchedule(ScheduleKind Kind, IReadOnlyList<TimeOnly> Times, int IntervalDays = 1, IReadOnlyList<DayOfWeek>? Days = null)
{
    public static DoseSchedule DailyAt(params TimeOnly[] times) => new(ScheduleKind.Daily, times);

    public bool IsDoseDay(DateOnly day, DateOnly start) => Kind switch
    {
        _ when day < start => false,
        ScheduleKind.Daily => true,
        ScheduleKind.EveryNDays => (day.DayNumber - start.DayNumber) % Math.Max(1, IntervalDays) == 0,
        ScheduleKind.Weekdays => Days?.Contains(day.DayOfWeek) == true,
        _ => false,
    };

    /// <summary>Doses taken on average per day, for working out how long the stock lasts. Null when only taken when needed.</summary>
    public decimal? DosesPerDay => Kind switch
    {
        ScheduleKind.Daily => Times.Count,
        ScheduleKind.EveryNDays => (decimal)Times.Count / Math.Max(1, IntervalDays),
        ScheduleKind.Weekdays => (decimal)Times.Count * (Days?.Distinct().Count() ?? 0) / 7,
        _ => null,
    };
}

/// <summary>A medicine someone in a home takes regularly (or when needed).</summary>
public sealed record Medicine
{
    public required string Id { get; init; }

    public required string HomeId { get; init; }

    /// <summary>Who takes it, e.g. "Appa".</summary>
    public required string Person { get; init; }

    /// <summary>E.g. "Amlodipine 5 mg".</summary>
    public required string Name { get; init; }

    public required DoseSchedule Schedule { get; init; }

    /// <summary>How much per dose, in <see cref="Unit"/>s.</summary>
    public decimal DoseAmount { get; init; } = 1;

    /// <summary>E.g. "tablet", "capsule", "ml", "drop", "puff".</summary>
    public string Unit { get; init; } = "tablet";

    /// <summary>How many units are left at home, when counted.</summary>
    public decimal? Stock { get; init; }

    /// <summary>Warn this many days before the stock runs out.</summary>
    public int RefillWarnDays { get; init; } = 5;

    /// <summary>The phone that rings for this medicine (the person's own phone). Other phones only see the status.</summary>
    public string? ReminderDeviceId { get; init; }

    public DateOnly StartDate { get; init; }

    /// <summary>
    /// Doses scheduled before this moment don't count: set when the medicine is added or its times change, so a
    /// 9 AM dose isn't "missed" when the medicine is added at 2 PM.
    /// </summary>
    public DateTime? FirstDoseFrom { get; init; }

    /// <summary>Last day of a course (e.g. antibiotics), or null to continue.</summary>
    public DateOnly? EndDate { get; init; }

    public string? Notes { get; init; }

    public string? AddedBy { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public bool IsDeleted { get; init; }

    public bool IsActiveOn(DateOnly day) => !IsDeleted && day >= StartDate && (EndDate is null || day <= EndDate);

    public static string NewId() => Guid.NewGuid().ToString("N");
}

public enum DoseStatus
{
    Taken,
    Skipped,
    Missed,

    /// <summary>"Later": remind again at <see cref="DoseEvent.LaterUntil"/>.</summary>
    Later,
}

/// <summary>What happened to one dose. Shared with the family, so everyone sees today's doses.</summary>
/// <param name="ScheduledAt">Local date and time the dose was due (for a when-needed dose: when it was taken).</param>
public sealed record DoseEvent(
    string Id,
    string MedicineId,
    string HomeId,
    DateTime ScheduledAt,
    DoseStatus Status,
    DateTimeOffset RecordedAt,
    string? By = null,
    DateTime? LaterUntil = null)
{
    /// <summary>One event per scheduled dose: a later change (Later → Taken) replaces the earlier one.</summary>
    public static string IdFor(string medicineId, DateTime scheduledAt) =>
        medicineId + "@" + scheduledAt.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
}
