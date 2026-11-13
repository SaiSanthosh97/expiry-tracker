using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Core.Items;
using HomeTracker.Device.Reminders;

namespace ExpiryTracker.App.Services;

/// <summary>Builds the daily notification from the item list and handles its buttons.</summary>
public sealed class ReminderSource(ItemService items, AppSettings settings) : IDailyReminderSource
{
    public async Task<ReminderContent?> BuildAsync(DateOnly today)
    {
        settings.LastReminderRun = today;
        HomeTracker.Device.HomeWidget.HomeWidget.Refresh();

        var due = ItemLists.DueForReminder(await items.GetOpenAsync(), today);
        if (due.Count == 0)
        {
            return null;
        }

        var lines = due.Select(e => string.Format(AppResources.Reminder_SingleTitle, e.Item.Name, Text.Status(e))).ToList();

        // A single item already says everything in its title; listing it again would repeat it.
        return due.Count == 1
            ? new ReminderContent(lines[0], AppResources.Reminder_TapToOpen, [], due[0].Item.Id)
            : new ReminderContent(string.Format(AppResources.Reminder_ManyTitle, due.Count), string.Join(", ", due.Select(e => e.Item.Name)), lines, null);
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
