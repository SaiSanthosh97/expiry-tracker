namespace HomeTracker.Core.ProductCatalog;

public enum ProductSource
{
    /// <summary>This household scanned the product before.</summary>
    Household,
    OpenFoodFacts,
}

/// <param name="CategoryId">A shelf-life category id (e.g. "chocolates"), when one could be worked out.</param>
public sealed record ProductInfo(string Barcode, string Name, string? Brand, string? CategoryId, ProductSource Source);

/// <summary>A source of product details by barcode.</summary>
public interface IProductInfoProvider
{
    /// <returns>The product, or null when this source doesn't know it.</returns>
    /// <exception cref="HttpRequestException">The source could not be reached.</exception>
    Task<ProductInfo?> FindAsync(string barcode, CancellationToken cancellationToken = default);
}

/// <param name="Product">The product, or null when not found.</param>
/// <param name="SomeSourcesUnavailable">True when a source couldn't be reached (e.g. offline), so "not found" may be wrong.</param>
public sealed record ProductLookupResult(ProductInfo? Product, bool SomeSourcesUnavailable);

/// <summary>Asks each provider in order and returns the first answer.</summary>
public sealed class ProductLookup(IEnumerable<IProductInfoProvider> providers)
{
    public async Task<ProductLookupResult> FindAsync(string barcode, CancellationToken cancellationToken = default)
    {
        var unavailable = false;
        foreach (var provider in providers)
        {
            try
            {
                if (await provider.FindAsync(barcode, cancellationToken) is { } product)
                {
                    return new ProductLookupResult(product, unavailable);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                unavailable = true;
            }
        }

        return new ProductLookupResult(null, unavailable);
    }
}
