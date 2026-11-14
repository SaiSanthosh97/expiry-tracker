using Android.App;
using Android.Content;
using Android.OS;
using AndroidApp = Android.App.Application;

namespace HomeTracker.Device.Reminders;

public static partial class DailyReminders
{
    /// <summary>Other messages ("phone hasn't checked in", refill).</summary>
    internal const string ChannelId = "expiry-reminders";

    /// <summary>The daily expiry reminder: its own channel with an alarm-like tone, so it stands out.</summary>
    internal const string AlertChannelId = "expiry-alerts";
    internal const int NotificationId = 1001;
    internal const string ExtraItemId = "itemId";
    internal const string ExtraAction = "action";
    internal const string ExtraAgain = "again";

    /// <summary>Put on the app's launch intent when the reminder is tapped or shown full screen.</summary>
    public const string ExtraOpen = "hometracker.reminders.open";
    public const string ExtraFullScreen = "hometracker.reminders.fullscreen";

    private const int DailyRequestCode = 1;
    private const int AgainRequestCode = 3;

    private static Context AppContext => AndroidApp.Context;

    static partial void PlatformInitialize() => EnsureChannel();

    /// <summary>Schedules (or replaces) the next daily check at the user's reminder time.</summary>
    public static void Schedule()
    {
        var next = NextOccurrence(DateTime.Now, ReminderTime);
        var triggerAt = new DateTimeOffset(next).ToUnixTimeMilliseconds();

        SetAlarm(triggerAt, DailyIntent());
        Android.Util.Log.Info("HomeTracker", $"Daily reminder scheduled for {next:yyyy-MM-dd HH:mm}");
    }

    /// <summary>Runs today's check right away (used by "Send test reminder" and when the app opens late).</summary>
    public static Task RunCheckNowAsync() => DailyReminderReceiver.RunAsync(AppContext, again: false);

    /// <summary>
    /// Shows today's reminder again at <paramref name="localTime"/> (snooze, or the evening repeat), if it hasn't been
    /// handled by then. Replaces an earlier "again" time.
    /// </summary>
    public static void RemindAgainAt(DateTime localTime)
    {
        var trigger = new DateTimeOffset(DateTime.SpecifyKind(localTime, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        SetAlarm(trigger, AgainIntent());
        Android.Util.Log.Info("HomeTracker", $"Reminder again at {localTime:yyyy-MM-dd HH:mm}");
    }

    /// <summary>Removes today's reminder notification (once it has been handled).</summary>
    public static void ClearReminder() =>
        ((NotificationManager)AppContext.GetSystemService(Context.NotificationService)!).Cancel(NotificationId);

    /// <summary>
    /// False when Android needs the user to allow "full-screen notifications" for this app (Android 14 and later).
    /// Without it the reminder pops up at the top of the screen instead.
    /// </summary>
    public static bool CanShowFullScreen =>
        !OperatingSystem.IsAndroidVersionAtLeast(34)
        || ((NotificationManager)AppContext.GetSystemService(Context.NotificationService)!).CanUseFullScreenIntent();

    /// <summary>Opens Android's "full-screen notifications" switch for this app.</summary>
    public static void OpenFullScreenSettings(Activity activity)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            activity.StartActivity(new Intent(Android.Provider.Settings.ActionManageAppUseFullScreenIntent,
                Android.Net.Uri.Parse($"package:{activity.PackageName}")));
        }
    }

    /// <summary>Shows a notification immediately, regardless of what's due.</summary>
    public static void Show(ReminderContent content) => Post(AppContext, content);

