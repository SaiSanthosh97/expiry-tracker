using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Core.Items;
using HomeTracker.Core.LabelCapture;
using HomeTracker.Core.ProductCatalog;

namespace ExpiryTracker.App.Services;

/// <summary>Gets a photo from the user and reads it: barcode → product, or printed dates.</summary>
public sealed class ScanService(LabelReader reader, ProductLookup lookup, Dialogs dialogs)
{
    /// <summary>Asks "Take a photo / Choose a photo" and returns a private copy of the photo, or null if cancelled.</summary>
    public async Task<string?> GetPhotoAsync(string title)
    {
        var choice = await dialogs.ChooseAsync(title, AppResources.Scan_TakePhoto, AppResources.Scan_ChoosePhoto);
        if (choice is null)
        {
            return null;
        }

        FileResult? photo;
        if (choice == AppResources.Scan_TakePhoto)
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await dialogs.AlertAsync(title, AppResources.Scan_CameraUnavailable);
                return null;
            }

            photo = await MediaPicker.Default.CapturePhotoAsync();
        }
        else
        {
            photo = (await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 1 })).FirstOrDefault();
        }

        if (photo is null)
        {
            return null;
        }

        // Copy to our own cache file: the picker's file may be temporary or behind a content URI.
        var path = Path.Combine(FileSystem.CacheDirectory, $"scan-{Guid.NewGuid():N}{Path.GetExtension(photo.FileName)}");
        await using (var source = await photo.OpenReadAsync())
        await using (var target = File.Create(path))
        {
            await source.CopyToAsync(target);
        }

        return path;
    }

    public Task<string?> ReadBarcodeAsync(string photoPath) => reader.ReadProductBarcodeAsync(photoPath);

    public Task<LabelReading> ReadDatesAsync(string photoPath) => reader.ReadDatesAsync(photoPath);

    public Task<ProductLookupResult> LookupAsync(string barcode) => lookup.FindAsync(barcode);

    /// <summary>Photos are only needed while reading; nothing is kept.</summary>
    public static void Discard(string photoPath)
    {
        try
        {
            File.Delete(photoPath);
        }
        catch (IOException)
        {
            // Cache files are cleaned up by Android anyway.
        }
    }
}

/// <summary>Products this household has scanned before, so they're named instantly and offline.</summary>
public sealed class HouseholdProductProvider(IItemRepository repository) : IProductInfoProvider
{
    public async Task<ProductInfo?> FindAsync(string barcode, CancellationToken cancellationToken = default)
    {
        var item = (await repository.GetEverythingAsync())
            .Where(i => i.Barcode == barcode && !i.IsDeleted)
            .MaxBy(i => i.UpdatedAt);
        return item is null ? null : new ProductInfo(barcode, item.Name, null, item.CategoryId, ProductSource.Household);
    }
}
