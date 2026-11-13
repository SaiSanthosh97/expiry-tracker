using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using HomeTracker.Core.AppUpdater;

namespace ExpiryTracker.App.Views;

/// <summary>
/// Downloads an update and hands it to Android's installer. The first time, Android needs the user to allow
/// installs from this app; the screen explains that and continues when the user comes back.
/// </summary>
public partial class UpdatePage : ContentPage
{
    private readonly UpdateService _updates;
    private readonly CancellationTokenSource _cancel = new();
    private AvailableUpdate? _update;
    private string? _apkPath;

    public UpdatePage(UpdateService updates)
    {
        InitializeComponent();
        _updates = updates;
    }

    public static async Task ShowAsync(AvailableUpdate update)
    {
        var page = IPlatformApplication.Current!.Services.GetRequiredService<UpdatePage>();
        page._update = update;
        await Shell.Current.Navigation.PushModalAsync(page);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_update is null || _apkPath is not null)
        {
            return;
        }

        StatusLabel.Text = string.Format(AppResources.Update_Downloading, _update.Version.ToString(3));
        var progress = new Progress<double>(value =>
        {
            Progress.Progress = value;
            PercentLabel.Text = string.Format(AppResources.Update_Progress, (int)(value * 100));
        });

        try
        {
            _apkPath = await _updates.DownloadAsync(_update, progress, _cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (DamagedDownloadException)
        {
            ShowProblem(AppResources.Update_Damaged);
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            ShowProblem(AppResources.Update_DownloadFailed);
            return;
        }

        Progress.IsVisible = false;
        PercentLabel.Text = "";
        if (Window is { } window)
        {
            // Coming back from Android's permission screen.
            window.Resumed += OnWindowResumed;
        }

        ShowNextStep();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Window is { } window)
        {
            window.Resumed -= OnWindowResumed;
        }
    }

    private void OnWindowResumed(object? sender, EventArgs e) => ShowNextStep();

    private void ShowNextStep()
    {
        var allowed = UpdateService.CanInstall;
        StatusLabel.Text = allowed ? AppResources.Update_Ready : AppResources.Update_AllowText;
        AllowButton.IsVisible = !allowed;
        InstallButton.IsVisible = allowed;
    }

    private void ShowProblem(string message)
    {
        StatusLabel.Text = message;
        Progress.IsVisible = false;
        PercentLabel.Text = "";
    }

    private void OnAllowClicked(object? sender, EventArgs e) => UpdateService.OpenInstallPermission();

    private async void OnInstallClicked(object? sender, EventArgs e)
    {
        if (_apkPath is null)
        {
            return;
        }

        if (!UpdateService.CanInstall)
        {
            ShowNextStep();
            return;
        }

        await UpdateService.InstallAsync(_apkPath);
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        _cancel.Cancel();
        UpdateService.DeleteDownloads();
        await Navigation.PopModalAsync();
    }
}
