using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Device.Reminders;

namespace ExpiryTracker.App.Services;

/// <summary>Everything the "Keep reminders working" section needs: checks, fixes, test, scheduling.</summary>
public sealed class ReminderHealth(AppSettings settings, Clock clock)
{
    public bool NotificationsEnabled => DailyReminders.AreNotificationsEnabled;

    public bool BatteryUnrestricted => DailyReminders.IsBatteryUnrestricted;

    public async Task<bool> RequestNotificationsAsync()
    {
        var status = await Permissions.RequestAsync<Permissions.PostNotifications>();
        return status == PermissionStatus.Granted && NotificationsEnabled;
    }

    public void RequestBatteryUnrestricted() => WithActivity(DailyReminders.RequestBatteryUnrestricted);

    public void OpenAppSettings() => WithActivity(DailyReminders.OpenAppSettings);

    public void SendTestReminder() =>
        DailyReminders.Show(new ReminderContent(AppResources.Test_Title, AppResources.Test_Text, [AppResources.Test_Text], null));

    /// <summary>Schedules the next daily check, and runs today's now if the phone missed it (e.g. it was switched off).</summary>
    public async Task ScheduleAndCatchUpAsync()
    {
        DailyReminders.Schedule();

        var now = clock.Now;
        if (settings.LastReminderRun != clock.Today && TimeOnly.FromDateTime(now.LocalDateTime) >= settings.ReminderTime)
        {
            await DailyReminders.RunCheckNowAsync();
        }
    }

    private static void WithActivity(Action<Android.App.Activity> action)
    {
        if (Platform.CurrentActivity is { } activity)
        {
            action(activity);
        }
    }
}
