using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Util;
using Android.Widget;
using AndroidApp = Android.App.Application;

namespace HomeTracker.Device.HomeWidget;

/// <summary>
/// A home-screen widget listing a few rows (title, coloured detail, one action button) plus an Add button.
/// Call <see cref="Initialize"/> at app start-up and <see cref="Refresh"/> whenever the data changes.
/// Tapping a row or Add opens the app's launcher activity; read the request with <see cref="TakeOpenRequest"/>.
/// </summary>
public static class HomeWidget
{
    public const int MaxRows = 3;

    internal const string ActionRowButton = "hometracker.homewidget.ROW_ACTION";
    internal const string ExtraRowId = "hometracker.homewidget.rowId";
    private const string ExtraOpen = "hometracker.homewidget.open";
    private const string OpenAdd = "add";

    private static Func<IHomeWidgetSource>? _sourceFactory;
    private static HomeWidgetOptions _options = new("Done", "+ Add");

    private static readonly (int Row, int Dot, int Title, int Detail, int Button)[] RowViews =
    [
        (Resource.Id.hometracker_widget_row1, Resource.Id.hometracker_widget_dot1, Resource.Id.hometracker_widget_title1, Resource.Id.hometracker_widget_detail1, Resource.Id.hometracker_widget_button1),
        (Resource.Id.hometracker_widget_row2, Resource.Id.hometracker_widget_dot2, Resource.Id.hometracker_widget_title2, Resource.Id.hometracker_widget_detail2, Resource.Id.hometracker_widget_button2),
        (Resource.Id.hometracker_widget_row3, Resource.Id.hometracker_widget_dot3, Resource.Id.hometracker_widget_title3, Resource.Id.hometracker_widget_detail3, Resource.Id.hometracker_widget_button3),
    ];

    private static Context AppContext => AndroidApp.Context;

    internal static IHomeWidgetSource Source =>
        _sourceFactory?.Invoke() ?? throw new InvalidOperationException("HomeWidget.Initialize was not called.");

    public static void Initialize(Func<IHomeWidgetSource> sourceFactory, HomeWidgetOptions options)
    {
        _sourceFactory = sourceFactory;
        _options = options;
    }

    /// <summary>Redraws every widget the user has placed. Safe to call often; does nothing when none are placed.</summary>
    public static void Refresh() => _ = RefreshAsync();

    internal static async Task RefreshAsync()
    {
        try
        {
            var manager = AppWidgetManager.GetInstance(AppContext)!;
            var ids = manager.GetAppWidgetIds(new ComponentName(AppContext, Java.Lang.Class.FromType(typeof(HomeWidgetProvider))));
            if (ids is null || ids.Length == 0)
            {
                return;
            }

            var content = await Source.BuildAsync(DateOnly.FromDateTime(DateTime.Now));
            manager.UpdateAppWidget(ids, Build(content));
        }
        catch (Exception ex)
        {
            Log.Error("HomeTracker", $"Widget refresh failed: {ex}");
        }
    }

    /// <summary>
    /// Returns what was tapped when the app was opened from the widget, and clears it so it is handled once.
    /// Call from the activity's OnCreate and OnNewIntent.
    /// </summary>
    public static HomeWidgetOpenRequest? TakeOpenRequest(Intent? intent)
    {
        var value = intent?.GetStringExtra(ExtraOpen);
        if (value is null)
        {
            return null;
        }

        intent!.RemoveExtra(ExtraOpen);
        return new HomeWidgetOpenRequest(value == OpenAdd ? null : value);
    }

    private static RemoteViews Build(HomeWidgetContent content)
    {
        var views = new RemoteViews(AppContext.PackageName, Resource.Layout.hometracker_widget);
        views.SetTextViewText(Resource.Id.hometracker_widget_header, content.Title);
        views.SetTextViewText(Resource.Id.hometracker_widget_add, _options.AddLabel);
        views.SetOnClickPendingIntent(Resource.Id.hometracker_widget_header, OpenApp(0, null));
        views.SetOnClickPendingIntent(Resource.Id.hometracker_widget_add, OpenApp(1, OpenAdd));

        views.SetTextViewText(Resource.Id.hometracker_widget_empty, content.EmptyText);
        views.SetViewVisibility(Resource.Id.hometracker_widget_empty, content.Rows.Count == 0 ? Android.Views.ViewStates.Visible : Android.Views.ViewStates.Gone);

        for (var i = 0; i < RowViews.Length; i++)
        {
            var ids = RowViews[i];
            if (i >= content.Rows.Count)
            {
                views.SetViewVisibility(ids.Row, Android.Views.ViewStates.Gone);
                continue;
            }

            var row = content.Rows[i];
            views.SetViewVisibility(ids.Row, Android.Views.ViewStates.Visible);
            views.SetTextViewText(ids.Title, row.Title);
            views.SetTextViewText(ids.Detail, row.Detail);
            views.SetTextColor(ids.Detail, new Android.Graphics.Color(row.Argb));
            views.SetTextColor(ids.Dot, new Android.Graphics.Color(row.Argb));
            views.SetTextViewText(ids.Button, _options.ActionLabel);
            views.SetOnClickPendingIntent(ids.Row, OpenApp(10 + i, row.Id));
            views.SetOnClickPendingIntent(ids.Button, RowAction(20 + i, row.Id));
        }

        return views;
    }

    private static PendingIntent OpenApp(int requestCode, string? open)
    {
        var intent = AppContext.PackageManager!.GetLaunchIntentForPackage(AppContext.PackageName!)!;
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        if (open is not null)
        {
            intent.PutExtra(ExtraOpen, open);
        }

        return PendingIntent.GetActivity(AppContext, requestCode, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    private static PendingIntent RowAction(int requestCode, string rowId)
    {
        var intent = new Intent(AppContext, typeof(HomeWidgetActionReceiver)).SetAction(ActionRowButton).PutExtra(ExtraRowId, rowId);
        return PendingIntent.GetBroadcast(AppContext, requestCode, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }
}
