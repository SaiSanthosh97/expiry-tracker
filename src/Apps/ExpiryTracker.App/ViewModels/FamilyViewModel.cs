using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using HomeTracker.Core.Households;
using HomeTracker.Core.LabelCapture;
using QRCoder;

namespace ExpiryTracker.App.ViewModels;

public sealed record MemberRow(string Name, string Detail, bool IsSilent);

/// <summary>Family screen: share this home, invite family, join another home, see the family's phones.</summary>
public sealed partial class FamilyViewModel(
    HomeService homes,
    FamilySyncService sync,
    ScanService scan,
    IBarcodeReader barcodes,
    Dialogs dialogs,
    Clock clock) : ObservableObject
{
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
    public partial bool HasManyHomes { get; set; }

    [ObservableProperty]
    public partial bool RemindThisHome { get; set; } = true;

    [ObservableProperty]
    public partial string DeviceName { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<MemberRow> Members { get; } = [];

    public async Task LoadAsync()
    {
        _loading = true;
        var home = _home = await homes.GetCurrentAsync();
        HomeName = home.Name;
        IsShared = home.IsShared;
        HasManyHomes = (await homes.GetHomesAsync()).Count > 1;
        RemindThisHome = HomeService.GetRemind(home.Id);
        DeviceName = homes.Me.Name;
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
        if (_home is null)
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
    private async Task JoinAsync()
    {
        var invite = await FindInviteAsync();
        if (invite is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            AppResources.Family_JoinTitle,
            string.Format(AppResources.Family_JoinConfirm, invite.Name),
            AppResources.Family_JoinButton,
            AppResources.Common_Cancel);
        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var home = await homes.JoinAsync(invite);
            await sync.AfterJoinAsync(home);
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
        await dialogs.AlertAsync(AppResources.Family_JoinedTitle, string.Format(AppResources.Family_JoinedText, invite.Name));
    }

    /// <summary>An invite copied from WhatsApp is used straight away; otherwise read a QR photo or pasted text.</summary>
    private async Task<Home?> FindInviteAsync()
    {
        try
        {
            if (Clipboard.Default.HasText && HomeInvite.TryDecode(await Clipboard.Default.GetTextAsync(), clock.Now, out var copied))
            {
                return copied;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Clipboard not available; fall through to the other ways.
        }

        var choice = await dialogs.ChooseAsync(AppResources.Family_JoinTitle, AppResources.Family_JoinByQr, AppResources.Family_JoinByPaste);
        if (choice == AppResources.Family_JoinByQr)
        {
            var photo = await scan.GetPhotoAsync(AppResources.Family_JoinByQr);
            if (photo is null)
            {
                return null;
            }

            try
            {
                IsBusy = true;
                foreach (var code in await barcodes.ReadAsync(photo))
                {
                    if (HomeInvite.TryDecode(code.Value, clock.Now, out var scanned))
                    {
                        return scanned;
                    }
                }
            }
            finally
            {
                IsBusy = false;
                ScanService.Discard(photo);
            }

            await dialogs.AlertAsync(AppResources.Family_JoinTitle, AppResources.Family_NoQr);
            return null;
        }

        if (choice == AppResources.Family_JoinByPaste)
        {
            var text = await dialogs.PromptAsync(AppResources.Family_JoinTitle, AppResources.Family_PasteHint);
            if (text is null)
            {
                return null;
            }

            if (HomeInvite.TryDecode(text, clock.Now, out var pasted))
            {
                return pasted;
            }

            await dialogs.AlertAsync(AppResources.Family_JoinTitle, AppResources.Family_BadInvite);
        }

        return null;
    }

    [RelayCommand]
    private async Task SwitchHomeAsync()
    {
        var all = await homes.GetHomesAsync();
        var picked = await dialogs.ChooseAsync(AppResources.Family_SwitchHome, all.Select(h => h.Name).ToArray());
        if (all.FirstOrDefault(h => h.Name == picked) is { } home)
        {
            homes.SetCurrent(home.Id);
            await LoadAsync();
        }
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
    private async Task ChangeDeviceNameAsync()
    {
        if (await dialogs.PromptAsync(AppResources.Family_PhoneName, AppResources.Family_PhoneNameHint, DeviceName) is not { Length: > 0 } name)
        {
            return;
        }

        homes.SetDeviceName(name);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        IsBusy = true;
        try
        {
            await sync.SyncNowAsync();
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
            AppResources.Family_LeaveText,
            AppResources.Family_LeaveButton,
            AppResources.Common_Cancel);
        if (!confirmed)
        {
            return;
        }

        await homes.LeaveAsync(_home);
        await sync.UpdateBackgroundAsync();
        await LoadAsync();
    }

    private static ImageSource QrImage(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(12);
        return ImageSource.FromStream(() => new MemoryStream(png));
    }
}
