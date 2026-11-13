using HomeTracker.Core.DateExtraction;

namespace HomeTracker.Core.LabelCapture.Tests;

public class LabelReaderTests
{
    private sealed class FakeBarcodes(params ScannedBarcode[] codes) : IBarcodeReader
    {
        public Task<IReadOnlyList<ScannedBarcode>> ReadAsync(string imagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScannedBarcode>>(codes);
    }

    private sealed class FakeText(string text) : ITextReader
    {
        public Task<TextReading> ReadAsync(string imagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TextReading(text, text.Split('\n').Select((l, i) => new TextLine(l, 0, i * 40, 200, 30)).ToList()));
    }

    private static LabelReader Reader(string text = "", params ScannedBarcode[] codes) =>
        new(new FakeBarcodes(codes), new FakeText(text), new LabelDateExtractor(() => new DateOnly(2026, 11, 11)));

    [Theory]
    [InlineData("3017620422003")] // EAN-13
    [InlineData("8901719110016")] // EAN-13 (India, 890 prefix)
    [InlineData("96385074")]      // EAN-8
    [InlineData("036000291452")]  // UPC-A
    public void Accepts_valid_product_codes(string code)
    {
        Assert.True(LabelReader.IsValidProductCode(code));
    }

    [Theory]
    [InlineData("3017620422004")] // wrong check digit
    [InlineData("301762042200")]  // 12 digits but not a valid UPC
    [InlineData("12345")]
    [InlineData("30176204220O3")]
    [InlineData("")]
    public void Rejects_invalid_product_codes(string code)
    {
        Assert.False(LabelReader.IsValidProductCode(code));
    }

    [Fact]
    public async Task Picks_first_valid_product_barcode_and_ignores_qr_codes()
    {
        var reader = Reader("", new ScannedBarcode("https://example.com", BarcodeKind.QrCode),
            new ScannedBarcode("3017620422004", BarcodeKind.Product),
            new ScannedBarcode("3017620422003", BarcodeKind.Product));

        Assert.Equal("3017620422003", await reader.ReadProductBarcodeAsync("photo.jpg"));
    }

    [Fact]
    public async Task No_barcode_returns_null()
    {
        Assert.Null(await Reader().ReadProductBarcodeAsync("photo.jpg"));
    }

    [Fact]
    public async Task Reads_dates_from_photo_text()
    {
        var reading = await Reader("MRP Rs. 40.00\nMFG 01/2026\nEXP 12/2027").ReadDatesAsync("photo.jpg");

        Assert.Equal(new DateOnly(2027, 12, 31), reading.Dates.ExpiryLastUsableDay);
        Assert.Contains("EXP 12/2027", reading.RawText);
    }
}
