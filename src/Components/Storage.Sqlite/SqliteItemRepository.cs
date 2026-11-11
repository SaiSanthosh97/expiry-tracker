using HomeTracker.Core.Items;
using SQLite;

namespace HomeTracker.Storage.Sqlite;

/// <summary>Stores items in a local SQLite file. Safe to use from the UI and from background reminders at the same time.</summary>
public sealed class SqliteItemRepository : IItemRepository, IAsyncDisposable
{
    private readonly SQLiteAsyncConnection _connection;
    private readonly Lazy<Task> _initialized;

    public SqliteItemRepository(string databasePath)
    {
        _connection = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache | SQLiteOpenFlags.FullMutex);
        _initialized = new Lazy<Task>(() => _connection.CreateTableAsync<ItemRow>());
    }

    public async Task<IReadOnlyList<TrackedItem>> GetAllAsync(bool includeClosed = false)
    {
        await _initialized.Value;
        var query = _connection.Table<ItemRow>().Where(r => !r.IsDeleted);
        if (!includeClosed)
        {
            query = query.Where(r => r.ClosedDate == null);
        }

        var rows = await query.ToListAsync();
        return rows.Select(r => r.ToItem()).ToList();
    }

    public async Task<TrackedItem?> GetAsync(string id)
    {
        await _initialized.Value;
        var row = await _connection.FindAsync<ItemRow>(id);
        return row is { IsDeleted: false } ? row.ToItem() : null;
    }

    public async Task SaveAsync(TrackedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        await _initialized.Value;
        await _connection.InsertOrReplaceAsync(ItemRow.From(item));
    }

    public async Task<IReadOnlyList<TrackedItem>> GetEverythingAsync()
    {
        await _initialized.Value;
        var rows = await _connection.Table<ItemRow>().ToListAsync();
        return rows.Select(r => r.ToItem()).ToList();
    }

    public ValueTask DisposeAsync() => new(_connection.CloseAsync());
}
