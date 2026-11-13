using HomeTracker.Core.Households;
using SQLite;

namespace HomeTracker.Storage.Sqlite;

/// <summary>Stores homes (with their secret keys) and family phones in the app's private SQLite file.</summary>
public sealed class SqliteHomeRepository : IHomeRepository, IAsyncDisposable
{
    private readonly SQLiteAsyncConnection _connection;
    private readonly Lazy<Task> _initialized;

    public SqliteHomeRepository(string databasePath)
    {
        _connection = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache | SQLiteOpenFlags.FullMutex);
        _initialized = new Lazy<Task>(async () =>
        {
            await _connection.CreateTableAsync<HomeRow>();
            await _connection.CreateTableAsync<DeviceRow>();
        });
    }

    public async Task<IReadOnlyList<Home>> GetHomesAsync()
    {
        await _initialized.Value;
        var rows = await _connection.Table<HomeRow>().OrderBy(r => r.CreatedAtMs).ToListAsync();
        return rows.Select(r => r.ToHome()).ToList();
    }

    public async Task SaveHomeAsync(Home home)
    {
        await _initialized.Value;
        await _connection.InsertOrReplaceAsync(HomeRow.From(home));
    }

    public async Task DeleteHomeAsync(string homeId)
    {
        await _initialized.Value;
        await _connection.DeleteAsync<HomeRow>(homeId);
        await _connection.ExecuteAsync("DELETE FROM Devices WHERE HomeId = ?", homeId);
    }

    public async Task<IReadOnlyList<FamilyDevice>> GetDevicesAsync(string homeId)
    {
        await _initialized.Value;
        var rows = await _connection.Table<DeviceRow>().Where(r => r.HomeId == homeId).ToListAsync();
        return rows.Select(r => r.ToDevice()).ToList();
    }

    public async Task SaveDeviceAsync(FamilyDevice device)
    {
        await _initialized.Value;
        await _connection.InsertOrReplaceAsync(DeviceRow.From(device));
    }

    public ValueTask DisposeAsync() => new(_connection.CloseAsync());

    [Table("Homes")]
    internal sealed class HomeRow
    {
        [PrimaryKey]
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string? KeyBase64 { get; set; }

        public string? Topic { get; set; }

        public string RelayUrl { get; set; } = "";

        public long CreatedAtMs { get; set; }

        public static HomeRow From(Home home) => new()
        {
            Id = home.Id,
            Name = home.Name,
            KeyBase64 = home.Key is null ? null : Convert.ToBase64String(home.Key),
            Topic = home.Topic,
            RelayUrl = home.RelayUrl.ToString(),
            CreatedAtMs = home.CreatedAt.ToUnixTimeMilliseconds(),
        };

        public Home ToHome() => new(
            Id,
            Name,
            KeyBase64 is null ? null : Convert.FromBase64String(KeyBase64),
            Topic,
            Uri.TryCreate(RelayUrl, UriKind.Absolute, out var relay) ? relay : Home.DefaultRelay,
            DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs));
    }

    [Table("Devices")]
    internal sealed class DeviceRow
    {
        /// <summary>HomeId + "/" + DeviceId: the same phone can be in several homes.</summary>
        [PrimaryKey]
        public string Key { get; set; } = "";

        [Indexed]
        public string HomeId { get; set; } = "";

        public string DeviceId { get; set; } = "";

        public string Name { get; set; } = "";

        public long LastSeenMs { get; set; }

        public long? SilentAlertSentMs { get; set; }

        public static DeviceRow From(FamilyDevice device) => new()
        {
            Key = device.HomeId + "/" + device.DeviceId,
            HomeId = device.HomeId,
            DeviceId = device.DeviceId,
            Name = device.Name,
            LastSeenMs = device.LastSeen.ToUnixTimeMilliseconds(),
            SilentAlertSentMs = device.SilentAlertSent?.ToUnixTimeMilliseconds(),
        };

        public FamilyDevice ToDevice() => new(
            HomeId,
            DeviceId,
            Name,
            DateTimeOffset.FromUnixTimeMilliseconds(LastSeenMs),
            SilentAlertSentMs is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null);
    }
}
