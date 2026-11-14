using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using AndroidApp = Android.App.Application;

namespace HomeTracker.Device.Reminders;

/// <summary>A button on a timed notification: <paramref name="Action"/> is passed back to the app when tapped.</summary>
public sealed record NotificationButton(string Action, string Label);

/// <summary>
/// One on-time alarm for the app (e.g. the next medicine dose) and notifications with buttons. The app decides what
/// happens when the alarm fires and when a button is tapped; it calls <see cref="ScheduleAt"/> again for the next time.
/// Uses exact alarms when Android allows them ("Alarms &amp; reminders"), otherwise Android may deliver a few minutes late.
/// </summary>
public static class TimedReminders
{
    internal const string ChannelId = "medicine-doses";
    internal const string ExtraKey = "key";
    internal const string ExtraAction = "action";
    internal const string ExtraPayload = "payload";
    private const int AlarmRequestCode = 2;

    private static Func<Task>? _onAlarm;
    private static Func<string, string, Task>? _onButton;

    private static Context AppContext => AndroidApp.Context;

    /// <summary>
    /// Call at app start-up (it also runs when Android starts the app for an alarm or a button).
    /// <paramref name="onAlarm"/> runs when the alarm fires, after a restart and after a clock change.
    /// <paramref name="onButton"/> gets (payload, action) when a notification button is tapped.
    /// </summary>
    public static void Initialize(string channelName, string channelDescription, Func<Task> onAlarm, Func<string, string, Task> onButton)
    {
        _onAlarm = onAlarm;
        _onButton = onButton;
        var channel = new NotificationChannel(ChannelId, channelName, NotificationImportance.High)
        {
            Description = channelDescription,
            LockscreenVisibility = NotificationVisibility.Public,
        };
        channel.EnableVibration(true);
        ((NotificationManager)AppContext.GetSystemService(Context.NotificationService)!).CreateNotificationChannel(channel);
        RescheduleReceiver.AlsoOnReschedule = () => _ = RunAlarmAsync();
    }

    /// <summary>False when Android needs the user to allow "Alarms &amp; reminders" for alarms to ring on the minute.</summary>
    public static bool CanRingOnTime =>
        !OperatingSystem.IsAndroidVersionAtLeast(31) || ((AlarmManager)AppContext.GetSystemService(Context.AlarmService)!).CanScheduleExactAlarms();

    /// <summary>Opens Android's "Alarms &amp; reminders" switch for this app.</summary>
    public static void OpenOnTimeSettings(Activity activity)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            activity.StartActivity(new Intent(Android.Provider.Settings.ActionRequestScheduleExactAlarm,
                Android.Net.Uri.Parse($"package:{activity.PackageName}")));
        }
    }

    /// <summary>Sets (or replaces) the alarm; null cancels it.</summary>
    public static void ScheduleAt(DateTime? localTime)
    {
        var alarms = (AlarmManager)AppContext.GetSystemService(Context.AlarmService)!;
        var intent = PendingIntent.GetBroadcast(AppContext, AlarmRequestCode, new Intent(AppContext, typeof(TimedAlarmReceiver)),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        if (localTime is not { } at)
        {
            alarms.Cancel(intent);
            return;
        }

        var trigger = new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        if (CanRingOnTime)
        {
            alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, trigger, intent);
        }
        else
        {
            alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, trigger, intent);
        }

        Log.Info("HomeTracker", $"Next medicine alarm at {at:yyyy-MM-dd HH:mm}{(CanRingOnTime ? "" : " (may be late)")}");
    }

    /// <summary>Shows (or updates) a notification. <paramref name="sound"/> false updates it silently.</summary>
    public static void Show(string key, string title, string text, IReadOnlyList<NotificationButton> buttons, string payload, bool sound, int smallIcon)
    {
        var context = AppContext;
        var launch = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!)!;
        launch.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        var open = PendingIntent.GetActivity(context, 0, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(smallIcon)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new Notification.BigTextStyle().BigText(text))
            .SetContentIntent(open)
            .SetCategory(Notification.CategoryAlarm)
            .SetOnlyAlertOnce(!sound)
            .SetAutoCancel(false)
            .SetOngoing(buttons.Count > 0);

        var id = IdFor(key);
        for (var i = 0; i < buttons.Count; i++)
        {
            var intent = new Intent(context, typeof(TimedButtonReceiver))
                .PutExtra(ExtraKey, key)
                .PutExtra(ExtraAction, buttons[i].Action)
                .PutExtra(ExtraPayload, payload);
            var pending = PendingIntent.GetBroadcast(context, id * 8 + i, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
            builder.AddAction(new Notification.Action.Builder((Android.Graphics.Drawables.Icon?)null, buttons[i].Label, pending).Build());
        }

        ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Notify(id, builder.Build());
    }

    public static void Cancel(string key) =>
        ((NotificationManager)AppContext.GetSystemService(Context.NotificationService)!).Cancel(IdFor(key));

    internal static async Task RunAlarmAsync()
    {
        try
        {
            if (_onAlarm is not null)
            {
                await _onAlarm();
            }
        }
        catch (Exception ex)
        {
            Log.Error("HomeTracker", $"Medicine alarm failed: {ex}");
        }
    }

    internal static async Task RunButtonAsync(string payload, string action)
    {
        try
        {
            if (_onButton is not null)
            {
                await _onButton(payload, action);
            }
        }
        catch (Exception ex)
        {
            Log.Error("HomeTracker", $"Medicine button failed: {ex}");
        }
    }

    /// <summary>Notification ids 300000–399999, apart from other notifications of the app.</summary>
    private static int IdFor(string key)
    {
        var hash = 17;
        foreach (var c in key)
        {
            hash = unchecked((hash * 31) + c);
        }

        return 300000 + (int)((uint)hash % 100000);
    }
}

[BroadcastReceiver(Name = "hometracker.reminders.TimedAlarmReceiver", Exported = false)]
public sealed class TimedAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await TimedReminders.RunAlarmAsync();
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}

[BroadcastReceiver(Name = "hometracker.reminders.TimedButtonReceiver", Exported = false)]
public sealed class TimedButtonReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var action = intent?.GetStringExtra(TimedReminders.ExtraAction);
        var payload = intent?.GetStringExtra(TimedReminders.ExtraPayload);
        if (action is null || payload is null)
        {
            return;
        }

        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await TimedReminders.RunButtonAsync(payload, action);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}
