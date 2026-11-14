using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Core.Items;
using HomeTracker.Device.Reminders;

namespace ExpiryTracker.App.Services;

/// <summary>
/// Builds the daily expiry reminder, keeps it until it is handled (an item marked, snoozed, or "Done"), repeats it in
/// the evening if nothing was done, and handles its buttons and the "Needs your attention" screen.
/// </summary>
public sealed class ReminderSource(ItemService items, HomeService homes, AppSettings settings, Clock clock) : IDailyReminderSource
{
    private const string HandledKey = "reminder.handled-day";
    private static readonly TimeOnly EveningAt = new(19, 0);

    /// <summary>True once today's reminder has been dealt with; the evening repeat then stays quiet.</summary>
    public bool HandledToday => Preferences.Default.Get(HandledKey, -1) == clock.Today.DayNumber;

    /// <summary>"This evening" is offered only while it is still ahead.</summary>
    public bool EveningAhead => TimeOnly.FromDateTime(clock.Now.LocalDateTime) < EveningAt.AddMinutes(-30);

    /// <summary>Items that need a reminder today, in every home this phone reminds about.</summary>
    public async Task<IReadOnlyList<ItemEvaluation>> GetDueAsync()
    {
        var all = await homes.GetHomesAsync();
        var reminded = all.Where(h => HomeService.GetRemind(h.Id)).Select(h => h.Id).ToHashSet();
        var open = (await items.GetOpenAsync()).Where(e => e.Item.HomeId is { } h && reminded.Contains(h)).ToList();
        return ItemLists.DueForReminder(open, clock.Today);
    }

    public async Task<ReminderContent?> BuildAsync(DateOnly today, bool again)
    {
        if (!again)
        {
            settings.LastReminderRun = today;
            Preferences.Default.Remove(HandledKey);
            HomeTracker.Device.HomeWidget.HomeWidget.Refresh();
        }
        else if (HandledToday)
        {
            return null;
        }

        var due = await GetDueAsync();
        if (due.Count == 0)
        {
            return null;
        }

        // Not handled by the evening: remind once more.
        if (EveningAhead)
        {
            DailyReminders.RemindAgainAt(today.ToDateTime(EveningAt));
        }

        // With several homes, say whose item it is: "Amma's house: Eye drops".
        var all = await homes.GetHomesAsync();
        var homeNames = all.ToDictionary(h => h.Id, h => h.Name);
        string NameOf(ItemEvaluation e) => all.Count > 1 && homeNames.TryGetValue(e.Item.HomeId ?? "", out var h) ? $"{h}: {e.Item.Name}" : e.Item.Name;
        var lines = due.Select(e => string.Format(AppResources.Reminder_SingleTitle, NameOf(e), Text.Status(e))).ToList();

        var title = due.Count == 1 ? lines[0] : string.Format(AppResources.Reminder_ManyTitle, due.Count);
        if (again)
        {
            title = string.Format(AppResources.Reminder_Again, title);
        }

        if (settings.ReadAloud)
        {
            _ = SpeakAsync(due.Select(e => $"{e.Item.Name}: {Text.Status(e)}."));
        }

        // A single item already says everything in its title; listing it again would repeat it.
        return due.Count == 1
            ? new ReminderContent(title, AppResources.Reminder_TapToOpen, [], due[0].Item.Id, settings.FullScreenReminders)
            : new ReminderContent(title, string.Join(", ", due.Select(NameOf)), lines, null, settings.FullScreenReminders);
    }

    public async Task HandleActionAsync(string? itemId, ReminderAction action)
    {
        switch (action)
        {
            case ReminderAction.UsedUp when itemId is not null:
                await items.CloseAsync(itemId, ClosedReason.UsedUp);
                MarkHandled();
                break;
            case ReminderAction.ThrownAway when itemId is not null:
                await items.CloseAsync(itemId, ClosedReason.ThrownAway);
                MarkHandled();
                break;
            case ReminderAction.RemindTomorrow:
                await RemindTomorrowAsync(itemId);
                break;
            case ReminderAction.SnoozeHour:
                Snooze(clock.Now.LocalDateTime.AddHours(1));
                break;
        }
    }

    /// <summary>Snooze the whole reminder until <paramref name="localTime"/>.</summary>
    public static void Snooze(DateTime localTime)
    {
        DailyReminders.ClearReminder();
        DailyReminders.RemindAgainAt(localTime);
    }

    public void SnoozeUntilEvening() => Snooze(clock.Today.ToDateTime(EveningAt));

    /// <summary>Don't remind about these items (all due ones when <paramref name="itemId"/> is null) until tomorrow.</summary>
    public async Task RemindTomorrowAsync(string? itemId = null)
    {
        var ids = itemId is not null ? [itemId] : (await GetDueAsync()).Select(e => e.Item.Id).ToList();
        foreach (var id in ids)
        {
            await items.SnoozeAsync(id);
        }

        MarkHandled();
    }

    /// <summary>Today's reminder is dealt with: remove it and skip the evening repeat.</summary>
    public void MarkHandled()
    {
        Preferences.Default.Set(HandledKey, clock.Today.DayNumber);
        DailyReminders.ClearReminder();
    }

    private static async Task SpeakAsync(IEnumerable<string> sentences)
    {
        try
        {
            await TextToSpeech.Default.SpeakAsync(string.Join(" ", sentences));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Android.Util.Log.Warn("HomeTracker", $"Reading the reminder aloud failed: {ex.Message}");
        }
    }
}
