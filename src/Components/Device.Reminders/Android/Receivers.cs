using Android.App;
using Android.Content;
using Android.Util;

namespace HomeTracker.Device.Reminders;

/// <summary>Fires once a day: posts today's notification (if anything is due) and schedules tomorrow.</summary>
[BroadcastReceiver(Name = "hometracker.reminders.DailyReminderReceiver", Exported = false)]
public sealed class DailyReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await RunAsync(context);
            }
            finally
            {
                DailyReminders.Schedule();
                pending?.Finish();
            }
        });
    }

    internal static async Task RunAsync(Context context)
    {
        try
        {
            var content = await DailyReminders.Source.BuildAsync(DateOnly.FromDateTime(DateTime.Now));
            if (content is not null)
            {
                DailyReminders.Post(context, content);
            }
        }
        catch (Exception ex)
        {
            Log.Error("HomeTracker", $"Daily reminder failed: {ex}");
        }
    }
}

/// <summary>Handles the Used up / Thrown / Remind tomorrow buttons on the notification.</summary>
[BroadcastReceiver(Name = "hometracker.reminders.ReminderActionReceiver", Exported = false)]
public sealed class ReminderActionReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var itemId = intent?.GetStringExtra(DailyReminders.ExtraItemId);
        if (context is null || itemId is null || !Enum.TryParse<ReminderAction>(intent?.GetStringExtra(DailyReminders.ExtraAction), out var action))
        {
            return;
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        manager.Cancel(DailyReminders.NotificationId);

        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await DailyReminders.Source.HandleActionAsync(itemId, action);
            }
            catch (Exception ex)
            {
                Log.Error("HomeTracker", $"Reminder action failed: {ex}");
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}

/// <summary>Restores the daily alarm after a restart, an app update or a clock/time zone change (Android clears alarms then).</summary>
[BroadcastReceiver(Name = "hometracker.reminders.RescheduleReceiver", Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced, Intent.ActionTimeChanged, Intent.ActionTimezoneChanged])]
public sealed class RescheduleReceiver : BroadcastReceiver
{
    /// <summary>Also run after a restart, app update or clock change (e.g. to set the next medicine alarm again).</summary>
    internal static Action? AlsoOnReschedule { get; set; }

    public override void OnReceive(Context? context, Intent? intent)
    {
        // Exported only so the system can deliver these protected broadcasts; other apps cannot send them.
        if (intent?.Action is Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced or Intent.ActionTimeChanged or Intent.ActionTimezoneChanged)
        {
            Log.Info("HomeTracker", $"Rescheduling daily reminder after {intent.Action}");
            DailyReminders.Schedule();
            AlsoOnReschedule?.Invoke();
        }
    }
}
