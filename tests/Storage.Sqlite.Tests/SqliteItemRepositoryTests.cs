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
            Barcode = "8901719110016",
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
    public async Task Database_from_version_0_1_without_barcode_column_still_opens()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hometracker-old-{Guid.NewGuid():N}.db3");
        try
        {
            using (var old = new SQLite.SQLiteConnection(path))
            {
                // Schema as created by 0.1.0 (no Barcode column).
                old.Execute("CREATE TABLE Items (Id varchar PRIMARY KEY NOT NULL, Name varchar, CategoryId varchar, Quantity integer, AddedDate integer, " +
                    "PrintedExpiry integer, ManufactureDate integer, BestBeforeAmount integer, BestBeforeUnit integer, ShelfLifeAmount integer, ShelfLifeUnit integer, " +
                    "OpenedDate integer, AfterOpeningAmount integer, AfterOpeningUnit integer, ReminderLeadDays integer, SnoozedUntil integer, ClosedDate integer, " +
                    "ClosedReason integer, PhotoPath varchar, UpdatedAtMs integer, IsDeleted integer)");
                old.Execute("INSERT INTO Items (Id, Name, CategoryId, Quantity, AddedDate, UpdatedAtMs, IsDeleted) VALUES ('old', 'Old milk', 'milk', 1, 739000, 0, 0)");
            }

            await using var repository = new SqliteItemRepository(path);
            var item = await repository.GetAsync("old");

            Assert.Equal("Old milk", item?.Name);
            Assert.Null(item?.Barcode);
            await repository.SaveAsync(item! with { Barcode = "8901719110016" });
            Assert.Equal("8901719110016", (await repository.GetAsync("old"))?.Barcode);
        }
        finally
        {
            SQLite.SQLiteAsyncConnection.ResetPool();
            File.Delete(path);
        }
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

    [Fact]
    public async Task Purge_removes_only_that_homes_items()
    {
        await _repository.SaveAsync(Item("Milk") with { HomeId = "amma" });
        await _repository.SaveAsync(Item("Bread") with { HomeId = "mine" });

        await _repository.PurgeHomeAsync("amma");

        Assert.Equal(["Bread"], (await _repository.GetEverythingAsync()).Select(i => i.Name));
        Assert.Equal("mine", (await _repository.GetEverythingAsync())[0].HomeId);
    }

    [Fact]
    public async Task Homes_and_family_phones_are_stored_with_their_keys()
    {
        await using var homes = new HomeTracker.Storage.Sqlite.SqliteHomeRepository(_path);
        var now = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
        var home = HomeTracker.Core.Households.Home.CreateLocal("Amma's house", now).StartSharing();
        await homes.SaveHomeAsync(home);
        await homes.SaveDeviceAsync(new HomeTracker.Core.Households.FamilyDevice(home.Id, "d1", "Appa's phone", now));
        await homes.SaveDeviceAsync(new HomeTracker.Core.Households.FamilyDevice(home.Id, "d1", "Appa's phone", now.AddHours(1), now));

        var loaded = Assert.Single(await homes.GetHomesAsync());
        Assert.Equal(home.Key, loaded.Key);
        Assert.Equal(home.Topic, loaded.Topic);
        Assert.Equal(now, loaded.CreatedAt);
        var device = Assert.Single(await homes.GetDevicesAsync(home.Id));
        Assert.Equal(now.AddHours(1), device.LastSeen);
        Assert.Equal(now, device.SilentAlertSent);

        await homes.DeleteHomeAsync(home.Id);
        Assert.Empty(await homes.GetHomesAsync());
        Assert.Empty(await homes.GetDevicesAsync(home.Id));
    }
}
