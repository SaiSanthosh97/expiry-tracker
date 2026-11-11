using HomeTracker.Core.AppUpdater;

namespace ExpiryTracker.App.Services;

/// <summary>Checks GitHub Releases for a newer APK. Automatic checks run at most once a day and only on Wi-Fi.</summary>
public sealed class UpdateService(HttpClient http, AppSettings settings, Clock clock)
{
    public const string Owner = "SaiSanthosh97";
    public const string Repository = "expiry-tracker";
    public static readonly Uri RepositoryUrl = new($"https://github.com/{Owner}/{Repository}");

    private readonly GitHubReleaseChecker _checker = new(http, Owner, Repository, ".apk");

    public static Version CurrentVersion => AppInfo.Current.Version;

    /// <exception cref="HttpRequestException">No connection.</exception>
    public async Task<AvailableUpdate?> CheckNowAsync()
    {
        var update = await _checker.CheckAsync(CurrentVersion);
        settings.LastUpdateCheck = clock.Now;
        return update;
    }

    /// <returns>An update, or null when not due, not on Wi-Fi, offline or up to date.</returns>
    public async Task<AvailableUpdate?> CheckIfDueAsync()
    {
        var onWifi = Connectivity.Current.NetworkAccess == NetworkAccess.Internet
            && Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.WiFi);
        if (!onWifi || settings.LastUpdateCheck is { } last && clock.Now - last < TimeSpan.FromDays(1))
        {
            return null;
        }

        try
        {
            return await CheckNowAsync();
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }
}
