using HomeTracker.Core.Items;
using HomeTracker.Core.ShelfLife;

namespace ExpiryTracker.App.Services;

/// <param name="items">The items as saved.</param>
/// <param name="fromSync">True when the change came from another family phone (so it isn't sent back out).</param>
public sealed class ItemsChangedEventArgs(IReadOnlyList<TrackedItem> items, bool fromSync) : EventArgs
{
    public IReadOnlyList<TrackedItem> Items { get; } = items;

    public bool FromSync { get; } = fromSync;
}

/// <summary>App-level operations on items. Used by the screens, by background reminder actions and by family sync.</summary>
public sealed class ItemService(IItemRepository repository, ItemEvaluator evaluator, Clock clock)
{
    /// <summary>Raised after any change, possibly from a background thread.</summary>
    public event EventHandler<ItemsChangedEventArgs>? Changed;

    public DateOnly Today => clock.Today;

    /// <summary>Open items of every home.</summary>
    public async Task<IReadOnlyList<ItemEvaluation>> GetOpenAsync() =>
        evaluator.EvaluateAll(await repository.GetAllAsync(), clock.Today);

    /// <summary>Open items of one home.</summary>
    public async Task<IReadOnlyList<ItemEvaluation>> GetOpenAsync(string homeId) =>
        evaluator.EvaluateAll((await repository.GetAllAsync()).Where(i => i.HomeId == homeId), clock.Today);

    public async Task<ItemEvaluation?> GetAsync(string id) =>
        await repository.GetAsync(id) is { } item ? evaluator.Evaluate(item, clock.Today) : null;

    public ItemEvaluation Preview(TrackedItem draft) => evaluator.Evaluate(draft, clock.Today);

    public Task SaveAsync(TrackedItem item) => StoreAsync(item with { UpdatedAt = clock.Now });

    public Task<TrackedItem?> CloseAsync(string id, ClosedReason reason) =>
        UpdateAsync(id, item => ItemActions.Close(item, reason, clock.Today, clock.Now));

    public Task<TrackedItem?> ReopenAsync(string id) =>
        UpdateAsync(id, item => ItemActions.Reopen(item, clock.Now));

    public Task<TrackedItem?> MarkOpenedAsync(string id, ShelfLifeDuration? afterOpening) =>
        UpdateAsync(id, item => ItemActions.MarkOpened(item, clock.Today, afterOpening, clock.Now));

    public Task<TrackedItem?> SnoozeAsync(string id) =>
        UpdateAsync(id, item => ItemActions.SnoozeUntilTomorrow(item, clock.Today, clock.Now));

    public Task<TrackedItem?> DeleteAsync(string id) =>
        UpdateAsync(id, item => ItemActions.Delete(item, clock.Now));

    public async Task<string> ExportAsync() =>
        ItemExport.Serialize(await repository.GetEverythingAsync(), clock.Now);

    /// <summary>Imports a list file. Items without a known home go into <paramref name="homeId"/>.</summary>
    /// <returns>Number of items added or updated.</returns>
    /// <exception cref="FormatException">Not a valid list file.</exception>
    public async Task<int> ImportAsync(string json, string homeId, IReadOnlySet<string> knownHomes)
    {
        var incoming = ItemExport.Deserialize(json)
            .Select(i => i.HomeId is { } h && knownHomes.Contains(h) ? i : i with { HomeId = homeId })
            .ToList();
        var changes = ItemExport.ChangesToApply(await repository.GetEverythingAsync(), incoming);
        await SaveAllAsync(changes, fromSync: false);
        return changes.Count;
    }

    /// <summary>Saves items received from another family phone, keeping whichever copy changed last.</summary>
    /// <returns>The items that were new or newer here.</returns>
    public async Task<IReadOnlyList<TrackedItem>> ApplyFromFamilyAsync(IReadOnlyList<TrackedItem> incoming)
    {
        var changes = ItemExport.ChangesToApply(await repository.GetEverythingAsync(), incoming);
        await SaveAllAsync(changes, fromSync: true);
        return changes;
    }

    /// <summary>Every item of a home, including closed and deleted ones (so those changes reach other phones too).</summary>
    public async Task<IReadOnlyList<TrackedItem>> GetEverythingAsync(string homeId) =>
        (await repository.GetEverythingAsync()).Where(i => i.HomeId == homeId).ToList();

    public async Task<IReadOnlyList<TrackedItem>> GetByIdsAsync(IReadOnlyCollection<string> ids)
    {
        var wanted = ids.ToHashSet();
        return (await repository.GetEverythingAsync()).Where(i => wanted.Contains(i.Id)).ToList();
    }

    /// <summary>Moves items saved before homes existed into <paramref name="homeId"/> (not sent to family).</summary>
    public async Task AdoptItemsWithoutHomeAsync(string homeId)
    {
        var orphans = (await repository.GetEverythingAsync()).Where(i => i.HomeId is null).ToList();
        foreach (var item in orphans)
        {
            await repository.SaveAsync(item with { HomeId = homeId });
        }
    }

    /// <summary>Removes a home's items from this phone only.</summary>
    public async Task PurgeHomeAsync(string homeId)
    {
        await repository.PurgeHomeAsync(homeId);
        Changed?.Invoke(this, new ItemsChangedEventArgs([], fromSync: true));
    }

    private async Task SaveAllAsync(IReadOnlyList<TrackedItem> items, bool fromSync)
    {
        foreach (var item in items)
        {
            await repository.SaveAsync(item);
        }

        if (items.Count > 0)
        {
            Changed?.Invoke(this, new ItemsChangedEventArgs(items, fromSync));
        }
    }

    private async Task<TrackedItem?> UpdateAsync(string id, Func<TrackedItem, TrackedItem> change)
    {
        // Read including closed items, so Reopen works on a closed one.
        var item = (await repository.GetAllAsync(includeClosed: true)).FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            return null;
        }

        var updated = change(item);
        await StoreAsync(updated);
        return updated;
    }

    private Task StoreAsync(TrackedItem item) => SaveAllAsync([item], fromSync: false);
}
