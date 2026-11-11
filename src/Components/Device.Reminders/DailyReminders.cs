namespace HomeTracker.Device.Reminders;

/// <summary>
/// Entry point for the app. Call <see cref="Initialize"/> at startup (it also runs when Android starts the
/// app in the background for a reminder), then <see cref="Schedule"/> whenever the reminder time changes.
/// </summary>
public static partial class DailyReminders
{
    private static Func<IDailyReminderSource>? _sourceFactory;
    private static Func<TimeOnly>? _timeProvider;

    internal static ReminderOptions? Options { get; private set; }

    /// <param name="timeProvider">Returns the user's chosen reminder time (read again at every reschedule).</param>
    public static void Initialize(Func<IDailyReminderSource> sourceFactory, Func<TimeOnly> timeProvider, ReminderOptions options)
    {
        _sourceFactory = sourceFactory;
        _timeProvider = timeProvider;
        Options = options;
        PlatformInitialize();
    }

    internal static IDailyReminderSource Source =>
        _sourceFactory?.Invoke() ?? throw new InvalidOperationException("DailyReminders.Initialize was not called.");

    internal static TimeOnly ReminderTime => _timeProvider?.Invoke() ?? new TimeOnly(9, 0);

    /// <summary>Next time the daily check should run: today at <paramref name="time"/> if still ahead, otherwise tomorrow.</summary>
    public static DateTime NextOccurrence(DateTime now, TimeOnly time)
    {
        var today = DateOnly.FromDateTime(now).ToDateTime(time);
        return today > now ? today : today.AddDays(1);
    }

    static partial void PlatformInitialize();
}
