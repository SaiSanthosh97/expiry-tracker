namespace HomeTracker.Core.LabelCapture;

public enum BarcodeKind
{
    /// <summary>Retail product code: EAN-13, EAN-8, UPC-A, UPC-E.</summary>
    Product,
    QrCode,
    Other,
}

public sealed record ScannedBarcode(string Value, BarcodeKind Kind);

/// <summary>Finds barcodes in a photo. Implemented per platform (e.g. Google ML Kit on Android).</summary>
public interface IBarcodeReader
{
    Task<IReadOnlyList<ScannedBarcode>> ReadAsync(string imagePath, CancellationToken cancellationToken = default);
}

/// <summary>One printed line and where it is in the photo (pixels, photo the right way up).</summary>
public sealed record TextLine(string Text, float Left, float Top, float Width, float Height);

/// <param name="Text">All text, one line per printed line.</param>
public sealed record TextReading(string Text, IReadOnlyList<TextLine> Lines)
{
    public static TextReading Empty { get; } = new("", []);
}

/// <summary>Reads printed text from a photo, line by line. Implemented per platform.</summary>
public interface ITextReader
{
    Task<TextReading> ReadAsync(string imagePath, CancellationToken cancellationToken = default);
}
