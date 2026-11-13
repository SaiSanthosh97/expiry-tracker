using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Core.Households;

namespace ExpiryTracker.App.Services;

/// <summary>
/// The homes this phone belongs to, which one is shown, and this phone's identity. There is always at least
/// one home: a phone that never shared or joined anything has a private "My home".
/// </summary>
public sealed class HomeService(IHomeRepository repository, ItemService items, Clock clock)
{
    private const string CurrentKey = "home.current";
    private const string DeviceIdKey = "device.id";
    private const string DeviceNameKey = "device.name";
    private const string RemindKeyPrefix = "home.remind.";

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Raised when homes are added, removed, renamed or switched.</summary>
    public event EventHandler? Changed;

    public DeviceIdentity Me
    {
        get
        {
            var id = Preferences.Default.Get<string?>(DeviceIdKey, null);
            if (id is null)
            {
                id = DeviceIdentity.NewId();
                Preferences.Default.Set(DeviceIdKey, id);
            }

            return new DeviceIdentity(id, Preferences.Default.Get(DeviceNameKey, DefaultDeviceName()));
        }
    }

    public void SetDeviceName(string name)
    {
        Preferences.Default.Set(DeviceNameKey, name.Trim());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>All homes, oldest first. Creates "My home" (and moves older items into it) on first use.</summary>
    public async Task<IReadOnlyList<Home>> GetHomesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var homes = await repository.GetHomesAsync();
            if (homes.Count == 0)
            {
                var home = Home.CreateLocal(AppResources.Home_DefaultName, clock.Now);
                await repository.SaveHomeAsync(home);
                homes = [home];
            }

            await items.AdoptItemsWithoutHomeAsync(homes[0].Id);
            return homes;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Home> GetCurrentAsync()
    {
        var homes = await GetHomesAsync();
        var id = Preferences.Default.Get<string?>(CurrentKey, null);
        return homes.FirstOrDefault(h => h.Id == id) ?? homes[0];
    }

    public async Task<Home?> GetAsync(string homeId) => (await GetHomesAsync()).FirstOrDefault(h => h.Id == homeId);

    public void SetCurrent(string homeId)
    {
        Preferences.Default.Set(CurrentKey, homeId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<Home> RenameAsync(Home home, string name)
    {
        var renamed = home with { Name = name.Trim() };
        await repository.SaveHomeAsync(renamed);
        Changed?.Invoke(this, EventArgs.Empty);
        return renamed;
    }

    /// <summary>Gives the home a secret key and channel so family phones can join it.</summary>
    public async Task<Home> StartSharingAsync(Home home)
    {
        if (home.IsShared)
        {
            return home;
        }

        var shared = home.StartSharing();
        await repository.SaveHomeAsync(shared);
        Changed?.Invoke(this, EventArgs.Empty);
        return shared;
    }

    /// <summary>Adds a home from an invite (or refreshes it if this phone was already in it) and shows it.</summary>
    public async Task<Home> JoinAsync(Home invited)
    {
        var existing = await GetAsync(invited.Id);
        var home = existing is null ? invited : existing with { Key = invited.Key, Topic = invited.Topic, RelayUrl = invited.RelayUrl };
        await repository.SaveHomeAsync(home);

        // An unused, empty "My home" from first start is replaced by the joined home.
        var homes = await repository.GetHomesAsync();
        foreach (var other in homes.Where(h => h.Id != home.Id && !h.IsShared))
        {
            if ((await items.GetEverythingAsync(other.Id)).Count == 0)
            {
                await repository.DeleteHomeAsync(other.Id);
            }
        }

        SetCurrent(home.Id);
        return home;
    }

    /// <summary>Removes the home and its items from this phone. Other phones keep theirs.</summary>
    public async Task LeaveAsync(Home home)
    {
        await repository.DeleteHomeAsync(home.Id);
        await items.PurgeHomeAsync(home.Id);
        Preferences.Default.Remove(RemindKeyPrefix + home.Id);
        if (Preferences.Default.Get<string?>(CurrentKey, null) == home.Id)
        {
            Preferences.Default.Remove(CurrentKey);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<IReadOnlyList<FamilyDevice>> GetDevicesAsync(string homeId) => repository.GetDevicesAsync(homeId);

    public Task SaveDeviceAsync(FamilyDevice device) => repository.SaveDeviceAsync(device);

    /// <summary>Whether the daily reminder includes this home's items (on by default).</summary>
    public static bool GetRemind(string homeId) => Preferences.Default.Get(RemindKeyPrefix + homeId, true);

    public static void SetRemind(string homeId, bool remind) => Preferences.Default.Set(RemindKeyPrefix + homeId, remind);

    private static string DefaultDeviceName()
    {
        var name = DeviceInfo.Current.Name;
        return string.IsNullOrWhiteSpace(name) ? DeviceInfo.Current.Model : name;
    }
}
