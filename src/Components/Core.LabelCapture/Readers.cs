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

/// <summary>Reads printed text from a photo, line by line. Implemented per platform.</summary>
public interface ITextReader
{
    Task<string> ReadAsync(string imagePath, CancellationToken cancellationToken = default);
}
