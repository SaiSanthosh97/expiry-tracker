using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using HomeTracker.Core.Households;
using QRCoder;

namespace ExpiryTracker.App.ViewModels;

public sealed record MemberRow(string Name, string Detail, bool IsSilent);

/// <summary>One home: share it, invite people, see whose phones are in it, rename, leave.</summary>
public sealed partial class FamilyViewModel(HomeService homes, FamilySyncService sync, Dialogs dialogs, Clock clock)
    : ObservableObject, IQueryAttributable
{
    private string? _homeId;
    private Home? _home;
    private bool _loading;

    [ObservableProperty]
    public partial string HomeName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotShared))]
    public partial bool IsShared { get; set; }

    public bool IsNotShared => !IsShared;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial ImageSource? InviteQr { get; set; }

    [ObservableProperty]
    public partial bool RemindThisHome { get; set; } = true;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<MemberRow> Members { get; } = [];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && id is string homeId)
        {
            _homeId = homeId;
        }
    }

    public async Task LoadAsync()
    {
        var home = (_homeId is null ? null : await homes.GetAsync(_homeId)) ?? await homes.GetCurrentAsync();
        _loading = true;
        _home = home;
        _homeId = home.Id;
        HomeName = home.Name;
        IsShared = home.IsShared;
        RemindThisHome = HomeService.GetRemind(home.Id);
        StatusText = !home.IsShared
            ? AppResources.Family_NotShared
            : FamilySyncService.LastSync(home.Id) is { } last
                ? string.Format(AppResources.Family_LastSync, Text.Ago(clock.Now - last))
                : AppResources.Family_NotSyncedYet;
        InviteQr = home.IsShared ? QrImage(HomeInvite.Encode(home)) : null;

        Members.Clear();
        Members.Add(new MemberRow(homes.Me.Name, AppResources.Family_ThisPhone, false));
        foreach (var device in (await homes.GetDevicesAsync(home.Id)).Where(d => d.DeviceId != homes.Me.DeviceId).OrderByDescending(d => d.LastSeen))
        {
            var ago = clock.Now - device.LastSeen;
            Members.Add(new MemberRow(device.Name, string.Format(AppResources.Family_LastSeen, Text.Ago(ago)), ago > TimeSpan.FromHours(24)));
        }

        _loading = false;
    }

    partial void OnRemindThisHomeChanged(bool value)
    {
        if (!_loading && _home is not null)
        {
            HomeService.SetRemind(_home.Id, value);
        }
    }

    [RelayCommand]
    private async Task ShareAsync()
    {
        if (_home is null || !await HomesViewModel.EnsureNameAsync(homes, dialogs))
        {
            return;
        }

        var shared = await homes.StartSharingAsync(_home);
        await sync.AfterShareAsync(shared);
        await LoadAsync();
        await dialogs.AlertAsync(AppResources.Family_SharedTitle, AppResources.Family_SharedText);
    }

    [RelayCommand]
    private async Task SendInviteAsync()
    {
        if (_home is not { IsShared: true } home)
        {
            return;
        }

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = AppResources.Family_SendInvite,
            Text = string.Format(AppResources.Family_InviteMessage, home.Name, HomeInvite.Encode(home)),
        });
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (_home is null || await dialogs.PromptAsync(AppResources.Family_Rename, AppResources.Family_HomeNameHint, _home.Name) is not { Length: > 0 } name)
        {
            return;
        }

        await homes.RenameAsync(_home, name);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        IsBusy = true;
        try
        {
            await sync.RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task LeaveAsync()
    {
        if (_home is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            string.Format(AppResources.Family_LeaveTitle, _home.Name),
            _home.IsShared ? AppResources.Family_LeaveText : AppResources.Homes_DeleteText,
            AppResources.Family_LeaveButton,
            AppResources.Common_Cancel);
        if (!confirmed)
        {
            return;
        }

        await homes.LeaveAsync(_home);
        await sync.UpdateBackgroundAsync();
        await Shell.Current.GoToAsync("..");
    }

    private static ImageSource QrImage(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(12);
        return ImageSource.FromStream(() => new MemoryStream(png));
    }
}
