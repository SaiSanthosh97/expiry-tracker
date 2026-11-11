using HomeTracker.Core.DateExtraction;

namespace HomeTracker.Core.LabelCapture;

/// <param name="RawText">Everything that was read, for troubleshooting and for other parsers (e.g. FSSAI licence numbers).</param>
public sealed record LabelReading(LabelDateResult Dates, string RawText);

/// <summary>
/// Turns photos of a pack into data: the product barcode, and the expiry information printed on it.
/// Platform-independent; the actual image reading is done by the injected readers.
/// </summary>
public sealed class LabelReader(IBarcodeReader barcodes, ITextReader text, LabelDateExtractor extractor)
{
    public async Task<LabelReading> ReadDatesAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        var raw = await text.ReadAsync(imagePath, cancellationToken);
        return new LabelReading(extractor.Extract(raw), raw);
    }

    /// <returns>The first valid retail product code in the photo, or null.</returns>
    public async Task<string?> ReadProductBarcodeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        var found = await barcodes.ReadAsync(imagePath, cancellationToken);
        return found
            .Where(b => b.Kind == BarcodeKind.Product)
            .Select(b => b.Value.Trim())
            .FirstOrDefault(IsValidProductCode);
    }

    /// <summary>
    /// True for an 8, 12, 13 or 14 digit GTIN with a correct check digit. Rejects misreads before
    /// they are looked up or saved.
    /// </summary>
    public static bool IsValidProductCode(string code)
    {
        if (code.Length is not (8 or 12 or 13 or 14) || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        // GTIN check digit: from the right (excluding the check digit), weights alternate 3, 1, 3, 1...
        var sum = 0;
        for (var i = code.Length - 2; i >= 0; i--)
        {
            var weight = (code.Length - 2 - i) % 2 == 0 ? 3 : 1;
            sum += (code[i] - '0') * weight;
        }

        var check = (10 - (sum % 10)) % 10;
        return check == code[^1] - '0';
    }
}
