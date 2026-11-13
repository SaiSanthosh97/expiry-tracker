using HomeTracker.Core.Items;
using HomeTracker.Device.Vision;

namespace ExpiryTracker.App.Services;

/// <summary>
/// Item photos, kept in the app's private storage (not in the phone's gallery). Items store only the file
/// name, so the folder can move. Each photo is shrunk to about 150 KB.
/// </summary>
public sealed class PhotoStore(IItemRepository repository)
{
    private static string Folder => Path.Combine(FileSystem.AppDataDirectory, "photos");

    /// <returns>The full path, or null when there's no photo or the file is missing (e.g. a list imported from another phone).</returns>
    public static string? PathOf(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName != Path.GetFileName(fileName))
        {
            return null;
        }

        var path = Path.Combine(Folder, fileName);
        return File.Exists(path) ? path : null;
    }

    public static ImageSource? ImageOf(string? fileName) => PathOf(fileName) is { } path ? ImageSource.FromFile(path) : null;

    /// <summary>Saves a smaller copy of a photo.</summary>
    /// <returns>The new file name, or null if the photo couldn't be read.</returns>
    public static Task<string?> KeepAsync(string sourcePath) => Task.Run(() =>
    {
        Directory.CreateDirectory(Folder);
        var name = $"{Guid.NewGuid():N}.jpg";
        return PhotoShrinker.SaveAsJpeg(sourcePath, Path.Combine(Folder, name)) ? name : null;
    });

    public static void Delete(string? fileName)
    {
        if (PathOf(fileName) is { } path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Removed by the next clean-up.
            }
        }
    }

    /// <summary>
    /// Deletes photos no item uses any more: items that were deleted, and photos taken on the Add screen
    /// that was then left without saving. Recent files are kept in case that screen is still open.
    /// </summary>
    public async Task CleanUpAsync()
    {
        if (!Directory.Exists(Folder))
        {
            return;
        }

        var used = (await repository.GetEverythingAsync())
            .Where(i => !i.IsDeleted && i.PhotoPath is not null)
            .Select(i => i.PhotoPath!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            if (!used.Contains(Path.GetFileName(file)) && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-1))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Try again next time.
                }
            }
        }
    }
}
