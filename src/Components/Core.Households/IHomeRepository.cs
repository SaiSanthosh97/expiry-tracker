namespace HomeTracker.Core.Households;

/// <summary>Stores the homes this phone belongs to and the other phones seen in each.</summary>
public interface IHomeRepository
{
    Task<IReadOnlyList<Home>> GetHomesAsync();

    /// <summary>Insert or replace.</summary>
    Task SaveHomeAsync(Home home);

    /// <summary>Removes the home and its list of phones.</summary>
    Task DeleteHomeAsync(string homeId);

    Task<IReadOnlyList<FamilyDevice>> GetDevicesAsync(string homeId);

    /// <summary>Insert or replace.</summary>
    Task SaveDeviceAsync(FamilyDevice device);
}
