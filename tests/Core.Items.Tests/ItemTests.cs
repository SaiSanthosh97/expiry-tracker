using HomeTracker.Core.ExpiryEngine;
using HomeTracker.Core.ShelfLife;

namespace HomeTracker.Core.Items.Tests;

public class ItemTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly ItemEvaluator Evaluator = new(new ShelfLifeCatalog());

    private static TrackedItem Item(string name, string category = "packaged-food") => new()
    {
        Id = TrackedItem.NewId(),
        Name = name,
        CategoryId = category,
        AddedDate = Today,
        UpdatedAt = Now,
    };

    [Fact]
    public void Sweets_use_shelf_life_and_short_lead_time()
    {
        var sweets = Item("Kaju katli", "sweets") with { ShelfLifeFromAdded = ShelfLifeDuration.Days(3) };

        var evaluation = Evaluator.Evaluate(sweets, Today);

        Assert.Equal(new DateOnly(2026, 11, 13), evaluation.Expiry.LastUsableDay);
        Assert.Equal(1, evaluation.ReminderLeadDays);
        Assert.Equal(new DateOnly(2026, 11, 12), evaluation.ReminderDay);
    }

    [Fact]
    public void Opening_eye_drops_applies_category_rule()
    {
        var drops = Item("Eye drops", "eye-ear-drops") with { PrintedExpiry = new DateOnly(2028, 1, 31) };

        var opened = ItemActions.MarkOpened(drops, Today, afterOpening: null, Now);
        var evaluation = Evaluator.Evaluate(opened, Today);

        Assert.Equal(new DateOnly(2026, 12, 8), evaluation.Expiry.LastUsableDay);
        Assert.Equal(ExpirySource.AfterOpening, evaluation.Expiry.Source);
    }

    [Fact]
    public void Opening_with_user_choice_overrides_category()
    {
        var ketchup = Item("Ketchup", "sauces") with { PrintedExpiry = new DateOnly(2027, 6, 30) };

        var opened = ItemActions.MarkOpened(ketchup, Today, ShelfLifeDuration.Days(14), Now);

        Assert.Equal(new DateOnly(2026, 11, 24), Evaluator.Evaluate(opened, Today).Expiry.LastUsableDay);
    }

    [Fact]
    public void Item_lead_time_override_wins()
    {
        var tablets = Item("Paracetamol", "tablets") with { PrintedExpiry = new DateOnly(2026, 12, 31), ReminderLeadDays = 30 };

        Assert.Equal(ExpiryState.UseSoon, Evaluator.Evaluate(tablets, new DateOnly(2026, 12, 5)).Status.State);
    }

    [Fact]
    public void Close_and_reopen()
    {
        var item = Item("Milk") with { PrintedExpiry = Today };

        var closed = ItemActions.Close(item, ClosedReason.ThrownAway, Today, Now);
        Assert.True(closed.IsClosed);
        Assert.Equal(ExpiryState.Closed, Evaluator.Evaluate(closed, Today).Status.State);

        var reopened = ItemActions.Reopen(closed, Now);
        Assert.False(reopened.IsClosed);
        Assert.Null(reopened.ClosedReason);
    }

    [Fact]
    public void Home_sections_are_ordered_and_exclude_closed_items()
    {
        var items = new[]
        {
            Item("Fresh later") with { PrintedExpiry = new DateOnly(2027, 1, 1) },
            Item("Fresh sooner") with { PrintedExpiry = new DateOnly(2026, 12, 1) },
            Item("Expired") with { PrintedExpiry = new DateOnly(2026, 11, 1) },
            Item("Last day") with { PrintedExpiry = Today },
            Item("No date"),
            ItemActions.Close(Item("Closed") with { PrintedExpiry = new DateOnly(2026, 11, 1) }, ClosedReason.UsedUp, Today, Now),
        };

        var sections = ItemLists.ForHome(Evaluator.EvaluateAll(items, Today));

        Assert.Equal(["Expired"], sections.Expired.Select(e => e.Item.Name));
        Assert.Equal(["Last day"], sections.UseSoon.Select(e => e.Item.Name));
        Assert.Equal(["Fresh sooner", "Fresh later"], sections.Fresh.Select(e => e.Item.Name));
        Assert.Equal(["No date"], sections.NoDate.Select(e => e.Item.Name));
    }

    [Fact]
    public void Home_sections_filter_by_group()
    {
        var items = new[]
        {
            Item("Tablets", "tablets") with { PrintedExpiry = new DateOnly(2027, 1, 1) },
            Item("Biscuits") with { PrintedExpiry = new DateOnly(2027, 1, 1) },
        };

        var sections = ItemLists.ForHome(Evaluator.EvaluateAll(items, Today), CategoryGroup.Medicine);

        Assert.Equal(["Tablets"], sections.Fresh.Select(e => e.Item.Name));
    }

    [Fact]
    public void Reminders_cover_window_and_day_after_expiry_only()
    {
        var items = new[]
        {
            Item("Tomorrow") with { PrintedExpiry = Today.AddDays(1) },
            Item("Expired yesterday") with { PrintedExpiry = Today.AddDays(-1) },
            Item("Expired last week") with { PrintedExpiry = Today.AddDays(-7) },
            Item("Next month") with { PrintedExpiry = Today.AddDays(30) },
        };

        var due = ItemLists.DueForReminder(Evaluator.EvaluateAll(items, Today), Today);

        Assert.Equal(["Expired yesterday", "Tomorrow"], due.Select(e => e.Item.Name));
    }

    [Fact]
    public void Snoozed_item_waits_until_tomorrow()
    {
        var item = ItemActions.SnoozeUntilTomorrow(Item("Bread") with { PrintedExpiry = Today.AddDays(1) }, Today, Now);

        Assert.Empty(ItemLists.DueForReminder([Evaluator.Evaluate(item, Today)], Today));
        var tomorrow = Today.AddDays(1);
        Assert.Single(ItemLists.DueForReminder([Evaluator.Evaluate(item, tomorrow)], tomorrow));
    }

    [Fact]
    public void Deleted_items_are_not_evaluated()
    {
        var deleted = ItemActions.Delete(Item("Gone"), Now);

        Assert.Empty(Evaluator.EvaluateAll([deleted], Today));
    }
}

