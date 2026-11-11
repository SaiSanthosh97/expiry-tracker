namespace HomeTracker.Core.ShelfLife;

/// <summary>
/// The list of categories a household uses: built-in presets plus the household's own
/// changes (e.g. "Sweets" changed from 3 to 4 days) and custom categories.
/// </summary>
public sealed class ShelfLifeCatalog
{
    private readonly Dictionary<string, ShelfLifeCategory> _categories;

    public ShelfLifeCatalog(IEnumerable<ShelfLifeCategory>? overrides = null)
    {
        _categories = DefaultPresets.All.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var category in overrides ?? [])
        {
            _categories[category.Id] = category;
        }
    }

    public IReadOnlyCollection<ShelfLifeCategory> All => _categories.Values;

    public IEnumerable<ShelfLifeCategory> InGroup(CategoryGroup group) =>
        _categories.Values.Where(c => c.Group == group);

    public ShelfLifeCategory? Find(string id) =>
        _categories.TryGetValue(id, out var category) ? category : null;
}
