using System.Globalization;
using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Core.Medicines;
using HomeTracker.Device.Reminders;

namespace ExpiryTracker.App.Services;

/// <summary>
/// Rings for the medicines whose reminders are on this phone: at the dose time, again after 10 and 30 minutes,
/// after "Later", and marks a dose missed after an hour (family phones then get an alert through family sync).
/// Runs when the alarm fires, when a button is tapped, when medicines change and when the app opens.
/// </summary>
public sealed class DoseEngine
{
    private const string TakenAction = "taken";
    private const string LaterAction = "later";
    private const string SkipAction = "skip";
    private static readonly TimeSpan AlertAgainAfter = TimeSpan.FromMinutes(9);

    private readonly MedicineService _medicines;
    private readonly SemaphoreSlim _running = new(1, 1);

    public DoseEngine(MedicineService medicines)
    {
        _medicines = medicines;
        _medicines.Changed += (_, _) => _ = RunAsync();
    }

    public void Initialize() => TimedReminders.Initialize(
        AppResources.Dose_Channel,
        AppResources.Dose_ChannelDescription,
        RunAsync,
        OnButtonAsync);

    /// <summary>Brings notifications up to date, records missed doses and sets the next alarm.</summary>
    public async Task RunAsync()
    {
        if (!Features.Medicines)
        {
            // Feature hidden: no alarms (cancels any set by 0.7.0).
            TimedReminders.ScheduleAt(null);
            return;
        }

        await _running.WaitAsync();
        try
        {
            var now = _medicines.Now;
            var mine = await _medicines.GetMineAsync();
            var events = await _medicines.GetRecentEventsAsync();
            foreach (var slot in DoseRules.Today(mine, events, now))
            {
                await HandleAsync(slot, now);
            }

            foreach (var medicine in mine)
            {
                AlertRefillIfNeeded(medicine);
            }

            TimedReminders.ScheduleAt(DoseRules.NextWake(mine, await _medicines.GetRecentEventsAsync(), now));
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("HomeTracker", $"Dose engine failed: {ex}");
        }
        finally
        {
            _running.Release();
        }
    }

    private async Task HandleAsync(DoseSlot slot, DateTime now)
    {
        switch (slot.State)
        {
            case DoseState.Due:
                var alertKey = "dose.alerted." + slot.Id;
                var last = Preferences.Default.Get(alertKey, 0L);
                var laterRunning = slot.Event is { Status: DoseStatus.Later, LaterUntil: { } until } && now < until;
                if (laterRunning)
                {
                    TimedReminders.Cancel(slot.Id);
                    break;
                }

                var sound = last == 0 || now - new DateTime(last) >= AlertAgainAfter;
                TimedReminders.Show(
                    slot.Id,
                    string.Format(AppResources.Dose_Title, slot.Medicine.Person, slot.Medicine.Name),
                    string.Format(AppResources.Dose_Text, MedicineText.Amount(slot.Medicine), slot.ScheduledAt.ToString("t", CultureInfo.CurrentCulture)),
                    [
                        new NotificationButton(TakenAction, AppResources.Dose_Taken),
                        new NotificationButton(LaterAction, AppResources.Dose_Later),
                        new NotificationButton(SkipAction, AppResources.Dose_Skip),
                    ],
                    slot.Medicine.Id + "|" + slot.ScheduledAt.Ticks.ToString(CultureInfo.InvariantCulture),
                    sound,
                    Resource.Drawable.notification_icon);
                if (sound)
                {
                    Preferences.Default.Set(alertKey, now.Ticks);
                }

                break;

            case DoseState.Missed when slot.Event is null or { Status: DoseStatus.Later }:
                // Nobody marked it within the hour: record it, so the family is told.
                await _medicines.RecordAsync(slot.Medicine, slot.ScheduledAt, DoseStatus.Missed);
                TimedReminders.Show(
                    slot.Id,
                    string.Format(AppResources.Dose_MissedTitle, slot.Medicine.Person, slot.Medicine.Name),
                    string.Format(AppResources.Dose_MissedText, slot.ScheduledAt.ToString("t", CultureInfo.CurrentCulture)),
                    [],
                    "",
                    sound: false,
                    Resource.Drawable.notification_icon);
                break;

            case DoseState.Taken or DoseState.Skipped:
                TimedReminders.Cancel(slot.Id);
                break;
        }
    }

    private async Task OnButtonAsync(string payload, string action)
    {
        var parts = payload.Split('|');
        if (parts.Length != 2 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || await _medicines.GetAsync(parts[0]) is not { } medicine)
        {
            return;
        }

        var scheduledAt = new DateTime(ticks);
        switch (action)
        {
            case TakenAction:
                await _medicines.RecordAsync(medicine, scheduledAt, DoseStatus.Taken);
                break;
            case SkipAction:
                await _medicines.RecordAsync(medicine, scheduledAt, DoseStatus.Skipped);
                break;
            case LaterAction:
                await _medicines.RecordAsync(medicine, scheduledAt, DoseStatus.Later, _medicines.Now + DoseRules.LaterBy);
                break;
        }

        TimedReminders.Cancel(DoseEvent.IdFor(medicine.Id, scheduledAt));
        // RecordAsync raises Changed, which runs the engine again and sets the next alarm.
    }

    /// <summary>"Buy more" once per stock level, on the phone that rings for the medicine.</summary>
    private static void AlertRefillIfNeeded(Medicine medicine)
    {
        var key = "dose.refill." + medicine.Id;
        if (!DoseStock.NeedsRefill(medicine))
        {
            Preferences.Default.Remove(key);
            return;
        }

        var level = medicine.Stock?.ToString(CultureInfo.InvariantCulture) ?? "";
        if (Preferences.Default.Get(key, "") == level || Preferences.Default.ContainsKey(key))
        {
            return; // Already told for this low stock; told again once it has been refilled.
        }

        Preferences.Default.Set(key, level);
        DailyReminders.ShowMessage(
            "refill/" + medicine.Id,
            string.Format(AppResources.Refill_Title, medicine.Name),
            MedicineText.Refill(medicine));
    }
}
