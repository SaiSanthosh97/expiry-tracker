using System.Text.Json;

namespace HomeTracker.Core.ProductCatalog.Tests;

public class OpenFoodFactsTests
{
    private static ProductInfo? Parse(string json) =>
        OpenFoodFactsProvider.Parse("3017620422003", JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Parses_found_product()
    {
        var product = Parse("""
            {"status":1,"product":{"product_name":"Nutella","brands":"Ferrero, Nutella","categories_tags":["en:spreads","en:sweet-spreads","en:cocoa-and-hazelnuts-spreads"]}}
            """);

        Assert.NotNull(product);
        Assert.Equal("Nutella", product.Name);
        Assert.Equal("Ferrero", product.Brand);
        Assert.Equal("jam", product.CategoryId);
        Assert.Equal(ProductSource.OpenFoodFacts, product.Source);
    }

    [Fact]
    public void Prefers_english_name()
    {
        var product = Parse("""{"status":1,"product":{"product_name":"दूध","product_name_en":"Toned milk","categories_tags":["en:milks"]}}""");

        Assert.Equal("Toned milk", product?.Name);
        Assert.Equal("milk", product?.CategoryId);
    }

    [Theory]
    [InlineData("""{"status":0,"status_verbose":"product not found"}""")]
    [InlineData("""{"status":1,"product":{"product_name":""}}""")]
    [InlineData("""{"status":1}""")]
    public void Missing_or_nameless_product_is_not_found(string json)
    {
        Assert.Null(Parse(json));
    }

    [Theory]
    [InlineData("en:milk-chocolates", "chocolates")]
    [InlineData("en:tomato-ketchup", "sauces")]
    [InlineData("en:fruit-jams", "jam")]
    [InlineData("en:milks", "milk")]
    [InlineData("en:biscuits", "packaged-food")]
    public void Maps_categories(string tag, string expected)
    {
        Assert.Equal(expected, OpenFoodFactsCategories.ToCategoryId([tag]));
    }

    [Fact]
    public void No_tags_means_no_category()
    {
        Assert.Null(OpenFoodFactsCategories.ToCategoryId([]));
    }
}

public class ProductLookupTests
{
    private sealed class Provider(Func<ProductInfo?> answer) : IProductInfoProvider
    {
        public Task<ProductInfo?> FindAsync(string barcode, CancellationToken cancellationToken = default) => Task.FromResult(answer());
    }

    private static readonly ProductInfo Known = new("123", "Biscuits", null, "packaged-food", ProductSource.Household);

    [Fact]
    public async Task First_provider_with_an_answer_wins()
    {
        var lookup = new ProductLookup([new Provider(() => null), new Provider(() => Known), new Provider(() => throw new InvalidOperationException())]);

        var result = await lookup.FindAsync("123");

        Assert.Equal(Known, result.Product);
        Assert.False(result.SomeSourcesUnavailable);
    }

    [Fact]
    public async Task Offline_source_is_reported_but_others_still_answer()
    {
        var lookup = new ProductLookup([new Provider(() => throw new HttpRequestException("offline")), new Provider(() => null)]);

        var result = await lookup.FindAsync("123");

        Assert.Null(result.Product);
        Assert.True(result.SomeSourcesUnavailable);
    }
}
