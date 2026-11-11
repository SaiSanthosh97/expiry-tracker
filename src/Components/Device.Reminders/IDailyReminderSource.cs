namespace HomeTracker.Device.Reminders;

public enum ReminderAction
{
    UsedUp,
    ThrownAway,
    RemindTomorrow,
}

/// <summary>What the daily notification shows.</summary>
/// <param name="Lines">One line per item, shown when the notification is expanded.</param>
/// <param name="SingleItemId">Set when exactly one item is due, so the notification can offer action buttons for it.</param>
public sealed record ReminderContent(string Title, string Text, IReadOnlyList<string> Lines, string? SingleItemId);

/// <summary>
/// Implemented by the app: decides what is due today and handles taps on the notification buttons.
/// Called from background broadcasts, possibly while the app's screens are closed.
/// </summary>
public interface IDailyReminderSource
{
    /// <returns>Null when nothing needs a reminder today.</returns>
    Task<ReminderContent?> BuildAsync(DateOnly today);

    Task HandleActionAsync(string itemId, ReminderAction action);
}

/// <param name="ChannelName">Name of the notification category shown in Android settings.</param>
/// <param name="SmallIconResourceId">App drawable used as the status bar icon.</param>
public sealed record ReminderOptions(
    string ChannelName,
    string ChannelDescription,
    int SmallIconResourceId,
    string UsedUpLabel,
    string ThrownAwayLabel,
    string RemindTomorrowLabel);
