using System.Globalization;
using System.Text.RegularExpressions;

namespace HomeTracker.Core.LabelCapture;

/// <summary>
/// Suggests a product name from the text on a pack: the brand or product name is usually the
/// largest text. Lines that are clearly dates, prices, weights, batch numbers or small print are skipped.
/// The result is only a suggestion for the user to check.
/// </summary>
public static partial class ProductNameGuesser
{
    private const int MinLetters = 3;
    private const int MaxLength = 40;

    public static string? Guess(IReadOnlyList<TextLine> lines)
    {
        var candidates = lines.Where(IsCandidate).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var main = candidates.MaxBy(l => l.Height)!;

        // A second line of nearly the same size right under the first is part of the name ("AMUL" / "BUTTER").
        var next = candidates
            .Where(l => l != main
                && l.Top > main.Top
                && l.Top - (main.Top + main.Height) < main.Height * 0.8f
                && l.Height >= main.Height * 0.8f
                && Overlaps(l, main))
            .MinBy(l => l.Top);

        var name = next is null ? Clean(main.Text) : $"{Clean(main.Text)} {Clean(next.Text)}";
        return name.Length > MaxLength ? name[..MaxLength].TrimEnd() : name;
    }

    private static bool IsCandidate(TextLine line)
    {
        var text = line.Text.Trim();
        if (text.Length < MinLetters || text.Length > MaxLength * 2 || line.Height <= 0)
        {
            return false;
        }

        var letters = text.Count(char.IsLetter);
        var digits = text.Count(char.IsDigit);
        return letters >= MinLetters && digits * 3 <= letters && !SmallPrintRegex().IsMatch(text);
    }

    private static bool Overlaps(TextLine a, TextLine b) =>
        a.Left < b.Left + b.Width && b.Left < a.Left + a.Width;

    /// <summary>Removes stray symbols and turns ALL CAPS into Title Case ("NUTELLA" → "Nutella").</summary>
    private static string Clean(string text)
    {
        var cleaned = StrayCharsRegex().Replace(text, " ");
        cleaned = SpacesRegex().Replace(cleaned, " ").Trim();
        if (cleaned.Any(char.IsLetter) && !cleaned.Any(char.IsLower))
        {
            cleaned = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
        }

        return cleaned;
    }

    [GeneratedRegex(
        @"\b(?:EXP|EXPIRY|MFG|MFD|MFR|PKD|PACKED|BATCH|B\.?\s?NO|LOT|MRP|PRICE|RS|INR|NET|WT|WEIGHT|QTY|INGREDIENTS?|NUTRITION(?:AL)?|ENERGY|PROTEIN|FAT|CARBOHYDRATES?|SUGARS?|SODIUM|FSSAI|LIC|LICENSE|LICENCE|BEST|BEFORE|USE|STORE|KEEP|CONTAINS|MANUFACTURED|MARKETED|DISTRIBUTED|CUSTOMER|CARE|EMAIL|WWW|HTTP|COMPOSITION|DOSAGE|SCHEDULE|CALORIES|KCAL|SERVING|PER|ALLERGEN|WARNING|CAUTION)\b|@|\.COM\b|\d+\s*(?:G|GM|KG|ML|L|MG)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SmallPrintRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}&'\- ]")]
    private static partial Regex StrayCharsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
