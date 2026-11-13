using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using ExpiryTracker.App.ViewModels;

namespace ExpiryTracker.App.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _viewModel;
    private readonly AppSettings _settings;
    private readonly ReminderHealth _health;
    private readonly UpdateService _updates;
    private readonly Dialogs _dialogs;
    private readonly PhotoStore _photos;
    private bool _startupDone;

    public HomePage(HomeViewModel viewModel, AppSettings settings, ReminderHealth health, UpdateService updates, Dialogs dialogs, PhotoStore photos)
    {
        _photos = photos;
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _settings = settings;
        _health = health;
        _updates = updates;
        _dialogs = dialogs;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();

        if (!_startupDone)
        {
            _startupDone = true;
            await RunStartupAsync();
        }

        await WidgetNavigation.GoAsync();
    }

    /// <summary>First-run welcome, reminder scheduling and the daily update check.</summary>
    private async Task RunStartupAsync()
    {
        if (!_settings.WelcomeShown)
        {
            await _dialogs.AlertAsync(AppResources.Welcome_Title, AppResources.About_Disclaimer);
            if (!await _health.RequestNotificationsAsync())
            {
                await _dialogs.AlertAsync(AppResources.Settings_Notifications, AppResources.Notifications_Denied);
            }

            _settings.WelcomeShown = true;
        }

        await _health.ScheduleAndCatchUpAsync();
        HomeTracker.Device.HomeWidget.HomeWidget.Refresh();
        UpdateService.DeleteDownloads();
        _ = _photos.CleanUpAsync();
        ScanService.ClearPickerCopies();

        if (await _updates.CheckIfDueAsync() is { } update)
        {
            await SettingsViewModel.OfferUpdateAsync(update, _dialogs);
        }
    }
}
