namespace HomeTracker.Core.ShelfLife.Tests;

public class ShelfLifeDurationTests
{
    [Fact]
    public void Day_count_includes_the_start_day()
    {
        // Sweets bought Monday, good for 3 days: Monday, Tuesday, Wednesday.
        Assert.Equal(new DateOnly(2026, 11, 11), ShelfLifeDuration.Days(3).LastUsableDay(new DateOnly(2026, 11, 9)));
    }

    [Fact]
    public void Eye_drops_28_days_after_opening()
    {
        Assert.Equal(new DateOnly(2026, 12, 8), ShelfLifeDuration.Days(28).LastUsableDay(new DateOnly(2026, 11, 11)));
    }

    [Fact]
    public void Months_end_the_day_before_the_same_date()
    {
        Assert.Equal(new DateOnly(2027, 1, 14), ShelfLifeDuration.Months(12).LastUsableDay(new DateOnly(2026, 1, 15)));
    }

    [Fact]
    public void Months_from_end_of_month_clamp_to_shorter_month()
    {
        Assert.Equal(new DateOnly(2027, 2, 27), ShelfLifeDuration.Months(1).LastUsableDay(new DateOnly(2027, 1, 31)));
    }

    [Fact]
    public void Weeks_are_seven_days()
    {
        Assert.Equal(new DateOnly(2026, 11, 24), ShelfLifeDuration.Weeks(2).LastUsableDay(new DateOnly(2026, 11, 11)));
    }

    [Fact]
    public void Rejects_zero_or_negative_amounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShelfLifeDuration.Days(0));
    }
}

public class ReminderLeadTimeTests
{
    [Fact]
    public void Short_life_items_remind_one_day_before()
    {
        Assert.Equal(1, ReminderLeadTime.For(DefaultPresets.Sweets, null));
    }

    [Fact]
    public void Other_items_remind_two_days_before()
    {
        Assert.Equal(2, ReminderLeadTime.For(DefaultPresets.PackagedFood, ShelfLifeDuration.Months(12)));
        Assert.Equal(2, ReminderLeadTime.For(null, null));
    }

    [Fact]
    public void Explicit_category_lead_time_wins()
    {
        var custom = DefaultPresets.Tablets with { ReminderLeadDays = 30 };

        Assert.Equal(30, ReminderLeadTime.For(custom, ShelfLifeDuration.Days(3)));
    }
}

public class ShelfLifeCatalogTests
{
    [Fact]
    public void Contains_all_presets_with_unique_ids()
    {
        var catalog = new ShelfLifeCatalog();

        Assert.Equal(DefaultPresets.All.Count, catalog.All.Count);
    }

    [Fact]
    public void Household_override_replaces_preset()
    {
        var catalog = new ShelfLifeCatalog([DefaultPresets.Sweets with { ShelfLife = ShelfLifeDuration.Days(4) }]);

        Assert.Equal(ShelfLifeDuration.Days(4), catalog.Find("SWEETS")?.ShelfLife);
    }

    [Fact]
    public void Custom_categories_are_added()
    {
        var catalog = new ShelfLifeCatalog([new ShelfLifeCategory("pickle", CategoryGroup.Food, AfterOpening: ShelfLifeDuration.Months(3))]);

        Assert.NotNull(catalog.Find("pickle"));
        Assert.Contains(catalog.InGroup(CategoryGroup.Food), c => c.Id == "pickle");
    }
}
