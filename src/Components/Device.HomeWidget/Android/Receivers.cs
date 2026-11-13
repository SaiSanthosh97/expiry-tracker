using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Util;
using Android.Widget;

namespace HomeTracker.Device.HomeWidget;

/// <summary>The widget itself, as Android sees it. Redraws when Android asks (placed, resized, every few hours).</summary>
[BroadcastReceiver(Name = "hometracker.homewidget.HomeWidgetProvider", Exported = true, Label = "@string/hometracker_widget_label")]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/hometracker_widget_info")]
public sealed class HomeWidgetProvider : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await HomeWidget.RefreshAsync();
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}

/// <summary>
/// Handles the button on a widget row. Kept separate from the provider and not exported, so other apps
/// cannot trigger it (the provider must be exported for Android to send it updates).
/// </summary>
[BroadcastReceiver(Name = "hometracker.homewidget.HomeWidgetActionReceiver", Exported = false)]
public sealed class HomeWidgetActionReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var rowId = intent?.GetStringExtra(HomeWidget.ExtraRowId);
        if (context is null || intent?.Action != HomeWidget.ActionRowButton || rowId is null)
        {
            return;
        }

        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                var message = await HomeWidget.Source.HandleActionAsync(rowId);
                if (message is not null)
                {
                    new Handler(Looper.MainLooper!).Post(() => Toast.MakeText(context, message, ToastLength.Long)!.Show());
                }

                await HomeWidget.RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Error("HomeTracker", $"Widget action failed: {ex}");
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}
