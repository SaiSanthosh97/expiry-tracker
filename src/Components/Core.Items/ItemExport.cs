using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeTracker.Core.Items;

/// <summary>
/// File format for backup and for sharing a list (e.g. over WhatsApp).
/// Merging keeps whichever copy of an item changed last.
/// </summary>
public static class ItemExport
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IEnumerable<TrackedItem> items, DateTimeOffset exportedAt) =>
        JsonSerializer.Serialize(new ExportFile(FormatVersion, exportedAt, items.ToList()), Options);

    /// <exception cref="FormatException">The file is not an export from this app, or from a newer version.</exception>
    public static IReadOnlyList<TrackedItem> Deserialize(string json)
    {
        ExportFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ExportFile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException("This file is not a list exported by this app.", ex);
        }

        if (file is null || file.Version < 1)
        {
            throw new FormatException("This file is not a list exported by this app.");
        }

        if (file.Version > FormatVersion)
        {
            throw new FormatException("This file was made by a newer version of the app. Update the app first.");
        }

        return file.Items;
    }

    /// <summary>Items from <paramref name="incoming"/> that are new or newer than the local copy.</summary>
    public static IReadOnlyList<TrackedItem> ChangesToApply(IEnumerable<TrackedItem> local, IEnumerable<TrackedItem> incoming)
    {
        var existing = local.ToDictionary(i => i.Id);
        return incoming
            .Where(i => !existing.TryGetValue(i.Id, out var current) || i.UpdatedAt > current.UpdatedAt)
            .ToList();
    }

    private sealed record ExportFile(int Version, DateTimeOffset ExportedAt, List<TrackedItem> Items);
}
