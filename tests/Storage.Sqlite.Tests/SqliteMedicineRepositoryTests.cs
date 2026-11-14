using HomeTracker.Core.Medicines;
using HomeTracker.Storage.Sqlite;

namespace HomeTracker.Storage.Sqlite.Tests;

public class SqliteMedicineRepositoryTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ht-meds-{Guid.NewGuid():N}.db3");
    private SqliteMedicineRepository _repository = null!;

    public Task InitializeAsync()
    {
        _repository = new SqliteMedicineRepository(_path);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _repository.DisposeAsync();
        SQLite.SQLiteAsyncConnection.ResetPool();
        File.Delete(_path);
    }

    private static Medicine Med(string homeId = "h1") => new()
    {
        Id = Medicine.NewId(),
        HomeId = homeId,
        Person = "Appa",
        Name = "Amlodipine 5 mg",
        Schedule = new DoseSchedule(ScheduleKind.Weekdays, [new TimeOnly(9, 0), new TimeOnly(21, 30)], 1, [DayOfWeek.Monday, DayOfWeek.Friday]),
        DoseAmount = 0.5m,
        Unit = "tablet",
        Stock = 12.5m,
        RefillWarnDays = 4,
        ReminderDeviceId = "dev-appa",
        StartDate = new DateOnly(2026, 11, 1),
        EndDate = new DateOnly(2026, 12, 31),
        FirstDoseFrom = new DateTime(2026, 11, 1, 14, 8, 0),
        Notes = "After food",
        AddedBy = "Ravi",
        UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_794_600_000_000),
    };

    [Fact]
    public async Task Saves_and_loads_every_field_of_a_medicine()
    {
        var med = Med();
        await _repository.SaveMedicineAsync(med);

        var loaded = Assert.Single(await _repository.GetMedicinesAsync());

        Assert.Equal(med with { Schedule = loaded.Schedule }, loaded);
        Assert.Equal(med.Schedule.Times, loaded.Schedule.Times);
        Assert.Equal(med.Schedule.Days, loaded.Schedule.Days);
        Assert.Equal(med.Schedule.Kind, loaded.Schedule.Kind);
    }

    [Fact]
    public async Task Deleted_medicines_are_only_listed_on_request()
    {
        await _repository.SaveMedicineAsync(Med() with { IsDeleted = true });

        Assert.Empty(await _repository.GetMedicinesAsync());
        Assert.Single(await _repository.GetMedicinesAsync(includeDeleted: true));
    }

    [Fact]
    public async Task Dose_records_replace_by_dose_and_filter_by_date()
    {
        var at = new DateTime(2026, 11, 14, 9, 0, 0);
        var id = DoseEvent.IdFor("m1", at);
        await _repository.SaveEventAsync(new DoseEvent(id, "m1", "h1", at, DoseStatus.Later, DateTimeOffset.Now, "Appa", at.AddMinutes(15)));
        await _repository.SaveEventAsync(new DoseEvent(id, "m1", "h1", at, DoseStatus.Taken, DateTimeOffset.Now, "Appa"));
        await _repository.SaveEventAsync(new DoseEvent(DoseEvent.IdFor("m1", at.AddDays(-3)), "m1", "h1", at.AddDays(-3), DoseStatus.Missed, DateTimeOffset.Now));

        var recent = await _repository.GetEventsAsync(at.Date);

        var only = Assert.Single(recent);
        Assert.Equal(DoseStatus.Taken, only.Status);
        Assert.Equal(at, only.ScheduledAt);
        Assert.Null(only.LaterUntil);
    }

    [Fact]
    public async Task Purge_removes_one_homes_medicines_and_doses()
    {
        await _repository.SaveMedicineAsync(Med("amma"));
        await _repository.SaveMedicineAsync(Med("mine"));
        await _repository.SaveEventAsync(new DoseEvent("x", "m", "amma", DateTime.Today, DoseStatus.Taken, DateTimeOffset.Now));

        await _repository.PurgeHomeAsync("amma");

        Assert.Equal(["mine"], (await _repository.GetMedicinesAsync()).Select(m => m.HomeId));
        Assert.Empty(await _repository.GetEventsAsync(DateTime.MinValue));
    }
}