public class ItemExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Round_trips_all_fields()
    {
        var item = new TrackedItem
        {
            Id = "abc",
            Name = "Eye drops",
            CategoryId = "eye-ear-drops",
            Quantity = 2,
            Barcode = "8901719110016",
            AddedDate = new DateOnly(2026, 11, 1),
            PrintedExpiry = new DateOnly(2028, 1, 31),
            ManufactureDate = new DateOnly(2026, 1, 1),
            BestBeforeFromManufacture = ShelfLifeDuration.Months(24),
            OpenedDate = new DateOnly(2026, 11, 10),
            AfterOpening = ShelfLifeDuration.Days(28),
            ReminderLeadDays = 3,
            ClosedDate = new DateOnly(2026, 11, 11),
            ClosedReason = ClosedReason.ThrownAway,
            UpdatedAt = Now,
        };

        var json = ItemExport.Serialize([item], Now);

        Assert.Equal(item, Assert.Single(ItemExport.Deserialize(json)));
        Assert.Contains("\"ThrownAway\"", json);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\": 99, \"items\": []}")]
    [InlineData("{\"something\": 1}")]
    public void Rejects_foreign_or_newer_files(string json)
    {
        Assert.Throws<FormatException>(() => ItemExport.Deserialize(json));
    }

    [Fact]
    public void Newer_copy_wins_when_merging()
    {
        TrackedItem Make(string id, string name, int minute) => new()
        {
            Id = id,
            Name = name,
            CategoryId = "other",
            UpdatedAt = Now.AddMinutes(minute),
        };

        var local = new[] { Make("1", "Local newer", 10), Make("2", "Local older", 0) };
        var incoming = new[] { Make("1", "Incoming older", 5), Make("2", "Incoming newer", 5), Make("3", "New", 0) };

        var changes = ItemExport.ChangesToApply(local, incoming);

        Assert.Equal(["Incoming newer", "New"], changes.Select(c => c.Name));
    }
}
