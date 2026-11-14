using HomeTracker.Core.Medicines;

namespace ExpiryTracker.App.Services;

/// <param name="Medicines">Medicines saved.</param>
/// <param name="Doses">Dose records saved.</param>
/// <param name="FromSync">True when the change came from another family phone (so it isn't sent back out).</param>
public sealed record MedicinesChangedEventArgs(IReadOnlyList<Medicine> Medicines, IReadOnlyList<DoseEvent> Doses, bool FromSync);

/// <summary>Medicines and dose records: adding, marking doses, stock, and changes from family phones.</summary>
public sealed class MedicineService(IMedicineRepository repository, HomeService homes, Clock clock)
{
    /// <summary>Raised after any change, possibly from a background thread.</summary>
    public event EventHandler<MedicinesChangedEventArgs>? Changed;

    public DateTime Now => clock.Now.LocalDateTime;

    public Task<IReadOnlyList<Medicine>> GetAllAsync(bool includeDeleted = false) => repository.GetMedicinesAsync(includeDeleted);

    public async Task<Medicine?> GetAsync(string id) => (await repository.GetMedicinesAsync()).FirstOrDefault(m => m.Id == id);

    /// <summary>Dose records from the last few days (enough for "today" and late-night doses).</summary>
    public Task<IReadOnlyList<DoseEvent>> GetRecentEventsAsync() => repository.GetEventsAsync(Now.Date.AddDays(-3));

    /// <summary>Today's doses for the given medicines, in time order.</summary>
    public async Task<IReadOnlyList<DoseSlot>> GetTodayAsync(IEnumerable<Medicine> medicines) =>
        DoseRules.Today(medicines, await GetRecentEventsAsync(), Now);

    public async Task SaveAsync(Medicine medicine)
    {
        var saved = medicine with { UpdatedAt = clock.Now };
        await repository.SaveMedicineAsync(saved);
        Changed?.Invoke(this, new MedicinesChangedEventArgs([saved], [], false));
    }

    public async Task DeleteAsync(Medicine medicine) => await SaveAsync(medicine with { IsDeleted = true });

    /// <summary>Adds bought tablets to the stock.</summary>
    public Task AddStockAsync(Medicine medicine, decimal amount) =>
        SaveAsync(medicine with { Stock = (medicine.Stock ?? 0) + amount });

    /// <summary>
    /// Records Taken / Skipped / Missed / Later for one dose. Taking a dose lowers the stock once
    /// (marking the same dose taken twice doesn't count twice).
    /// </summary>
    public async Task<DoseEvent> RecordAsync(Medicine medicine, DateTime scheduledAt, DoseStatus status, DateTime? laterUntil = null)
    {
        var id = DoseEvent.IdFor(medicine.Id, scheduledAt);
        var previous = (await repository.GetEventsAsync(scheduledAt)).FirstOrDefault(e => e.Id == id);
        var doseEvent = new DoseEvent(id, medicine.Id, medicine.HomeId, scheduledAt, status, clock.Now, homes.Me.Name, laterUntil);
        await repository.SaveEventAsync(doseEvent);

        Medicine? changed = null;
        if (status == DoseStatus.Taken && previous?.Status != DoseStatus.Taken && medicine.Stock is not null)
        {
            changed = DoseStock.AfterTaken(await GetAsync(medicine.Id) ?? medicine) with { UpdatedAt = clock.Now };
            await repository.SaveMedicineAsync(changed);
        }

        Changed?.Invoke(this, new MedicinesChangedEventArgs(changed is null ? [] : [changed], [doseEvent], false));
        return doseEvent;
    }

    /// <summary>A when-needed medicine taken now.</summary>
    public Task<DoseEvent> TakeNowAsync(Medicine medicine)
    {
        var now = Now;
        return RecordAsync(medicine, new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0), DoseStatus.Taken);
    }

    /// <summary>Saves medicines and dose records from a family phone, keeping whichever copy changed last.</summary>
    /// <returns>What was new or newer here.</returns>
    public async Task<MedicinesChangedEventArgs> ApplyFromFamilyAsync(IReadOnlyList<Medicine> medicines, IReadOnlyList<DoseEvent> doses)
    {
        var localMeds = (await repository.GetMedicinesAsync(includeDeleted: true)).ToDictionary(m => m.Id);
        var newMeds = medicines.Where(m => !localMeds.TryGetValue(m.Id, out var mine) || m.UpdatedAt > mine.UpdatedAt).ToList();
        foreach (var m in newMeds)
        {
            await repository.SaveMedicineAsync(m);
        }

        var from = doses.Count == 0 ? DateTime.MaxValue : doses.Min(d => d.ScheduledAt);
        var localDoses = doses.Count == 0 ? [] : (await repository.GetEventsAsync(from)).ToDictionary(d => d.Id);
        var newDoses = doses.Where(d => !localDoses.TryGetValue(d.Id, out var mine) || d.RecordedAt > mine.RecordedAt).ToList();
        foreach (var d in newDoses)
        {
            await repository.SaveEventAsync(d);
        }

        var result = new MedicinesChangedEventArgs(newMeds, newDoses, true);
        if (newMeds.Count > 0 || newDoses.Count > 0)
        {
            Changed?.Invoke(this, result);
        }

        return result;
    }

    /// <summary>A home's medicines (including recently deleted) and the last few days of doses, for sending to family.</summary>
    public async Task<(IReadOnlyList<Medicine> Medicines, IReadOnlyList<DoseEvent> Doses)> GetForHomeAsync(string homeId)
    {
        var cutoff = clock.Now.AddDays(-60);
        var meds = (await repository.GetMedicinesAsync(includeDeleted: true))
            .Where(m => m.HomeId == homeId && (!m.IsDeleted || m.UpdatedAt > cutoff))
            .ToList();
        var doses = (await GetRecentEventsAsync()).Where(d => d.HomeId == homeId).ToList();
        return (meds, doses);
    }

    public Task PurgeHomeAsync(string homeId) => repository.PurgeHomeAsync(homeId);

    /// <summary>Medicines whose reminders ring on this phone.</summary>
    public async Task<IReadOnlyList<Medicine>> GetMineAsync()
    {
        var me = homes.Me.DeviceId;
        return (await repository.GetMedicinesAsync()).Where(m => m.ReminderDeviceId is null || m.ReminderDeviceId == me).ToList();
    }
}
