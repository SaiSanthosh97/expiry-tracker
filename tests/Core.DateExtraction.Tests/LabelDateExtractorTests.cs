using HomeTracker.Core.ShelfLife;

namespace HomeTracker.Core.DateExtraction.Tests;

public class LabelDateExtractorTests
{
    private static readonly LabelDateExtractor Extractor = new(() => new DateOnly(2026, 11, 11));

    [Theory]
    [InlineData("EXP 05/2027", 2027, 5, 31)]
    [InlineData("EXP.DATE:12/2027", 2027, 12, 31)]
    [InlineData("Expiry Date 02/28", 2028, 2, 29)]
    [InlineData("Use by 12.03.26", 2026, 3, 12)]
    [InlineData("USE BEFORE 01-06-2027", 2027, 6, 1)]
    [InlineData("Best Before 15/08/2027", 2027, 8, 15)]
    [InlineData("EXP MAY 2027", 2027, 5, 31)]
    [InlineData("Exp: May'27", 2027, 5, 31)]
    [InlineData("EXP 05MAY2027", 2027, 5, 5)]
    [InlineData("exp. date (MM/YY): 09/27", 2027, 9, 30)]
    [InlineData("EXP 2027-04-30", 2027, 4, 30)]
    [InlineData("EX 05/2027", 2027, 5, 31)]
    [InlineData("EX: 12.26", 2026, 12, 31)]
    [InlineData("EX.DT. 03/28", 2028, 3, 31)]
    [InlineData("EXP05/2027", 2027, 5, 31)]
    [InlineData("EX10.2027", 2027, 10, 31)]
    [InlineData("EXPY 08 2027", 2027, 8, 31)]
    [InlineData("E.D. 15/09/2027", 2027, 9, 15)]
    [InlineData("Best before 01.27", 2027, 1, 31)]
    public void Reads_labelled_expiry_with_high_confidence(string text, int year, int month, int day)
    {
        var result = Extractor.Extract(text);

        Assert.Equal(new DateOnly(year, month, day), result.ExpiryLastUsableDay);
        Assert.Equal(ExtractionConfidence.High, result.Confidence);
    }

    [Fact]
    public void Reads_manufacture_and_expiry_on_one_line()
    {
        var result = Extractor.Extract("B.No. AX123 MFG.DATE: 01/2026 EXP.DATE: 12/2027");

        Assert.Equal(new DateOnly(2027, 12, 31), result.ExpiryLastUsableDay);
        Assert.Equal(new DateOnly(2026, 1, 1), result.Manufacture?.Date);
    }

    [Fact]
    public void Reads_table_header_followed_by_values()
    {
        var result = Extractor.Extract("MFG.  EXP.\n01/26  12/27");

        Assert.Equal(new DateOnly(2027, 12, 31), result.ExpiryLastUsableDay);
        Assert.Equal(new DateOnly(2026, 1, 1), result.Manufacture?.Date);
        Assert.Equal(ExtractionConfidence.High, result.Confidence);
    }

    [Fact]
    public void Calculates_expiry_from_manufacture_month_and_best_before_months()
    {
        var result = Extractor.Extract("MFD 01/2026\nBEST BEFORE 12 MONTHS FROM MANUFACTURE");

        // Counted from 1 January (earliest possible manufacture day).
        Assert.Equal(new DateOnly(2026, 12, 31), result.ExpiryLastUsableDay);
        Assert.Equal(ShelfLifeDuration.Months(12), result.BestBeforeFromManufacture);
        Assert.Equal(ExtractionConfidence.Medium, result.Confidence);
    }

    [Fact]
    public void Calculates_expiry_from_packed_day_and_best_before()
    {
        var result = Extractor.Extract("PKD: 15/01/2026 Best before 6 months from packaging");

        Assert.Equal(new DateOnly(2026, 7, 14), result.ExpiryLastUsableDay);
    }

    [Fact]
    public void Best_before_phrase_before_manufacture_date_is_not_read_as_expiry_label()
    {
        var result = Extractor.Extract("Best before 9 months. Mfd: 03/26");

        Assert.Equal(new DateOnly(2026, 11, 30), result.ExpiryLastUsableDay);
        Assert.Null(result.Expiry);
    }

    [Fact]
    public void Shelf_life_in_years_is_converted_to_months()
    {
        var result = Extractor.Extract("Shelf life: 2 years from mfg. MFG 06/2025");

        Assert.Equal(ShelfLifeDuration.Months(24), result.BestBeforeFromManufacture);
        Assert.Equal(new DateOnly(2027, 5, 31), result.ExpiryLastUsableDay);
    }

