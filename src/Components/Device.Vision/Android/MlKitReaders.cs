using Android.Runtime;
using HomeTracker.Core.LabelCapture;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;
using AndroidApp = Android.App.Application;

namespace HomeTracker.Device.Vision;

/// <summary>Finds barcodes in a photo with Google ML Kit, entirely on the phone.</summary>
public sealed class MlKitBarcodeReader : IBarcodeReader
{
    public async Task<IReadOnlyList<ScannedBarcode>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        using var image = LoadImage(imagePath);
        using var scanner = BarcodeScanning.Client;
        Java.Lang.Object? result;
        try
        {
            result = await scanner.Process(image).AsTask().WaitAsync(cancellationToken);
        }
        finally
        {
            scanner.Close();
        }

        var barcodes = result?.JavaCast<JavaList>();
        if (barcodes is null)
        {
            return [];
        }

        return barcodes
            .Cast<Java.Lang.Object>()
            .Select(o => o.JavaCast<Barcode>())
            .Where(b => !string.IsNullOrEmpty(b?.RawValue))
            .Select(b => new ScannedBarcode(b!.RawValue!, KindOf(b.Format)))
            .ToList();
    }

    private static BarcodeKind KindOf(int format) => format switch
    {
        Barcode.FormatEan13 or Barcode.FormatEan8 or Barcode.FormatUpcA or Barcode.FormatUpcE => BarcodeKind.Product,
        Barcode.FormatQrCode => BarcodeKind.QrCode,
        _ => BarcodeKind.Other,
    };

    internal static InputImage LoadImage(string imagePath) =>
        // FromFilePath reads the photo's rotation, so text in portrait photos is read the right way up.
        InputImage.FromFilePath(AndroidApp.Context, Android.Net.Uri.FromFile(new Java.IO.File(imagePath))!);
}

/// <summary>Reads printed Latin-script text (English and numbers) from a photo with Google ML Kit, on the phone.</summary>
public sealed class MlKitTextReader : ITextReader
{
    public async Task<string> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        using var image = MlKitBarcodeReader.LoadImage(imagePath);
        using var recognizer = TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);
        try
        {
            var result = await recognizer.Process(image).AsTask().WaitAsync(cancellationToken);
            return result?.JavaCast<Text>()?.GetText() ?? "";
        }
        finally
        {
            recognizer.Close();
        }
    }
}
