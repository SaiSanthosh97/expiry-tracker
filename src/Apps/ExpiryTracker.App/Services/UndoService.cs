namespace ExpiryTracker.App.Services;

/// <summary>Remembers the last "Used up / Thrown away" so the home screen can offer Undo for a few seconds.</summary>
public sealed class UndoService
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private (string ItemId, string Message, DateTime At)? _pending;

    public void Offer(string itemId, string message) => _pending = (itemId, message, DateTime.UtcNow);

    /// <returns>The pending undo, if it is still within the time window.</returns>
    public (string ItemId, string Message)? Take()
    {
        if (_pending is { } p && DateTime.UtcNow - p.At < Window)
        {
            return (p.ItemId, p.Message);
        }

        _pending = null;
        return null;
    }

    public void Clear() => _pending = null;
}
