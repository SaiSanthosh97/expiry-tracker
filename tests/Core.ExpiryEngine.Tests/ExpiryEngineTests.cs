using HomeTracker.Core.ShelfLife;

namespace HomeTracker.Core.ExpiryEngine.Tests;

public class ExpiryCalculatorTests
{
    [Fact]
    public void No_dates_means_unknown()
    {
        Assert.Equal(ExpiryResult.Unknown, ExpiryCalculator.Calculate(new ExpiryInputs()));
    }

    [Fact]
    public void Printed_date_is_used()
    {
        var result = ExpiryCalculator.Calculate(new ExpiryInputs { PrintedExpiry = new DateOnly(2027, 5, 31) });

        Assert.Equal(new DateOnly(2027, 5, 31), result.LastUsableDay);
        Assert.Equal(ExpirySource.Printed, result.Source);
    }

    [Fact]
    public void Loose_sweets_use_shelf_life_from_added_date()
    {
        var result = ExpiryCalculator.Calculate(new ExpiryInputs
        {
            AddedDate = new DateOnly(2026, 11, 9),
            ShelfLifeFromAdded = ShelfLifeDuration.Days(3),
        });

        Assert.Equal(new DateOnly(2026, 11, 11), result.LastUsableDay);
        Assert.Equal(ExpirySource.AddedPlusShelfLife, result.Source);
    }

    [Fact]
    public void Opening_brings_expiry_forward()
    {
        var result = ExpiryCalculator.Calculate(new ExpiryInputs
        {
            PrintedExpiry = new DateOnly(2027, 3, 31),
            OpenedDate = new DateOnly(2026, 11, 11),
            AfterOpening = ShelfLifeDuration.Days(28),
        });

        Assert.Equal(new DateOnly(2026, 12, 8), result.LastUsableDay);
        Assert.Equal(ExpirySource.AfterOpening, result.Source);
    }

    [Fact]
    public void Opening_never_extends_printed_expiry()
    {
        var result = ExpiryCalculator.Calculate(new ExpiryInputs
        {
            PrintedExpiry = new DateOnly(2026, 11, 20),
            OpenedDate = new DateOnly(2026, 11, 11),
            AfterOpening = ShelfLifeDuration.Days(30),
        });

        Assert.Equal(new DateOnly(2026, 11, 20), result.LastUsableDay);
        Assert.Equal(ExpirySource.Printed, result.Source);
    }

    [Fact]
    public void Manufacture_plus_best_before_is_used()
    {
        var result = ExpiryCalculator.Calculate(new ExpiryInputs
        {
            ManufactureDate = new DateOnly(2026, 1, 1),
            BestBeforeFromManufacture = ShelfLifeDuration.Months(12),
        });

        Assert.Equal(new DateOnly(2026, 12, 31), result.LastUsableDay);
    }

    [Fact]
    public void Incomplete_rules_are_ignored()
    {
        var result = ExpiryCalculator.Calculate(new ExpiryInputs
        {
            OpenedDate = new DateOnly(2026, 11, 11),
            ManufactureDate = new DateOnly(2026, 1, 1),
        });

        Assert.Equal(ExpiryResult.Unknown, result);
    }
}

public class ExpiryStatusEvaluatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    [Theory]
    [InlineData(2026, 11, 20, ExpiryState.Fresh, 9)]
    [InlineData(2026, 11, 13, ExpiryState.UseSoon, 2)]
    [InlineData(2026, 11, 11, ExpiryState.UseSoon, 0)]
    [InlineData(2026, 11, 10, ExpiryState.Expired, -1)]
    public void Status_depends_on_days_left(int year, int month, int day, ExpiryState expected, int daysLeft)
    {
        var status = ExpiryStatusEvaluator.Evaluate(new DateOnly(year, month, day), Today, reminderLeadDays: 2);

        Assert.Equal(expected, status.State);
        Assert.Equal(daysLeft, status.DaysLeft);
    }

    [Fact]
    public void Closed_items_are_closed_regardless_of_date()
    {
        Assert.Equal(ExpiryState.Closed, ExpiryStatusEvaluator.Evaluate(new DateOnly(2026, 1, 1), Today, 2, isClosed: true).State);
    }

    [Fact]
    public void Missing_date_is_unknown()
    {
        Assert.Equal(ExpiryState.Unknown, ExpiryStatusEvaluator.Evaluate(null, Today, 2).State);
    }

    [Fact]
    public void Reminder_fires_lead_days_before_last_day()
    {
        Assert.Equal(new DateOnly(2026, 11, 18), ExpiryStatusEvaluator.ReminderDay(new DateOnly(2026, 11, 20), Today, 2));
    }

    [Fact]
    public void Reminder_already_due_fires_today()
    {
        Assert.Equal(Today, ExpiryStatusEvaluator.ReminderDay(new DateOnly(2026, 11, 12), Today, 2));
    }

    [Fact]
    public void No_reminder_for_expired_or_closed_items()
    {
        Assert.Null(ExpiryStatusEvaluator.ReminderDay(new DateOnly(2026, 11, 10), Today, 2));
        Assert.Null(ExpiryStatusEvaluator.ReminderDay(new DateOnly(2026, 11, 20), Today, 2, isClosed: true));
    }
}
