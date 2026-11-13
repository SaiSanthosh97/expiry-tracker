using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Core.Items;
using HomeTracker.Device.Reminders;

namespace ExpiryTracker.App.Services;

/// <summary>Builds the daily notification from the item list and handles its buttons.</summary>
public sealed class ReminderSource(ItemService items, HomeService homes, AppSettings settings) : IDailyReminderSource
{
    public async Task<ReminderContent?> BuildAsync(DateOnly today)
    {
        settings.LastReminderRun = today;
        HomeTracker.Device.HomeWidget.HomeWidget.Refresh();

        var all = await homes.GetHomesAsync();
        var reminded = all.Where(h => HomeService.GetRemind(h.Id)).ToDictionary(h => h.Id, h => h.Name);
        var open = (await items.GetOpenAsync()).Where(e => e.Item.HomeId is { } h && reminded.ContainsKey(h)).ToList();
        var due = ItemLists.DueForReminder(open, today);
        if (due.Count == 0)
        {
            return null;
        }

        // With several homes, say whose item it is: "Amma's house: Eye drops".
        string NameOf(ItemEvaluation e) => all.Count > 1 ? $"{reminded[e.Item.HomeId!]}: {e.Item.Name}" : e.Item.Name;
        var lines = due.Select(e => string.Format(AppResources.Reminder_SingleTitle, NameOf(e), Text.Status(e))).ToList();

        // A single item already says everything in its title; listing it again would repeat it.
        return due.Count == 1
            ? new ReminderContent(lines[0], AppResources.Reminder_TapToOpen, [], due[0].Item.Id)
            : new ReminderContent(string.Format(AppResources.Reminder_ManyTitle, due.Count), string.Join(", ", due.Select(NameOf)), lines, null);
    }

    public async Task HandleActionAsync(string itemId, ReminderAction action)
    {
        _ = action switch
        {
            ReminderAction.UsedUp => await items.CloseAsync(itemId, ClosedReason.UsedUp),
            ReminderAction.ThrownAway => await items.CloseAsync(itemId, ClosedReason.ThrownAway),
            ReminderAction.RemindTomorrow => await items.SnoozeAsync(itemId),
            _ => null,
        };
    }
}
