namespace HomeTracker.Core.Medicines;

public interface IMedicineRepository
{
    /// <summary>All medicines, including deleted ones when <paramref name="includeDeleted"/> is true (for syncing).</summary>
    Task<IReadOnlyList<Medicine>> GetMedicinesAsync(bool includeDeleted = false);

    /// <summary>Insert or replace.</summary>
    Task SaveMedicineAsync(Medicine medicine);

    /// <summary>Dose records for doses scheduled on or after <paramref name="from"/>.</summary>
    Task<IReadOnlyList<DoseEvent>> GetEventsAsync(DateTime from);

    /// <summary>Insert or replace (one record per dose).</summary>
    Task SaveEventAsync(DoseEvent doseEvent);

    /// <summary>Removes a home's medicines and dose records from this phone.</summary>
    Task PurgeHomeAsync(string homeId);
}
