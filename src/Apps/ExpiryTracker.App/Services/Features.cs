namespace ExpiryTracker.App.Services;

/// <summary>Switches for features that are built but not shown to users yet.</summary>
public static class Features
{
    /// <summary>
    /// Medicine reminders (Release 4). Off since 0.7.1: entering a medicine is too much work for a first-time user,
    /// so the tab, alarms and missed-dose alerts stay hidden until adding a medicine is simpler. Data already
    /// entered is kept and still synced.
    /// </summary>
    public static readonly bool Medicines = false;
}
