namespace HomeTracker.Core.Items;

public interface IItemRepository
{
    /// <summary>All items that are not deleted, including closed ones when <paramref name="includeClosed"/> is true.</summary>
    Task<IReadOnlyList<TrackedItem>> GetAllAsync(bool includeClosed = false);

    Task<TrackedItem?> GetAsync(string id);

    /// <summary>Insert or replace.</summary>
    Task SaveAsync(TrackedItem item);

    /// <summary>Every row, including deleted ones (for export and merging).</summary>
    Task<IReadOnlyList<TrackedItem>> GetEverythingAsync();
}
