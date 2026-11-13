using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HomeTracker.Core.ShelfLife;

namespace HomeTracker.Core.DateExtraction;

/// <summary>
/// Reads expiry information from label text (typically OCR output), in the formats used on
/// Indian packs: "EXP 05/2027", "USE BY 12.03.26", "MFD 01/2026 BEST BEFORE 12 MONTHS FROM MFG",
/// "MFG EXP / 01/26 12/27", "EXP MAY 2027".
/// Day-first (DD/MM/YYYY) is assumed. Results are conservative: when several rules apply,
/// the earliest last-usable day wins.
/// </summary>
public sealed partial class LabelDateExtractor
{
    private const int MaxGapBetweenLabelAndDate = 3;
    private const int YearWindow = 20;

    private readonly Func<DateOnly> _today;

    /// <param name="today">Clock used to reject implausible years. Defaults to the local date.</param>
    public LabelDateExtractor(Func<DateOnly>? today = null)
    {
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
    }

    public LabelDateResult Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LabelDateResult.Empty;
        }

        var normalized = Normalize(text);

        var (bestBefore, afterOpening, masked) = ExtractRelativeShelfLife(normalized);
        var needLabel = new HashSet<int>();
        var dates = AssignRoles(masked, FindDates(masked, needLabel))
            .Where(d => d.Role != DateRole.Unknown || !needLabel.Contains(d.Index))
            .ToList();

        return Combine(dates, bestBefore, afterOpening);
    }

    private static string Normalize(string text)
    {
        var upper = text.ToUpperInvariant()
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'');

        // Format hints like "(MM/YY)" or "DD.MM.YYYY" sit between a label and its date; blank them out.
        upper = FormatHintRegex().Replace(upper, m => new string(' ', m.Length));

        // Common OCR mix-ups inside numeric dates: O→0, I/L→1 ("05/2O27").
        upper = NumericRunRegex().Replace(upper, m =>
        {
            var run = m.Value;
            if (run.Count(char.IsAsciiDigit) < 2)
            {
                return run;
            }

            return run.Replace('O', '0').Replace('I', '1').Replace('L', '1');
        });

        // Labels and dates are often printed or read without a space ("EXP05/27", "EX.DT.12/26").
        return LetterDigitJoinRegex().Replace(upper, " ");
    }

    private static (ShelfLifeDuration? BestBefore, ShelfLifeDuration? AfterOpening, string Masked) ExtractRelativeShelfLife(string text)
    {
        ShelfLifeDuration? bestBefore = null;
        ShelfLifeDuration? afterOpening = null;
        var masked = new StringBuilder(text);

        foreach (var regex in new[] { RelativeWithLabelRegex(), RelativeFromEventRegex() })
        {
            foreach (Match match in regex.Matches(masked.ToString()))
            {
                if (ToDuration(match.Groups["n"].Value, match.Groups["unit"].Value) is not { } duration)
                {
                    continue;
                }

                if (match.Groups["from"].Value.StartsWith("OPEN", StringComparison.Ordinal))
                {
                    afterOpening = Earliest(afterOpening, duration);
                }
                else
                {
                    bestBefore = Earliest(bestBefore, duration);
                }

                // Hide the phrase so its words ("BEST BEFORE", "MFG") aren't read as labels for nearby dates.
                for (var i = match.Index; i < match.Index + match.Length; i++)
                {
                    masked[i] = ' ';
                }
            }
        }

        return (bestBefore, afterOpening, masked.ToString());
    }

    private static ShelfLifeDuration? ToDuration(string number, string unit)
    {
        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount < 1)
        {
            return null;
        }

        return unit[0] switch
        {
            'D' => ShelfLifeDuration.Days(amount),
            'W' => ShelfLifeDuration.Weeks(amount),
            'M' => ShelfLifeDuration.Months(amount),
            'Y' => ShelfLifeDuration.Months(amount * 12),
            _ => null,
        };
    }

    private static ShelfLifeDuration Earliest(ShelfLifeDuration? current, ShelfLifeDuration candidate) =>
        current is { } existing && existing.ApproximateDays <= candidate.ApproximateDays ? existing : candidate;

    /// <summary>
    /// Finds dates. Month/year written with a dot or a space ("05.27", "05 2027") is also how prices
    /// and weights look, so those are returned in <paramref name="needLabel"/> and kept only when a label
    /// such as EXP or MFG sits right before them.
    /// </summary>
    private List<LabelDate> FindDates(string text, HashSet<int> needLabel)
    {
        var today = _today();
        var dates = new List<LabelDate>();

        foreach (Match match in DateRegex().Matches(text))
        {
            if (IsPrice(text, match.Index))
            {
                continue;
            }

            var date = ParseDate(match, today);
            if (date is not null)
            {
                dates.Add(date);
                if (date.Precision == DatePrecision.Month && !match.Groups["mon"].Success && match.Groups["sep"].Value is not ("/" or "-"))
                {
                    needLabel.Add(date.Index);
                }
            }
        }

        return dates;
    }

    private static bool IsPrice(string text, int index)
    {
        var start = Math.Max(0, index - 8);
        return PriceMarkerRegex().IsMatch(text.AsSpan(start, index - start).ToString());
    }

    private static LabelDate? ParseDate(Match match, DateOnly today)
    {
        var groups = match.Groups;
        if (!TryParseYear(groups["y"].Value, today, out var year))
        {
            return null;
        }

        int month;
        if (groups["mon"].Success)
        {
            month = MonthFromName(groups["mon"].Value);
        }
        else if (!int.TryParse(groups["m"].Value, CultureInfo.InvariantCulture, out month))
        {
            return null;
        }

        if (!groups["d"].Success)
        {
            return month is >= 1 and <= 12
                ? new LabelDate(DateRole.Unknown, new DateOnly(year, month, 1), DatePrecision.Month, match.Index, match.Value)
                : null;
        }

        var day = int.Parse(groups["d"].Value, CultureInfo.InvariantCulture);
        var swapped = false;
        if (month > 12 && day <= 12 && !groups["mon"].Success)
        {
            (day, month) = (month, day);
            swapped = true;
        }

        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        return new LabelDate(DateRole.Unknown, new DateOnly(year, month, day), DatePrecision.Day, match.Index, match.Value, swapped);
    }

    private static bool TryParseYear(string value, DateOnly today, out int year)
    {
        year = 0;
        if (!int.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        year = value.Length == 2 ? 2000 + parsed : parsed;
        return Math.Abs(year - today.Year) <= YearWindow;
    }

    private static int MonthFromName(string name) => name[..3] switch
    {
        "JAN" => 1,
        "FEB" => 2,
        "MAR" => 3,
        "APR" => 4,
        "MAY" => 5,
        "JUN" => 6,
        "JUL" => 7,
        "AUG" => 8,
        "SEP" => 9,
        "OCT" => 10,
        "NOV" => 11,
        "DEC" => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown month."),
    };

    /// <summary>
    /// Links each date to the label just before it. A run of labels with nothing between them
    /// ("MFG EXP" table header) is handed out in order to the dates that follow.
    /// </summary>
    private static List<LabelDate> AssignRoles(string text, List<LabelDate> dates)
    {
        var groups = FindLabelGroups(text);
        var result = new List<LabelDate>(dates.Count);
        LabelGroup? active = null;
        var cursor = 0;

        foreach (var date in dates)
        {
            var latest = groups.LastOrDefault(g => g.End <= date.Index);
            if (latest is not null && !ReferenceEquals(latest, active) && latest.End >= cursor)
            {
                active = latest;
                cursor = latest.End;
            }

            var role = DateRole.Unknown;
            if (active is not null && active.Pending.Count > 0 && IsShortGap(text, cursor, date.Index))
            {
                role = active.Pending.Dequeue();
            }

            result.Add(date with { Role = role });
            cursor = date.Index + date.Text.Length;
        }

        return result;
    }

    private static bool IsShortGap(string text, int from, int to)
    {
        var meaningful = 0;
        for (var i = from; i < to; i++)
        {
            if (char.IsLetterOrDigit(text[i]))
            {
                meaningful++;
            }
        }

        return meaningful <= MaxGapBetweenLabelAndDate;
    }

    private static List<LabelGroup> FindLabelGroups(string text)
    {
        var groups = new List<LabelGroup>();
        foreach (Match match in LabelRegex().Matches(text))
        {
            var role = match.Groups["exp"].Success ? DateRole.Expiry : DateRole.Manufacture;
            var previous = groups.Count > 0 ? groups[^1] : null;

            if (previous is not null && OnlySeparators(text, previous.End, match.Index))
            {
                previous.Pending.Enqueue(role);
                previous.End = match.Index + match.Length;
            }
            else
            {
                var group = new LabelGroup { End = match.Index + match.Length };
                group.Pending.Enqueue(role);
                groups.Add(group);
            }
        }

        return groups;
    }

    private static bool OnlySeparators(string text, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            if (char.IsLetterOrDigit(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static LabelDateResult Combine(List<LabelDate> dates, ShelfLifeDuration? bestBefore, ShelfLifeDuration? afterOpening)
    {
        var expiry = dates.Where(d => d.Role == DateRole.Expiry).MinBy(d => d.LastDay);
        var manufacture = dates.Where(d => d.Role == DateRole.Manufacture).MaxBy(d => d.Date);
        var unknown = dates.Where(d => d.Role == DateRole.Unknown).OrderBy(d => d.Date).ToList();
        var confidence = ExtractionConfidence.None;

        // Unlabelled dates: with two different dates, the earlier is manufacture and the later is expiry.
        if (expiry is null && manufacture is null && unknown.Count > 0)
        {
            confidence = ExtractionConfidence.Low;
            if (bestBefore is not null && unknown.Count == 1)
            {
                manufacture = unknown[0] with { Role = DateRole.Manufacture };
            }
            else
            {
                expiry = unknown[^1] with { Role = DateRole.Expiry };
                if (unknown.Count > 1 && unknown[0].Date < expiry.Date)
                {
                    manufacture = unknown[0] with { Role = DateRole.Manufacture };
                }
            }
        }

        var candidates = new List<(DateOnly Day, ExtractionConfidence Confidence)>(2);

        if (expiry is not null)
        {
            var labelled = confidence == ExtractionConfidence.None;
            candidates.Add((expiry.LastDay, !labelled ? ExtractionConfidence.Low
                : expiry.IsDayMonthSwapped ? ExtractionConfidence.Medium
                : ExtractionConfidence.High));
        }

        if (manufacture is not null && bestBefore is { } duration)
        {
            // Month-only manufacture dates count from the 1st, the earliest possible day.
            candidates.Add((duration.LastUsableDay(manufacture.Date),
                confidence == ExtractionConfidence.Low ? ExtractionConfidence.Low : ExtractionConfidence.Medium));
        }

        var earliest = candidates.Count > 0 ? candidates.MinBy(c => c.Day) : default;

        return new LabelDateResult
        {
            ExpiryLastUsableDay = candidates.Count > 0 ? earliest.Day : null,
            Confidence = candidates.Count > 0 ? earliest.Confidence : ExtractionConfidence.None,
            Expiry = expiry,
            Manufacture = manufacture,
            BestBeforeFromManufacture = bestBefore,
            AfterOpening = afterOpening,
            AllDates = dates,
        };
    }

    private sealed class LabelGroup
    {
        public int End { get; set; }

        public Queue<DateRole> Pending { get; } = new();
    }

    private const string MonthNames =
        "JAN(?:UARY)?|FEB(?:RUARY)?|MAR(?:CH)?|APR(?:IL)?|MAY|JUNE?|JULY?|AUG(?:UST)?|SEP(?:T(?:EMBER)?)?|OCT(?:OBER)?|NOV(?:EMBER)?|DEC(?:EMBER)?";

    private const string DurationUnit = @"(?<unit>MONTHS?\b|MTHS?\b|M\b|DAYS?\b|D\b|WEEKS?\b|WKS?\b|YEARS?\b|YRS?\b)";

    private const string FromEvent =
        @"(?:FROM|OF|AFTER)\s*(?:THE\s*)?(?:DATE\s*OF\s*)?(?<from>MANUFACTUR(?:E|ING)|MFG|MFD|MFR|PACKAGING|PACKING|PACKED|PKD|PKG|PACKAGE|OPENING|OPENED|OPEN)\b\.?(?:\s*DATE\b)?";

    [GeneratedRegex(@"\(?\b(?:DD|MM|MMM|YY|YYYY)(?:\s*[\/\.\-]\s*(?:DD|MM|MMM|YY|YYYY))+\b\)?")]
    private static partial Regex FormatHintRegex();

    [GeneratedRegex(@"[0-9OIL][0-9OIL\/\.\-]*[0-9OIL]")]
    private static partial Regex NumericRunRegex();

    [GeneratedRegex(@"(?:BEST\s*BEFORE|USE\s*BEFORE|USE\s*WITHIN|CONSUME\s*WITHIN|BEST\s*WITHIN|SHELF\s*LIFE|EXPIRES?\s*IN)[\s:\-]*(?:OF\s*)?(?<n>\d{1,3})\s*" + DurationUnit + @"(?:\s*" + FromEvent + ")?")]
    private static partial Regex RelativeWithLabelRegex();

    [GeneratedRegex(@"\b(?<n>\d{1,3})\s*" + DurationUnit + @"\s*" + FromEvent)]
    private static partial Regex RelativeFromEventRegex();

    [GeneratedRegex(
        @"(?<exp>\b(?:EXP(?:IRY|IRES|IRATION|Y)?|EX|E\.D|ED)\b\.?(?:\s*(?:DATE|DT|ON)\b\.?)?|\bUSE\s*BY\b|\bUSE\s*BEFORE\b|\bBEST\s*BEFORE(?:\s*END)?\b|\bBB[DE]?\b)" +
        @"|(?<mfg>\b(?:MFG|MFD|MFR|MNF|MANF|MANUFACTURED|MANUFACTURING|MANUFACTURE|PKD|PACKED|PKG|PACKAGED|PACKING|DOM|D\.O\.M)\b\.?(?:\s*(?:DATE|DT|ON)\b\.?)?)")]
    private static partial Regex LabelRegex();

    [GeneratedRegex(
        @"\b(?<y>\d{4})[\-\/\.](?<m>\d{1,2})[\-\/\.](?<d>\d{1,2})\b" +
        @"|\b(?<d>\d{1,2})\s*(?<sep>[\/\.\-])\s*(?<m>\d{1,2})\s*\k<sep>\s*(?<y>\d{4}|\d{2})\b" +
        @"|\b(?<d>\d{1,2})\s*[\/\.\-\s]?\s*(?<mon>" + MonthNames + @")(?![A-Z])\.?\s*[\/\.\-\s,']?\s*(?<y>\d{4}|\d{2})\b" +
        @"|\b(?<mon>" + MonthNames + @")(?![A-Z])\.?\s*[\/\.\-\s,']?\s*(?<y>\d{4}|\d{2})\b" +
        @"|\b(?<m>\d{1,2})\s*(?<sep>[\/\.\-])\s*(?<y>\d{4}|\d{2})\b" +
        @"|\b(?<m>\d{1,2})(?<sep>\s+)(?<y>20\d{2})\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"(?<=[A-Z])(?=\d)|(?<=\d)(?=[A-Z])")]
    private static partial Regex LetterDigitJoinRegex();

    [GeneratedRegex(@"(?:RS|INR|MRP|PRICE|₹)\W*$")]
    private static partial Regex PriceMarkerRegex();
}
