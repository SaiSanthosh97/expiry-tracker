using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Core.AppUpdater;

namespace ExpiryTracker.App.Services;

/// <summary>
/// Checks GitHub Releases for a newer APK. Automatic checks run at most once a day and only on Wi-Fi:
/// when the app opens (a pop-up) and in the background (a notification, once per new version).
/// </summary>
public sealed class UpdateService(HttpClient http, AppSettings settings, Clock clock, Dialogs dialogs)
{
    /// <summary>Put on the launch intent by the "update available" notification.</summary>
    public const string ExtraOpenUpdate = "hometracker.update.open";

    private const string BackgroundCheckKey = "update.backgroundCheck";
    private const string NotifiedVersionKey = "update.notifiedVersion";

    private static bool _openRequested;
    private static Version? _offered;

    private const string ApkMimeType = "application/vnd.android.package-archive";

    public const string Owner = "SaiSanthosh97";
    public const string Repository = "expiry-tracker";
    public static readonly Uri RepositoryUrl = new($"https://github.com/{Owner}/{Repository}");

    private readonly GitHubReleaseChecker _checker = new(http, Owner, Repository, ".apk");

    public static Version CurrentVersion => AppInfo.Current.Version;

    private static string DownloadFolder => Path.Combine(FileSystem.CacheDirectory, "updates");

    /// <summary>Downloads the new version into the app's cache, checked against GitHub's published checksum.</summary>
    /// <returns>The downloaded file.</returns>
    /// <exception cref="HttpRequestException">No connection.</exception>
    /// <exception cref="DamagedDownloadException">The file was damaged on the way.</exception>
    public async Task<string> DownloadAsync(AvailableUpdate update, IProgress<double> progress, CancellationToken cancellationToken)
    {
        DeleteDownloads();
        Directory.CreateDirectory(DownloadFolder);
        var path = Path.Combine(DownloadFolder, $"use-it-before-{update.Version.ToString(3)}.apk");
        await new UpdateDownloader(http).DownloadAsync(update, path, progress, cancellationToken);
        return path;
    }

    /// <summary>Removes downloaded installers (after an update they are no longer needed).</summary>
    public static void DeleteDownloads()
    {
        try
        {
            if (Directory.Exists(DownloadFolder))
            {
                Directory.Delete(DownloadFolder, recursive: true);
            }
        }
        catch (IOException)
        {
            // Android clears the cache when space is low anyway.
        }
    }

    /// <summary>
    /// Android asks once whether this app may install updates ("Install unknown apps").
    /// </summary>
    public static bool CanInstall =>
        Platform.AppContext.PackageManager?.CanRequestPackageInstalls() == true;

    /// <summary>Opens Android's "Install unknown apps" switch for this app.</summary>
    public static void OpenInstallPermission()
    {
        var intent = new Android.Content.Intent(
            Android.Provider.Settings.ActionManageUnknownAppSources,
            Android.Net.Uri.Parse($"package:{AppInfo.Current.PackageName}"));
        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
        Platform.AppContext.StartActivity(intent);
    }

    /// <summary>Hands the file to Android's installer, which shows its own "Update this app?" screen.</summary>
    public static Task InstallAsync(string apkPath) =>
        Launcher.Default.OpenAsync(new OpenFileRequest(AppResources.Update_AvailableTitle, new ReadOnlyFile(apkPath, ApkMimeType)));

    /// <exception cref="HttpRequestException">No connection.</exception>
    public async Task<AvailableUpdate?> CheckNowAsync()
    {
        var update = await _checker.CheckAsync(CurrentVersion);
        settings.LastUpdateCheck = clock.Now;
        return update;
    }

    private static bool OnWifi =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet
        && Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.WiFi);

    /// <summary>
    /// Background job: once a day on Wi-Fi, shows "Update available" as a notification, once for each new version,
    /// so people hear about it without opening the app.
    /// </summary>
    public async Task NotifyIfAvailableAsync(CancellationToken cancellationToken)
    {
        var last = Preferences.Default.Get(BackgroundCheckKey, 0L);
        if (!OnWifi || clock.Now.ToUnixTimeMilliseconds() - last < (long)TimeSpan.FromDays(1).TotalMilliseconds)
        {
            return;
        }

        AvailableUpdate? update;
        try
        {
            update = await _checker.CheckAsync(CurrentVersion).WaitAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return;
        }

        Preferences.Default.Set(BackgroundCheckKey, clock.Now.ToUnixTimeMilliseconds());
        var version = update?.Version.ToString(3);
        if (update is null || Preferences.Default.Get(NotifiedVersionKey, "") == version)
        {
            return;
        }

        Preferences.Default.Set(NotifiedVersionKey, version);
        HomeTracker.Device.Reminders.DailyReminders.ShowMessage(
            "app-update",
            AppResources.Update_AvailableTitle,
            string.Format(AppResources.Update_NotificationText, version),
            ExtraOpenUpdate);
    }

    /// <summary>The "update available" notification was tapped: offer the update as soon as the app is ready.</summary>
    public static void RequestOpen()
    {
        _openRequested = true;
        if (Shell.Current is not null)
        {
            MainThread.BeginInvokeOnMainThread(() => _ = IPlatformApplication.Current!.Services.GetRequiredService<UpdateService>().OfferIfRequestedAsync());
        }
    }

    /// <summary>Called when the home screen appears, for a notification tap that arrived before the app was ready.</summary>
    public async Task OfferIfRequestedAsync()
    {
        if (!_openRequested || Shell.Current is null)
        {
            return;
        }

        _openRequested = false;
        AvailableUpdate? update;
        try
        {
            update = await CheckNowAsync();
        }
        catch (HttpRequestException)
        {
            await dialogs.AlertAsync(AppResources.Settings_Updates, AppResources.Update_Failed);
            return;
        }
        catch (TaskCanceledException)
        {
            await dialogs.AlertAsync(AppResources.Settings_Updates, AppResources.Update_Failed);
            return;
        }

        if (update is null)
        {
            await dialogs.AlertAsync(AppResources.Settings_Updates, AppResources.Update_None);
            return;
        }

        await OfferAsync(update);
    }

    /// <summary>Shows the "Update available" question, at most once per version while the app is open.</summary>
    public async Task OfferAsync(AvailableUpdate update)
    {
        if (_offered == update.Version)
        {
            return;
        }

        _offered = update.Version;
        await ViewModels.SettingsViewModel.OfferUpdateAsync(update, dialogs);
    }

    /// <returns>An update, or null when not due, not on Wi-Fi, offline or up to date.</returns>
    public async Task<AvailableUpdate?> CheckIfDueAsync()
    {
        if (!OnWifi || settings.LastUpdateCheck is { } last && clock.Now - last < TimeSpan.FromDays(1))
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
