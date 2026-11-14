namespace HomeTracker.Device.Reminders;

public enum ReminderAction
{
    UsedUp,
    ThrownAway,

    /// <summary>Don't remind about this item (or, without an item, any item in the reminder) until tomorrow.</summary>
    RemindTomorrow,

    /// <summary>Show the whole reminder again in an hour.</summary>
    SnoozeHour,
}

/// <summary>What the daily notification shows.</summary>
/// <param name="Lines">One line per item, shown when the notification is expanded.</param>
/// <param name="SingleItemId">Set when exactly one item is due, so the notification can offer action buttons for it.</param>
/// <param name="FullScreen">Also show it full screen (over the lock screen) when Android allows it.</param>
public sealed record ReminderContent(string Title, string Text, IReadOnlyList<string> Lines, string? SingleItemId, bool FullScreen = false);

/// <summary>
/// Implemented by the app: decides what is due today and handles taps on the notification buttons.
/// Called from background broadcasts, possibly while the app's screens are closed.
/// </summary>
public interface IDailyReminderSource
{
    /// <param name="again">True for a snoozed or evening repeat of today's reminder (return null if already handled).</param>
    /// <returns>Null when nothing needs a reminder.</returns>
    Task<ReminderContent?> BuildAsync(DateOnly today, bool again);

    /// <param name="itemId">The item, or null for a button that applies to the whole reminder.</param>
    Task HandleActionAsync(string? itemId, ReminderAction action);
}

/// <param name="ChannelName">Name of the notification category shown in Android settings.</param>
/// <param name="SmallIconResourceId">App drawable used as the status bar icon.</param>
/// <param name="AccentColor">Colour of the notification's icon and buttons (0xAARRGGBB), so it stands out.</param>
public sealed record ReminderOptions(
    string ChannelName,
    string ChannelDescription,
    int SmallIconResourceId,
    string UsedUpLabel,
    string ThrownAwayLabel,
    string RemindTomorrowLabel,
    string SnoozeHourLabel = "1 hour",
    int AccentColor = unchecked((int)0xFF0E4F51));
