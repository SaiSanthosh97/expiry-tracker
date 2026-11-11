namespace ExpiryTracker.App.Services;

/// <summary>Small per-device settings, stored with MAUI Preferences.</summary>
public sealed class AppSettings
{
    private const string ReminderMinutesKey = "reminderMinutes";
    private const string LastReminderRunKey = "lastReminderRun";
    private const string LastUpdateCheckKey = "lastUpdateCheck";
    private const string WelcomeShownKey = "welcomeShown";

    public TimeOnly ReminderTime
    {
        get => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Preferences.Default.Get(ReminderMinutesKey, 9 * 60)));
        set => Preferences.Default.Set(ReminderMinutesKey, (int)value.ToTimeSpan().TotalMinutes);
    }

    /// <summary>Day the daily reminder check last ran, so a missed alarm can be caught up when the app opens.</summary>
    public DateOnly? LastReminderRun
    {
        get => Preferences.Default.Get(LastReminderRunKey, -1) is var n and >= 0 ? DateOnly.FromDayNumber(n) : null;
        set => Preferences.Default.Set(LastReminderRunKey, value?.DayNumber ?? -1);
    }

    public DateTimeOffset? LastUpdateCheck
    {
        get => Preferences.Default.Get(LastUpdateCheckKey, 0L) is var ms and > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
        set => Preferences.Default.Set(LastUpdateCheckKey, value?.ToUnixTimeMilliseconds() ?? 0L);
    }

    public bool WelcomeShown
    {
        get => Preferences.Default.Get(WelcomeShownKey, false);
        set => Preferences.Default.Set(WelcomeShownKey, value);
    }
}
