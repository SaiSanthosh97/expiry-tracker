using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HomeTracker.Core.ProductCatalog;

/// <summary>
/// Looks up products in Open Food Facts (free, open data under the Open Database License).
/// Only the barcode number is sent. Apps using this must show "Product data from Open Food Facts".
/// </summary>
public sealed class OpenFoodFactsProvider : IProductInfoProvider
{
    private const string Fields = "product_name,product_name_en,brands,categories_tags";

    private readonly HttpClient _http;
    private readonly ProductInfoHeaderValue _userAgent;
    private readonly ProductInfoHeaderValue _contact;

    /// <param name="appName">Identifies the app to Open Food Facts, as their API rules require.</param>
    /// <param name="appVersion">App version.</param>
    /// <param name="contact">Project URL or contact, as their API rules require.</param>
    public OpenFoodFactsProvider(HttpClient http, string appName, string appVersion, string contact)
    {
        _http = http;
        _userAgent = new ProductInfoHeaderValue(appName, appVersion);
        _contact = new ProductInfoHeaderValue($"({contact})");
    }

    public async Task<ProductInfo?> FindAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (!barcode.All(char.IsAsciiDigit) || barcode.Length is < 8 or > 14)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://world.openfoodfacts.org/api/v2/product/{barcode}?fields={Fields}");
        request.Headers.UserAgent.Add(_userAgent);
        request.Headers.UserAgent.Add(_contact);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return Parse(barcode, json.RootElement);
    }

    internal static ProductInfo? Parse(string barcode, JsonElement root)
    {
        if (!root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Number || status.GetInt32() != 1
            || !root.TryGetProperty("product", out var product))
        {
            return null;
        }

        var name = Text(product, "product_name_en") ?? Text(product, "product_name");
        if (name is null)
        {
            return null;
        }

        var brand = Text(product, "brands")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var tags = product.TryGetProperty("categories_tags", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
            : [];

        return new ProductInfo(barcode, name, brand, OpenFoodFactsCategories.ToCategoryId(tags), ProductSource.OpenFoodFacts);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
}
