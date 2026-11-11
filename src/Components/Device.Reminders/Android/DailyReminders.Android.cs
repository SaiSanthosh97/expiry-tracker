using Android.App;
using Android.Content;
using Android.OS;
using AndroidApp = Android.App.Application;

namespace HomeTracker.Device.Reminders;

public static partial class DailyReminders
{
    internal const string ChannelId = "expiry-reminders";
    internal const int NotificationId = 1001;
    internal const string ExtraItemId = "itemId";
    internal const string ExtraAction = "action";

    private const int DailyRequestCode = 1;

    private static Context AppContext => AndroidApp.Context;

    static partial void PlatformInitialize() => EnsureChannel();

    /// <summary>Schedules (or replaces) the next daily check at the user's reminder time.</summary>
    public static void Schedule()
    {
        var next = NextOccurrence(DateTime.Now, ReminderTime);
        var triggerAt = new DateTimeOffset(next).ToUnixTimeMilliseconds();

        var alarms = (AlarmManager)AppContext.GetSystemService(Context.AlarmService)!;

        // Day-level reminders don't need exact alarms; this works in battery-saving (Doze) mode without extra permission.
        alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, DailyIntent());
        Android.Util.Log.Info("HomeTracker", $"Daily reminder scheduled for {next:yyyy-MM-dd HH:mm}");
    }

    /// <summary>Runs today's check right away (used by "Send test reminder" and when the app opens late).</summary>
    public static Task RunCheckNowAsync() => DailyReminderReceiver.RunAsync(AppContext);

    /// <summary>Shows a notification immediately, regardless of what's due.</summary>
    public static void Show(ReminderContent content) => Post(AppContext, content);

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
    }

    internal static void Post(Context context, ReminderContent content)
    {
        var options = Options ?? throw new InvalidOperationException("DailyReminders.Initialize was not called.");

        var launch = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!)!;
        launch.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        var open = PendingIntent.GetActivity(context, 0, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var style = new Notification.InboxStyle();
        foreach (var line in content.Lines)
        {
            style.AddLine(line);
        }

        var builder = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(options.SmallIconResourceId)
            .SetContentTitle(content.Title)
            .SetContentText(content.Text)
            .SetStyle(style)
            .SetContentIntent(open)
            .SetAutoCancel(true)
            .SetCategory(Notification.CategoryReminder);

        if (content.SingleItemId is { } itemId)
        {
            builder
                .AddAction(Action(context, itemId, ReminderAction.UsedUp, options.UsedUpLabel, 10))
                .AddAction(Action(context, itemId, ReminderAction.ThrownAway, options.ThrownAwayLabel, 11))
                .AddAction(Action(context, itemId, ReminderAction.RemindTomorrow, options.RemindTomorrowLabel, 12));
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        manager.Notify(NotificationId, builder.Build());
    }

    private static Notification.Action Action(Context context, string itemId, ReminderAction action, string label, int requestCode)
    {
        var intent = new Intent(context, typeof(ReminderActionReceiver))
            .PutExtra(ExtraItemId, itemId)
            .PutExtra(ExtraAction, action.ToString());
        var pending = PendingIntent.GetBroadcast(context, requestCode, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        return new Notification.Action.Builder((Android.Graphics.Drawables.Icon?)null, label, pending).Build()!;
    }
}
