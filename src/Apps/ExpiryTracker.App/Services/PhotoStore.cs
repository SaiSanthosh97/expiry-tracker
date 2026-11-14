using HomeTracker.Core.Items;
using HomeTracker.Device.Vision;

namespace ExpiryTracker.App.Services;

/// <summary>
/// Item photos, kept in the app's private storage (not in the phone's gallery). Items store only the file
/// name, so the folder can move. Each photo is shrunk to about 100 KB. Photos are shared with family
/// phones through family sync and included in the phone's Android backup.
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

    /// <summary>Photo file names are always 32 hex characters + ".jpg"; anything else from another phone is refused.</summary>
    public static bool IsValidName(string? fileName) =>
        fileName is { Length: 36 } && fileName.EndsWith(".jpg", StringComparison.Ordinal)
        && fileName[..32].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool Exists(string? fileName) => PathOf(fileName) is not null;

    public static byte[]? ReadBytes(string fileName) => PathOf(fileName) is { } path ? File.ReadAllBytes(path) : null;

    /// <summary>Saves a photo received from a family phone under its original name.</summary>
    public static async Task SaveReceivedAsync(string fileName, byte[] bytes)
    {
        if (!IsValidName(fileName))
        {
            return;
        }

        Directory.CreateDirectory(Folder);
        var target = Path.Combine(Folder, fileName);
        var temp = target + ".part";
        await File.WriteAllBytesAsync(temp, bytes);
        File.Move(temp, target, overwrite: true);
    }

    public static ImageSource? ImageOf(string? fileName) => PathOf(fileName) is { } path ? ImageSource.FromFile(path) : null;

    /// <summary>Saves a smaller copy of a photo.</summary>
    /// <returns>The new file name, or null if the photo couldn't be read.</returns>
    public static Task<string?> KeepAsync(string sourcePath) => Task.Run(() =>
    {
        Directory.CreateDirectory(Folder);
        var name = $"{Guid.NewGuid():N}.jpg";
        // About 80–120 KB each, so hundreds fit in Android's 25 MB app backup.
        return PhotoShrinker.SaveAsJpeg(sourcePath, Path.Combine(Folder, name), maxSide: 1024, quality: 75) ? name : null;
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
    /// Deletes photos no longer needed: of deleted items, of items marked used up or thrown away (once
    /// Undo is no longer offered), and photos taken on the Add screen that was left without saving.
    /// Recent files are kept in case that screen is still open.
    /// </summary>
    public async Task CleanUpAsync()
    {
        if (!Directory.Exists(Folder))
        {
            return;
        }

        var undoEnded = DateTimeOffset.Now - UndoService.WidgetWindow;
        var used = (await repository.GetEverythingAsync())
            .Where(i => !i.IsDeleted && i.PhotoPath is not null && (!i.IsClosed || i.UpdatedAt > undoEnded))
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
