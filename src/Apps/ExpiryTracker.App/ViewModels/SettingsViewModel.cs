using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using HomeTracker.Core.AppUpdater;

namespace ExpiryTracker.App.ViewModels;

public sealed partial class SettingsViewModel(
    HomeService homes,
    AppSettings settings,
    ReminderHealth health,
    ItemService items,
    UpdateService updates,
    Dialogs dialogs,
    Clock clock) : ObservableObject
{
    [ObservableProperty]
    public partial TimeSpan ReminderTime { get; set; } = settings.ReminderTime.ToTimeSpan();

    [ObservableProperty]
    public partial bool NotificationsOk { get; set; }

    [ObservableProperty]
    public partial bool BatteryOk { get; set; }

    public string VersionText => string.Format(AppResources.Settings_Version, AppInfo.Current.VersionString);

    /// <summary>Re-reads permission state (call on appearing and when returning from Android settings).</summary>
    public void Refresh()
    {
        NotificationsOk = health.NotificationsEnabled;
        BatteryOk = health.BatteryUnrestricted;
    }

    partial void OnReminderTimeChanged(TimeSpan value)
    {
        settings.ReminderTime = TimeOnly.FromTimeSpan(value);
        _ = health.ScheduleAndCatchUpAsync();
    }

    [RelayCommand]
    private async Task AllowNotificationsAsync()
    {
        if (!await health.RequestNotificationsAsync())
        {
            // Android only shows the permission prompt twice; after that it must be allowed in settings.
            health.OpenAppSettings();
        }

        Refresh();
    }

    [RelayCommand]
    private void FixBattery() => health.RequestBatteryUnrestricted();

    [RelayCommand]
    private void OpenAppSettings() => health.OpenAppSettings();

    [RelayCommand]
    private void SendTestReminder() => health.SendTestReminder();

    [RelayCommand]
    private async Task ExportAsync()
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"home-tracker-list-{clock.Today:yyyy-MM-dd}.json");
        await File.WriteAllTextAsync(path, await items.ExportAsync());
        await Share.Default.RequestAsync(new ShareFileRequest(AppResources.Settings_Export, new ShareFile(path, "application/json")));
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var file = await FilePicker.Default.PickAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var current = await homes.GetCurrentAsync();
            var known = (await homes.GetHomesAsync()).Select(h => h.Id).ToHashSet();
            var count = await items.ImportAsync(await reader.ReadToEndAsync(), current.Id, known);
            await dialogs.AlertAsync(AppResources.Settings_Import, string.Format(AppResources.Import_Done, count));
        }
        catch (FormatException ex)
        {
            await dialogs.AlertAsync(AppResources.Import_Failed, ex.Message);
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        AvailableUpdate? update;
        try
        {
            update = await updates.CheckNowAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await dialogs.AlertAsync(AppResources.Settings_Updates, AppResources.Update_Failed);
            return;
        }

        if (update is null)
        {
            await dialogs.AlertAsync(AppResources.Settings_Updates, AppResources.Update_None);
            return;
        }

        await OfferUpdateAsync(update, dialogs);
    }

    [RelayCommand]
    private Task OpenFamilyAsync() => Shell.Current.GoToAsync("family");

    [RelayCommand]
    private Task OpenSourceCodeAsync() => Launcher.Default.OpenAsync(UpdateService.RepositoryUrl);

    public static async Task OfferUpdateAsync(AvailableUpdate update, Dialogs dialogs)
    {
        var download = await dialogs.ConfirmAsync(
            AppResources.Update_AvailableTitle,
            string.Format(AppResources.Update_AvailableText, update.Version.ToString(3)) + "\n\n" + AppResources.Update_InstallHint,
            AppResources.Update_Download,
            AppResources.Update_Later);
        if (download)
        {
            // Downloaded inside the app and checked; Android's installer then asks the user to confirm.
            await Views.UpdatePage.ShowAsync(update);
        }
    }
}
