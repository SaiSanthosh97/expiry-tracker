using HomeTracker.Core.Items;
using HomeTracker.Core.ShelfLife;

namespace HomeTracker.Storage.Sqlite.Tests;

public sealed class SqliteItemRepositoryTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hometracker-test-{Guid.NewGuid():N}.db3");
    private SqliteItemRepository _repository = null!;

    public Task InitializeAsync()
    {
        _repository = new SqliteItemRepository(_path);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _repository.DisposeAsync();
        File.Delete(_path);
    }

    private static TrackedItem Item(string name) => new()
    {
        Id = TrackedItem.NewId(),
        Name = name,
        CategoryId = "other",
        AddedDate = new DateOnly(2026, 11, 11),
        UpdatedAt = new DateTimeOffset(2026, 11, 11, 9, 30, 15, TimeSpan.Zero),
    };

    [Fact]
    public async Task Saves_and_loads_every_field()
    {
        var item = Item("Eye drops") with
        {
            CategoryId = "eye-ear-drops",
            Quantity = 3,
            PrintedExpiry = new DateOnly(2028, 1, 31),
            ManufactureDate = new DateOnly(2026, 1, 1),
            BestBeforeFromManufacture = ShelfLifeDuration.Months(24),
            ShelfLifeFromAdded = ShelfLifeDuration.Weeks(2),
            OpenedDate = new DateOnly(2026, 11, 10),
            AfterOpening = ShelfLifeDuration.Days(28),
            ReminderLeadDays = 5,
            SnoozedUntil = new DateOnly(2026, 11, 12),
            PhotoPath = "photo.jpg",
        };

        await _repository.SaveAsync(item);

        Assert.Equal(item, await _repository.GetAsync(item.Id));
    }

    [Fact]
    public async Task Closed_items_are_only_listed_on_request()
    {
        var open = Item("Open");
        var closed = Item("Closed") with { ClosedDate = new DateOnly(2026, 11, 11), ClosedReason = ClosedReason.UsedUp };
        await _repository.SaveAsync(open);
        await _repository.SaveAsync(closed);

        Assert.Equal(["Open"], (await _repository.GetAllAsync()).Select(i => i.Name));
        Assert.Equal(2, (await _repository.GetAllAsync(includeClosed: true)).Count);
    }

    [Fact]
    public async Task Deleted_items_are_hidden_but_kept_for_export()
    {
        var item = Item("Gone") with { IsDeleted = true };
        await _repository.SaveAsync(item);

        Assert.Null(await _repository.GetAsync(item.Id));
        Assert.Empty(await _repository.GetAllAsync(includeClosed: true));
        Assert.Single(await _repository.GetEverythingAsync());
    }

    [Fact]
    public async Task Save_replaces_existing_item()
    {
        var item = Item("Milk");
        await _repository.SaveAsync(item);
        await _repository.SaveAsync(item with { Name = "Milk (opened)" });

        Assert.Equal("Milk (opened)", (await _repository.GetAsync(item.Id))?.Name);
        Assert.Single(await _repository.GetEverythingAsync());
    }
}