    /// <summary>
    /// Shows a separate notification (e.g. "Appa's phone hasn't checked in") without replacing the daily reminder.
    /// The same <paramref name="key"/> replaces its previous notification.
    /// </summary>
    public static void ShowMessage(string key, string title, string text)
    {
        var options = Options ?? throw new InvalidOperationException("DailyReminders.Initialize was not called.");
        var context = AppContext;
        var launch = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!)!;
        launch.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        var open = PendingIntent.GetActivity(context, 0, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var notification = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(options.SmallIconResourceId)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new Notification.BigTextStyle().BigText(text))
            .SetContentIntent(open)
            .SetAutoCancel(true)
            .Build();

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        // Message notifications use ids from 2000 up, apart from the daily reminder's id.
        manager.Notify(2000 + (int)((uint)StableHash(key) % 100000), notification);
    }

    private static int StableHash(string text)
    {
        var hash = 17;
        foreach (var c in text)
        {
            hash = unchecked((hash * 31) + c);
        }

        return hash;
    }

    public static bool AreNotificationsEnabled =>
        ((NotificationManager)AppContext.GetSystemService(Context.NotificationService)!).AreNotificationsEnabled();

    /// <summary>
    /// False when Android or the phone maker's battery saver (e.g. Samsung "sleeping apps") may delay or block reminders.
    /// </summary>
    public static bool IsBatteryUnrestricted =>
        ((PowerManager)AppContext.GetSystemService(Context.PowerService)!).IsIgnoringBatteryOptimizations(AppContext.PackageName);

    /// <summary>Asks Android to stop battery-optimizing this app (shows a system Allow/Deny dialog).</summary>
    public static void RequestBatteryUnrestricted(Activity activity)
    {
        var intent = new Intent(Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations,
            Android.Net.Uri.Parse($"package:{activity.PackageName}"));
        activity.StartActivity(intent);
    }

    /// <summary>Opens this app's page in Android settings (battery, notifications, permissions).</summary>
    public static void OpenAppSettings(Activity activity)
    {
        var intent = new Intent(Android.Provider.Settings.ActionApplicationDetailsSettings,
            Android.Net.Uri.Parse($"package:{activity.PackageName}"));
        activity.StartActivity(intent);
    }

    /// <summary>
    /// On the minute when "Alarms &amp; reminders" is allowed; otherwise within 10 minutes (Android's shortest window
    /// without that permission). A plain inexact alarm could be up to an hour late.
    /// </summary>
    private static void SetAlarm(long triggerAtMs, PendingIntent intent)
    {
        var alarms = (AlarmManager)AppContext.GetSystemService(Context.AlarmService)!;
        if (TimedReminders.CanRingOnTime)
        {
            alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMs, intent);
        }
        else
        {
            alarms.SetWindow(AlarmType.RtcWakeup, triggerAtMs, 10 * 60 * 1000L, intent);
        }
    }

    private static PendingIntent AgainIntent()
    {
        var intent = new Intent(AppContext, typeof(DailyReminderReceiver)).PutExtra(ExtraAgain, true);
        return PendingIntent.GetBroadcast(AppContext, AgainRequestCode, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    private static PendingIntent DailyIntent()
    {
        var intent = new Intent(AppContext, typeof(DailyReminderReceiver));
        return PendingIntent.GetBroadcast(AppContext, DailyRequestCode, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    private static void EnsureChannel()
    {
        if (Options is null)
        {
            return;
        }

        var channel = new NotificationChannel(ChannelId, Options.ChannelName, NotificationImportance.High)
        {
            Description = Options.ChannelDescription,
        };
        var manager = (NotificationManager)AppContext.GetSystemService(Context.NotificationService)!;
        manager.CreateNotificationChannel(channel);

        // The daily reminder gets an alarm-like tone and a strong vibration, unlike ordinary notifications.
        var alert = new NotificationChannel(AlertChannelId, Options.ChannelName, NotificationImportance.High)
        {
            Description = Options.ChannelDescription,
            LockscreenVisibility = NotificationVisibility.Public,
        };
        var tone = Android.Media.RingtoneManager.GetDefaultUri(Android.Media.RingtoneType.Alarm)
            ?? Android.Media.RingtoneManager.GetDefaultUri(Android.Media.RingtoneType.Notification);
        alert.SetSound(tone, new Android.Media.AudioAttributes.Builder()
            .SetUsage(Android.Media.AudioUsageKind.Notification)!
            .SetContentType(Android.Media.AudioContentType.Sonification)!
            .Build());
        alert.EnableVibration(true);
        alert.SetVibrationPattern([0, 600, 300, 600, 300, 600]);
        alert.EnableLights(true);
        alert.LightColor = Options.AccentColor;
        manager.CreateNotificationChannel(alert);
    }

    internal static void Post(Context context, ReminderContent content)
    {
        var options = Options ?? throw new InvalidOperationException("DailyReminders.Initialize was not called.");

        // Tapping opens the app's "needs your attention" screen.
        var launch = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!)!;
        launch.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        launch.PutExtra(ExtraOpen, true);
        var open = PendingIntent.GetActivity(context, 40, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var style = new Notification.InboxStyle();
        foreach (var line in content.Lines)
        {
            style.AddLine(line);
        }

        var builder = new Notification.Builder(context, AlertChannelId)
            .SetSmallIcon(options.SmallIconResourceId)
            .SetColor(options.AccentColor)
            .SetColorized(true)
            .SetContentTitle(content.Title)
            .SetContentText(content.Text)
            .SetStyle(style)
            .SetContentIntent(open)
            // Stays until handled (an item marked, snoozed, or "Done" on the reminder screen).
            .SetOngoing(true)
            .SetAutoCancel(false)
            .SetCategory(Notification.CategoryReminder)
            .SetVisibility(NotificationVisibility.Public);

        if (content.FullScreen && CanShowFullScreen)
        {
            // Over the lock screen like an alarm clock; while the phone is in use, Android pops it up at the top instead.
            var full = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!)!;
            full.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
            full.PutExtra(ExtraOpen, true).PutExtra(ExtraFullScreen, true);
            builder.SetFullScreenIntent(PendingIntent.GetActivity(context, 41, full, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable), true);
        }

        if (content.SingleItemId is { } itemId)
        {
            builder
                .AddAction(Action(context, itemId, ReminderAction.UsedUp, options.UsedUpLabel, 10))
                .AddAction(Action(context, itemId, ReminderAction.SnoozeHour, options.SnoozeHourLabel, 11))
                .AddAction(Action(context, itemId, ReminderAction.RemindTomorrow, options.RemindTomorrowLabel, 12));
        }
        else
        {
            builder
                .AddAction(Action(context, null, ReminderAction.SnoozeHour, options.SnoozeHourLabel, 13))
                .AddAction(Action(context, null, ReminderAction.RemindTomorrow, options.RemindTomorrowLabel, 14));
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        manager.Notify(NotificationId, builder.Build());
    }

    private static Notification.Action Action(Context context, string? itemId, ReminderAction action, string label, int requestCode)
    {
        var intent = new Intent(context, typeof(ReminderActionReceiver))
            .PutExtra(ExtraAction, action.ToString());
        if (itemId is not null)
        {
            intent.PutExtra(ExtraItemId, itemId);
        }

        var pending = PendingIntent.GetBroadcast(context, requestCode, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        return new Notification.Action.Builder((Android.Graphics.Drawables.Icon?)null, label, pending).Build()!;
    }
}
