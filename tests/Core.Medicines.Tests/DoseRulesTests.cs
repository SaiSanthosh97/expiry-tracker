using HomeTracker.Core.Medicines;

namespace HomeTracker.Core.Medicines.Tests;

public class DoseRulesTests
{
    private static readonly DateOnly Start = new(2026, 11, 1);

    private static Medicine Med(DoseSchedule schedule, decimal? stock = null, decimal amount = 1) => new()
    {
        Id = "m1",
        HomeId = "h",
        Person = "Appa",
        Name = "Amlodipine 5 mg",
        Schedule = schedule,
        StartDate = Start,
        Stock = stock,
        DoseAmount = amount,
    };

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0);

    private static DoseEvent Event(DateTime scheduled, DoseStatus status, DateTime? laterUntil = null) =>
        new(DoseEvent.IdFor("m1", scheduled), "m1", "h", scheduled, status, DateTimeOffset.Now, "Appa", laterUntil);

    [Fact]
    public void Daily_doses_on_every_day_from_the_start()
    {
        var med = Med(DoseSchedule.DailyAt(new TimeOnly(21, 0), new TimeOnly(9, 0)));

        var times = DoseRules.Occurrences(med, At(14, 0), At(16, 0)).ToList();

        Assert.Equal([At(14, 9), At(14, 21), At(15, 9), At(15, 21)], times);
        Assert.Empty(DoseRules.Occurrences(med, new DateTime(2026, 10, 20), new DateTime(2026, 10, 31)));
    }

    [Fact]
    public void Alternate_days_count_from_the_start_date()
    {
        var med = Med(new DoseSchedule(ScheduleKind.EveryNDays, [new TimeOnly(8, 0)], IntervalDays: 2));

        var days = DoseRules.Occurrences(med, At(1, 0), At(7, 0)).Select(d => d.Day).ToList();

        Assert.Equal([1, 3, 5], days);
    }

    [Fact]
    public void Weekday_schedule_only_on_chosen_days()
    {
        // 14 Nov 2026 is a Saturday.
        var med = Med(new DoseSchedule(ScheduleKind.Weekdays, [new TimeOnly(8, 0)], Days: [DayOfWeek.Monday, DayOfWeek.Thursday]));

        var days = DoseRules.Occurrences(med, At(14, 0), At(21, 0)).Select(d => d.DayOfWeek).ToList();

        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Thursday], days);
    }

    [Fact]
    public void As_needed_has_no_scheduled_doses_and_no_stock_forecast()
    {
        var med = Med(new DoseSchedule(ScheduleKind.AsNeeded, []), stock: 10);

        Assert.Empty(DoseRules.Occurrences(med, At(1, 0), At(30, 0)));
        Assert.Null(DoseStock.DaysLeft(med));
    }

    [Fact]
    public void Doses_before_the_medicine_was_added_do_not_count()
    {
        var med = Med(DoseSchedule.DailyAt(new TimeOnly(9, 0), new TimeOnly(21, 0))) with { FirstDoseFrom = At(14, 14, 8) };

        Assert.Equal([At(14, 21), At(15, 9)], DoseRules.Occurrences(med, At(14, 0), At(15, 12)));
        Assert.DoesNotContain(DoseRules.Today([med], [], At(14, 14, 10)), s => s.State == DoseState.Missed);
    }

    [Fact]
    public void A_course_ends_on_its_end_date()
    {
        var med = Med(DoseSchedule.DailyAt(new TimeOnly(9, 0))) with { EndDate = new DateOnly(2026, 11, 3) };

        Assert.Equal(3, DoseRules.Occurrences(med, At(1, 0), At(10, 0)).Count());
    }

    [Theory]
    [InlineData(8, 59, DoseState.Upcoming)]
    [InlineData(9, 0, DoseState.Due)]
    [InlineData(9, 59, DoseState.Due)]
    [InlineData(10, 0, DoseState.Missed)]
    public void Dose_is_due_for_an_hour_then_missed(int hour, int minute, DoseState expected)
    {
        var med = Med(DoseSchedule.DailyAt(new TimeOnly(9, 0)));

        Assert.Equal(expected, DoseRules.Evaluate(med, At(14, 9), null, At(14, hour, minute)).State);
    }

    [Fact]
    public void Marked_doses_keep_their_state_whatever_the_time()
    {
        var med = Med(DoseSchedule.DailyAt(new TimeOnly(9, 0)));

        Assert.Equal(DoseState.Taken, DoseRules.Evaluate(med, At(14, 9), Event(At(14, 9), DoseStatus.Taken), At(14, 23)).State);
        Assert.Equal(DoseState.Skipped, DoseRules.Evaluate(med, At(14, 9), Event(At(14, 9), DoseStatus.Skipped), At(14, 23)).State);
    }

    [Fact]
    public void Later_near_the_end_of_the_hour_moves_the_missed_moment()
    {
        var med = Med(DoseSchedule.DailyAt(new TimeOnly(9, 0)));
        var later = Event(At(14, 9), DoseStatus.Later, laterUntil: At(14, 10, 0));

        var slot = DoseRules.Evaluate(med, At(14, 9), later, At(14, 10, 10));

        Assert.Equal(DoseState.Due, slot.State);
        Assert.Equal(At(14, 10, 30), slot.MissedAt);
        Assert.Equal(DoseState.Missed, DoseRules.Evaluate(med, At(14, 9), later, At(14, 10, 30)).State);
    }

    [Fact]
    public void Next_wake_is_the_next_dose_when_nothing_is_due()
    {
        var med = Med(DoseSchedule.DailyAt(new TimeOnly(9, 0), new TimeOnly(21, 0)));

        Assert.Equal(At(14, 21), DoseRules.NextWake([med], [], At(14, 12)));
    }

    [Fact]
    public void Next_wake_follows_the_remind_again_and_missed_moments_of_a_due_dose()
    {
        var med = Med(DoseSchedule.DailyAt(new TimeOnly(9, 0)));

        Assert.Equal(At(14, 9, 10), DoseRules.NextWake([med], [], At(14, 9, 0)));
        Assert.Equal(At(14, 9, 30), DoseRules.NextWake([med], [], At(14, 9, 10)));
        Assert.Equal(At(14, 10, 0), DoseRules.NextWake([med], [], At(14, 9, 30)));
        Assert.Equal(At(15, 9, 0), DoseRules.NextWake([med], [], At(14, 10, 0)));
    }

    [Fact]
    public void Next_wake_after_later_is_the_end_of_later()
    {
        var med = Med(DoseSchedule.DailyAt(new TimeOnly(9, 0)));
        var later = Event(At(14, 9), DoseStatus.Later, laterUntil: At(14, 9, 47));

        Assert.Equal(At(14, 9, 47), DoseRules.NextWake([med], [later], At(14, 9, 32)));
    }

    [Fact]
    public void Taken_dose_needs_no_more_wakes_today()
    {
        var med = Med(DoseSchedule.DailyAt(new TimeOnly(9, 0)));

        Assert.Equal(At(15, 9), DoseRules.NextWake([med], [Event(At(14, 9), DoseStatus.Taken)], At(14, 9, 5)));
    }

    [Fact]
    public void Today_includes_a_late_night_dose_still_within_its_hour()
    {
        var med = Med(DoseSchedule.DailyAt(new TimeOnly(23, 30)));

        var slots = DoseRules.Today([med], [], At(15, 0, 10));

        Assert.Equal([At(14, 23, 30), At(15, 23, 30)], slots.Select(s => s.ScheduledAt));
        Assert.Equal(DoseState.Due, slots[0].State);
    }

    [Fact]
    public void Stock_lasts_and_warns_before_running_out()
    {
        var twiceDaily = Med(DoseSchedule.DailyAt(new TimeOnly(9, 0), new TimeOnly(21, 0)), stock: 15);

        Assert.Equal(7, DoseStock.DaysLeft(twiceDaily));
        Assert.False(DoseStock.NeedsRefill(twiceDaily));
        Assert.True(DoseStock.NeedsRefill(twiceDaily with { Stock = 10 }));
        Assert.Equal(14, DoseStock.AfterTaken(twiceDaily).Stock);
        Assert.Equal(0, DoseStock.AfterTaken(twiceDaily with { Stock = 0.5m }).Stock);
    }

    [Fact]
    public void Alternate_day_stock_lasts_twice_as_long()
    {
        var med = Med(new DoseSchedule(ScheduleKind.EveryNDays, [new TimeOnly(8, 0)], IntervalDays: 2), stock: 10);

        Assert.Equal(20, DoseStock.DaysLeft(med));
    }
}