    [Fact]
    public void Earliest_date_wins_when_printed_and_calculated_expiry_disagree()
    {
        var result = Extractor.Extract("MFD 01/2026 BEST BEFORE 6 MONTHS FROM MFG EXP 12/2026");

        Assert.Equal(new DateOnly(2026, 6, 30), result.ExpiryLastUsableDay);
    }

    [Fact]
    public void Reads_after_opening_rule()
    {
        var result = Extractor.Extract("Use within 28 days of opening. EXP 03/2028");

        Assert.Equal(ShelfLifeDuration.Days(28), result.AfterOpening);
        Assert.Equal(new DateOnly(2028, 3, 31), result.ExpiryLastUsableDay);
    }

    [Fact]
    public void Ignores_prices()
    {
        var result = Extractor.Extract("MRP Rs. 10.25 (Incl. of all taxes) EXP 03/2027");

        Assert.Equal(new DateOnly(2027, 3, 31), result.ExpiryLastUsableDay);
        Assert.Single(result.AllDates);
    }

    [Fact]
    public void Ignores_manufacturing_licence_numbers()
    {
        var result = Extractor.Extract("Mfg. Lic. No. 21/KD/2007 EXP 08/2027");

        Assert.Equal(new DateOnly(2027, 8, 31), result.ExpiryLastUsableDay);
        Assert.Null(result.Manufacture);
    }

    [Fact]
    public void Manufacturer_name_label_does_not_claim_a_later_date()
    {
        var result = Extractor.Extract("Mfd by: Sunrise Foods Pvt Ltd, Hyderabad 500032\n01/2027");

        Assert.Null(result.Manufacture);
        Assert.Equal(ExtractionConfidence.Low, result.Confidence);
    }

    [Fact]
    public void Fixes_common_ocr_letter_digit_mixups()
    {
        Assert.Equal(new DateOnly(2027, 5, 31), Extractor.Extract("EXP 05/2O27").ExpiryLastUsableDay);
        Assert.Equal(new DateOnly(2027, 11, 30), Extractor.Extract("EXP I1/2027").ExpiryLastUsableDay);
    }

    [Fact]
    public void Swaps_month_and_day_when_middle_number_is_over_twelve_with_lower_confidence()
    {
        var result = Extractor.Extract("EXP 05/25/2027");

        Assert.Equal(new DateOnly(2027, 5, 25), result.ExpiryLastUsableDay);
        Assert.Equal(ExtractionConfidence.Medium, result.Confidence);
    }

    [Fact]
    public void Two_unlabelled_dates_later_one_is_expiry_with_low_confidence()
    {
        var result = Extractor.Extract("01/2026\n12/2027");

        Assert.Equal(new DateOnly(2027, 12, 31), result.ExpiryLastUsableDay);
        Assert.Equal(new DateOnly(2026, 1, 1), result.Manufacture?.Date);
        Assert.Equal(ExtractionConfidence.Low, result.Confidence);
    }

    [Fact]
    public void Dotted_month_year_after_expiry_label_is_read_but_dotted_numbers_elsewhere_are_not()
    {
        var result = Extractor.Extract("MRP 45.00 NET WT 10.25 KG" + "\n" + "MFG 06.26 EX 05.27");

        Assert.Equal(new DateOnly(2027, 5, 31), result.ExpiryLastUsableDay);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Manufacture?.Date);
        Assert.Equal(ExtractionConfidence.High, result.Confidence);
        Assert.Equal(2, result.AllDates.Count);
    }

    [Fact]
    public void Manufacture_date_alone_gives_no_expiry()
    {
        var result = Extractor.Extract("MFD 01/2026");

        Assert.Null(result.ExpiryLastUsableDay);
        Assert.Equal(new DateOnly(2026, 1, 1), result.Manufacture?.Date);
        Assert.Equal(ExtractionConfidence.None, result.Confidence);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Net Wt. 500 g  Store in a cool dry place")]
    [InlineData("EXP 13/2027")]
    [InlineData("EXP 31/02/2027")]
    [InlineData("EXP 05/1990")]
    [InlineData("Price 10.25")]
    [InlineData("Net wt 12 2027 g")]
    [InlineData("Excellent taste")]
    public void Returns_nothing_when_no_valid_date(string? text)
    {
        var result = Extractor.Extract(text);

        Assert.Null(result.ExpiryLastUsableDay);
        Assert.Equal(ExtractionConfidence.None, result.Confidence);
    }
}
