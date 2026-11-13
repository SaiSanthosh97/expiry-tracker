using System.Globalization;
using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Core.ExpiryEngine;
using HomeTracker.Core.Items;
using HomeTracker.Core.ShelfLife;

namespace ExpiryTracker.App.Services;

/// <summary>Turns item data into the short, plain sentences shown on screen and in notifications.</summary>
public static class Text
{
    private const int ShowDateAfterDays = 60;

    public static string Get(string key) => AppResources.ResourceManager.GetString(key, AppResources.Culture) ?? key;

    public static string Category(string categoryId) => Get("Category_" + categoryId.Replace('-', '_'));

    /// <summary>"just now", "5 min ago", "3 hours ago", "2 days ago".</summary>
    public static string Ago(TimeSpan elapsed) => elapsed switch
    {
        { TotalMinutes: < 2 } => AppResources.Ago_JustNow,
        { TotalHours: < 1 } => string.Format(AppResources.Ago_Minutes, (int)elapsed.TotalMinutes),
        { TotalHours: < 2 } => AppResources.Ago_OneHour,
        { TotalDays: < 1 } => string.Format(AppResources.Ago_Hours, (int)elapsed.TotalHours),
        { TotalDays: < 2 } => AppResources.Ago_OneDay,
        _ => string.Format(AppResources.Ago_Days, (int)elapsed.TotalDays),
    };

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    public static string Status(ItemEvaluation evaluation)
    {
        var days = evaluation.Status.DaysLeft;
        return evaluation.Status.State switch
        {
            ExpiryState.Expired when days == -1 => AppResources.Status_ExpiredYesterday,
            ExpiryState.Expired => string.Format(AppResources.Status_ExpiredDaysAgo, -days),
            ExpiryState.UseSoon when days == 0 => AppResources.Status_LastDayToday,
            ExpiryState.UseSoon when days == 1 => AppResources.Status_UseByTomorrow,
            ExpiryState.UseSoon => string.Format(AppResources.Status_UseWithinDays, days + 1),
            ExpiryState.Fresh when days > ShowDateAfterDays && evaluation.Expiry.LastUsableDay is { } last =>
                string.Format(AppResources.Status_GoodTill, Date(last)),
            ExpiryState.Fresh => string.Format(AppResources.Status_DaysLeft, days + 1),
            _ => AppResources.Status_NoDate,
        };
    }

    public static Color StatusColor(ExpiryState state) => state switch
    {
        ExpiryState.Expired => Palette.Expired,
        ExpiryState.UseSoon => Palette.UseSoon,
        ExpiryState.Fresh => Palette.Fresh,
        _ => Palette.NoDate,
    };

    public static string Duration(ShelfLifeDuration duration) => (duration.Unit, duration.Amount) switch
    {
        (ShelfLifeUnit.Days, 1) => AppResources.Duration_OneDay,
        (ShelfLifeUnit.Days, var n) => string.Format(AppResources.Duration_Days, n),
        (ShelfLifeUnit.Weeks, 1) => AppResources.Duration_OneWeek,
        (ShelfLifeUnit.Weeks, var n) => string.Format(AppResources.Duration_Weeks, n),
        (ShelfLifeUnit.Months, 1) => AppResources.Duration_OneMonth,
        (_, var n) => string.Format(AppResources.Duration_Months, n),
    };
}
