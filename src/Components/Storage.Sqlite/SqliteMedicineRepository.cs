using System.Text.Json;
using HomeTracker.Core.Medicines;
using SQLite;

namespace HomeTracker.Storage.Sqlite;

/// <summary>Stores medicines and dose records in the app's private SQLite file.</summary>
public sealed class SqliteMedicineRepository : IMedicineRepository, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SQLiteAsyncConnection _connection;
    private readonly Lazy<Task> _initialized;

    public SqliteMedicineRepository(string databasePath)
    {
        _connection = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache | SQLiteOpenFlags.FullMutex);
        _initialized = new Lazy<Task>(async () =>
        {
            await _connection.CreateTableAsync<MedicineRow>();
            await _connection.CreateTableAsync<DoseRow>();
        });
    }

    public async Task<IReadOnlyList<Medicine>> GetMedicinesAsync(bool includeDeleted = false)
    {
        await _initialized.Value;
        var query = _connection.Table<MedicineRow>();
        if (!includeDeleted)
        {
            query = query.Where(r => !r.IsDeleted);
        }

        return (await query.ToListAsync()).Select(r => r.ToMedicine()).ToList();
    }

    public async Task SaveMedicineAsync(Medicine medicine)
    {
        await _initialized.Value;
        await _connection.InsertOrReplaceAsync(MedicineRow.From(medicine));
    }

    public async Task<IReadOnlyList<DoseEvent>> GetEventsAsync(DateTime from)
    {
        await _initialized.Value;
        var ticks = from.Ticks;
        var rows = await _connection.Table<DoseRow>().Where(r => r.ScheduledAtTicks >= ticks).ToListAsync();
        return rows.Select(r => r.ToEvent()).ToList();
    }

    public async Task SaveEventAsync(DoseEvent doseEvent)
    {
        await _initialized.Value;
        await _connection.InsertOrReplaceAsync(DoseRow.From(doseEvent));
    }

    public async Task PurgeHomeAsync(string homeId)
    {
        await _initialized.Value;
        await _connection.ExecuteAsync("DELETE FROM Medicines WHERE HomeId = ?", homeId);
        await _connection.ExecuteAsync("DELETE FROM Doses WHERE HomeId = ?", homeId);
    }

    public ValueTask DisposeAsync() => new(_connection.CloseAsync());

    [Table("Medicines")]
    internal sealed class MedicineRow
    {
        [PrimaryKey]
        public string Id { get; set; } = "";

        [Indexed]
        public string HomeId { get; set; } = "";

        public string Person { get; set; } = "";

        public string Name { get; set; } = "";

        /// <summary>The schedule as JSON: times, kind, interval, days.</summary>
        public string ScheduleJson { get; set; } = "";

        public string DoseAmount { get; set; } = "1";

        public string Unit { get; set; } = "";

        public string? Stock { get; set; }

        public int RefillWarnDays { get; set; }

        public string? ReminderDeviceId { get; set; }

        public int StartDate { get; set; }

        public int? EndDate { get; set; }

        public long? FirstDoseFromTicks { get; set; }

        public string? Notes { get; set; }

        public string? AddedBy { get; set; }

        public long UpdatedAtMs { get; set; }

        public bool IsDeleted { get; set; }

        public static MedicineRow From(Medicine m) => new()
        {
            Id = m.Id,
            HomeId = m.HomeId,
            Person = m.Person,
            Name = m.Name,
            ScheduleJson = JsonSerializer.Serialize(m.Schedule, Json),
            DoseAmount = m.DoseAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Unit = m.Unit,
            Stock = m.Stock?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RefillWarnDays = m.RefillWarnDays,
            ReminderDeviceId = m.ReminderDeviceId,
            StartDate = m.StartDate.DayNumber,
            EndDate = m.EndDate?.DayNumber,
            FirstDoseFromTicks = m.FirstDoseFrom?.Ticks,
            Notes = m.Notes,
            AddedBy = m.AddedBy,
            UpdatedAtMs = m.UpdatedAt.ToUnixTimeMilliseconds(),
            IsDeleted = m.IsDeleted,
        };

        public Medicine ToMedicine() => new()
        {
            Id = Id,
            HomeId = HomeId,
            Person = Person,
            Name = Name,
            Schedule = JsonSerializer.Deserialize<DoseSchedule>(ScheduleJson, Json) ?? new DoseSchedule(ScheduleKind.AsNeeded, []),
            DoseAmount = decimal.Parse(DoseAmount, System.Globalization.CultureInfo.InvariantCulture),
            Unit = Unit,
            Stock = Stock is null ? null : decimal.Parse(Stock, System.Globalization.CultureInfo.InvariantCulture),
            RefillWarnDays = RefillWarnDays,
            ReminderDeviceId = ReminderDeviceId,
            StartDate = DateOnly.FromDayNumber(StartDate),
            EndDate = EndDate is { } end ? DateOnly.FromDayNumber(end) : null,
            FirstDoseFrom = FirstDoseFromTicks is { } first ? new DateTime(first, DateTimeKind.Unspecified) : null,
            Notes = Notes,
            AddedBy = AddedBy,
            UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtMs),
            IsDeleted = IsDeleted,
        };
    }

    [Table("Doses")]
    internal sealed class DoseRow
    {
        [PrimaryKey]
        public string Id { get; set; } = "";

        public string MedicineId { get; set; } = "";

        [Indexed]
        public string HomeId { get; set; } = "";

        [Indexed]
        public long ScheduledAtTicks { get; set; }

        public int Status { get; set; }

        public long RecordedAtMs { get; set; }

        public string? By { get; set; }

        public long? LaterUntilTicks { get; set; }

        public static DoseRow From(DoseEvent e) => new()
        {
            Id = e.Id,
            MedicineId = e.MedicineId,
            HomeId = e.HomeId,
            ScheduledAtTicks = e.ScheduledAt.Ticks,
            Status = (int)e.Status,
            RecordedAtMs = e.RecordedAt.ToUnixTimeMilliseconds(),
            By = e.By,
            LaterUntilTicks = e.LaterUntil?.Ticks,
        };

        public DoseEvent ToEvent() => new(
            Id,
            MedicineId,
            HomeId,
            new DateTime(ScheduledAtTicks, DateTimeKind.Unspecified),
            (DoseStatus)Status,
            DateTimeOffset.FromUnixTimeMilliseconds(RecordedAtMs),
            By,
            LaterUntilTicks is { } t ? new DateTime(t, DateTimeKind.Unspecified) : null);
    }
}
