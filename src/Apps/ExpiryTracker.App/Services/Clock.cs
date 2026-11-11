namespace ExpiryTracker.App.Services;

public sealed class Clock
{
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public DateTimeOffset Now => DateTimeOffset.Now;
}
