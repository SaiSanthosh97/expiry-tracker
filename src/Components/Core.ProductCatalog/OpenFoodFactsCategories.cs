using HomeTracker.Core.ShelfLife;

namespace HomeTracker.Core.ProductCatalog;

/// <summary>Maps Open Food Facts category tags (e.g. "en:milk-chocolates") to shelf-life category ids.</summary>
public static class OpenFoodFactsCategories
{
    // Checked in order: the first rule that matches any tag wins, so specific rules come first
    // ("milk chocolate" is a chocolate, not milk).
    private static readonly (string[] Keywords, string CategoryId)[] Rules =
    [
        (["chocolate"], DefaultPresets.Chocolates.Id),
        (["ketchup", "sauce", "mayonnaise", "condiment"], DefaultPresets.SaucesAndKetchup.Id),
        (["jam", "spread", "honey", "marmalade"], DefaultPresets.JamAndSpreads.Id),
        (["en:milks", "en:dairy-milks", "en:cow-milks", "en:fresh-milks", "en:uht-milks"], DefaultPresets.Milk.Id),
        (["cosmetic", "shampoo", "soap"], DefaultPresets.Cosmetics.Id),
    ];

    /// <returns>A category id; packaged food when nothing more specific matches, null when there are no tags.</returns>
    public static string? ToCategoryId(IReadOnlyCollection<string> tags)
    {
        if (tags.Count == 0)
        {
            return null;
        }

        foreach (var (keywords, categoryId) in Rules)
        {
            if (tags.Any(tag => keywords.Any(k => k.Contains(':')
                    ? tag.Equals(k, StringComparison.OrdinalIgnoreCase)
                    : tag.Contains(k, StringComparison.OrdinalIgnoreCase))))
            {
                return categoryId;
            }
        }

        return DefaultPresets.PackagedFood.Id;
    }
}
