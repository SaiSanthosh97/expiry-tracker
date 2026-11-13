using HomeTracker.Core.LabelCapture;

namespace HomeTracker.Core.LabelCapture.Tests;

public class ProductNameGuesserTests
{
    private static TextLine Line(string text, float top, float height, float left = 0, float width = 400) =>
        new(text, left, top, width, height);

    [Fact]
    public void Picks_the_largest_text_and_title_cases_all_caps()
    {
        var name = ProductNameGuesser.Guess(
        [
            Line("Hazelnut spread with cocoa", 200, 30),
            Line("NUTELLA", 50, 120),
            Line("Net Wt. 350 g", 600, 25),
        ]);

        Assert.Equal("Nutella", name);
    }

    [Fact]
    public void Joins_a_second_line_of_similar_size_below()
    {
        var name = ProductNameGuesser.Guess(
        [
            Line("AMUL", 40, 100),
            Line("BUTTER", 150, 90),
            Line("Pasteurised table butter", 300, 30),
        ]);

        Assert.Equal("Amul Butter", name);
    }

    [Fact]
    public void Skips_dates_prices_and_small_print_even_when_large()
    {
        var name = ProductNameGuesser.Guess(
        [
            Line("EXP 05/2027", 10, 150),
            Line("MRP Rs. 120.00", 200, 140),
            Line("Paracetamol Tablets", 400, 60),
            Line("Batch No. AX123", 500, 30),
        ]);

        Assert.Equal("Paracetamol Tablets", name);
    }

    [Fact]
    public void Keeps_mixed_case_names_as_printed()
    {
        Assert.Equal("Haldiram's Soan Papdi", ProductNameGuesser.Guess([Line("Haldiram's Soan Papdi®", 10, 80)]));
    }

    [Fact]
    public void Returns_null_when_nothing_looks_like_a_name()
    {
        Assert.Null(ProductNameGuesser.Guess([Line("8901234567890", 10, 80), Line("12/26", 100, 40)]));
        Assert.Null(ProductNameGuesser.Guess([]));
    }
}
