using HomeTracker.Core.Items;
using HomeTracker.Core.ShelfLife;

namespace ExpiryTracker.App.Services;

/// <summary>App-level operations on items. Used by the screens and by background reminder actions.</summary>
public sealed class ItemService(IItemRepository repository, ItemEvaluator evaluator, Clock clock)
{
    /// <summary>Raised after any change, possibly from a background thread.</summary>
    public event EventHandler? Changed;

    public DateOnly Today => clock.Today;

    public async Task<IReadOnlyList<ItemEvaluation>> GetOpenAsync() =>
        evaluator.EvaluateAll(await repository.GetAllAsync(), clock.Today);

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

    /// <returns>Number of items added or updated.</returns>
    /// <exception cref="FormatException">Not a valid list file.</exception>
    public async Task<int> ImportAsync(string json)
    {
        var incoming = ItemExport.Deserialize(json);
        var changes = ItemExport.ChangesToApply(await repository.GetEverythingAsync(), incoming);
        foreach (var item in changes)
        {
            await repository.SaveAsync(item);
        }

        if (changes.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return changes.Count;
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

    private async Task StoreAsync(TrackedItem item)
    {
        await repository.SaveAsync(item);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
