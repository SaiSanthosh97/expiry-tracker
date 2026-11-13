using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Core.ExpiryEngine;
using HomeTracker.Core.Items;
using HomeTracker.Device.HomeWidget;

namespace ExpiryTracker.App.Services;

/// <summary>Fills the home-screen widget: the items closest to their last day, each with a "Used" button.</summary>
public sealed class WidgetSource(ItemService items, HomeService homes, UndoService undo) : IHomeWidgetSource
{
    public async Task<HomeWidgetContent> BuildAsync(DateOnly today)
    {
        var home = await homes.GetCurrentAsync();
        var sections = ItemLists.ForHome(await items.GetOpenAsync(home.Id), null);
        var attention = sections.Expired.Count + sections.UseSoon.Count;

        var rows = sections.Expired.Concat(sections.UseSoon).Concat(sections.Fresh)
            .Take(HomeWidget.MaxRows)
            .Select(e => new HomeWidgetRow(e.Item.Id, e.Item.Name, Text.Status(e), Text.StatusColor(e.Status.State).ToInt()))
            .ToList();

        var title = attention switch
        {
            0 when rows.Count == 0 => AppResources.AppName,
            0 => AppResources.Widget_AllGood,
            1 => AppResources.Widget_OneNeedsAttention,
            _ => string.Format(AppResources.Widget_ManyNeedAttention, attention),
        };

        return new HomeWidgetContent(title, rows, AppResources.Widget_Empty);
    }

    public async Task<string?> HandleActionAsync(string rowId)
    {
        if (await items.CloseAsync(rowId, ClosedReason.UsedUp) is not { } item)
        {
            return null;
        }

        // If the app is opened within a few minutes, its Undo bar offers to bring the item back.
        undo.Offer(item.Id, string.Format(AppResources.Undo_UsedUp, item.Name), UndoService.WidgetWindow);
        return string.Format(AppResources.Widget_UsedToast, item.Name);
    }
}

/// <summary>Opens the screen the user tapped on the widget, once the app is ready.</summary>
public static class WidgetNavigation
{
    private static HomeWidgetOpenRequest? _pending;

    public static void Request(HomeWidgetOpenRequest request)
    {
        _pending = request;
        if (Shell.Current is not null)
        {
            MainThread.BeginInvokeOnMainThread(() => _ = GoAsync());
        }
    }

    /// <summary>Called when the home screen appears, for requests that arrived before the app was ready.</summary>
    public static async Task GoAsync()
    {
        if (_pending is not { } request || Shell.Current is null)
        {
            return;
        }

        _pending = null;
        await Shell.Current.Navigation.PopToRootAsync(false);
        await Shell.Current.GoToAsync(request.IsAdd ? "add" : $"item?id={Uri.EscapeDataString(request.RowId!)}");
    }
}
