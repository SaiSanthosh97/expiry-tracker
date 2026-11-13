namespace ExpiryTracker.App.Services;

/// <summary>
/// Remembers the last "Used up / Thrown away" so the home screen can offer Undo. Kept in the app's settings,
/// so an action from the widget can still be undone when the app is opened a little later.
/// </summary>
public sealed class UndoService
{
    /// <summary>How long Undo stays on screen once shown, and how long an action in the app can be undone.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    /// <summary>How long an action taken on the home-screen widget can be undone from the app.</summary>
    public static readonly TimeSpan WidgetWindow = TimeSpan.FromMinutes(5);

    private const string IdKey = "undo.itemId";
    private const string MessageKey = "undo.message";
    private const string UntilKey = "undo.untilUtcTicks";

    public void Offer(string itemId, string message) => Offer(itemId, message, Window);

    public void Offer(string itemId, string message, TimeSpan window)
    {
        Preferences.Default.Set(IdKey, itemId);
        Preferences.Default.Set(MessageKey, message);
        Preferences.Default.Set(UntilKey, (DateTime.UtcNow + window).Ticks);
    }

    /// <returns>The pending undo, if it is still within its time window.</returns>
    public (string ItemId, string Message)? Take()
    {
        var id = Preferences.Default.Get<string?>(IdKey, null);
        var message = Preferences.Default.Get<string?>(MessageKey, null);
        var until = Preferences.Default.Get(UntilKey, 0L);
        if (id is not null && message is not null && DateTime.UtcNow.Ticks < until)
        {
            return (id, message);
        }

        Clear();
        return null;
    }

    public void Clear()
    {
        Preferences.Default.Remove(IdKey);
        Preferences.Default.Remove(MessageKey);
        Preferences.Default.Remove(UntilKey);
    }
}
