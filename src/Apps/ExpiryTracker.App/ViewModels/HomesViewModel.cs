using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using HomeTracker.Core.Households;
using HomeTracker.Core.LabelCapture;

namespace ExpiryTracker.App.ViewModels;

public sealed record HomeRow(string Id, string Name, string Detail);

/// <summary>
/// Homes screen: every home this phone is in (e.g. "Amma's house", "Ravi's flat"), with ways to create a new
/// home, join someone else's, and set the name others see.
/// </summary>
public sealed partial class HomesViewModel(
    HomeService homes,
    FamilySyncService sync,
    ScanService scan,
    IBarcodeReader barcodes,
    Dialogs dialogs,
    Clock clock) : ObservableObject
{
    public ObservableCollection<HomeRow> Homes { get; } = [];

    [ObservableProperty]
    public partial string YourName { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public async Task LoadAsync()
    {
        YourName = homes.Me.Name;
        Homes.Clear();
        foreach (var home in await homes.GetHomesAsync())
        {
            var phones = (await homes.GetDevicesAsync(home.Id)).Count(d => d.DeviceId != homes.Me.DeviceId);
            var detail = !home.IsShared
                ? AppResources.Family_NotShared
                : string.Format(AppResources.Homes_SharedDetail, phones + 1,
                    FamilySyncService.LastSync(home.Id) is { } last ? Text.Ago(clock.Now - last) : "–");
            Homes.Add(new HomeRow(home.Id, home.Name, detail));
        }
    }

    [RelayCommand]
    private Task OpenAsync(HomeRow row) => Shell.Current.GoToAsync($"home?id={row.Id}");

    [RelayCommand]
    private async Task NewHomeAsync()
    {
        var name = await dialogs.PromptAsync(AppResources.Homes_New, AppResources.Family_HomeNameHint);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var home = await homes.CreateAsync(name);
        await LoadAsync();
        await Shell.Current.GoToAsync($"home?id={home.Id}");
    }

    [RelayCommand]
    private async Task ChangeNameAsync()
    {
        if (await dialogs.PromptAsync(AppResources.Homes_YourName, AppResources.Homes_YourNameHint, YourName) is { Length: > 0 } name)
        {
            homes.SetDeviceName(name);
            YourName = homes.Me.Name;
        }
    }

    [RelayCommand]
    private Task MoveToNewPhoneAsync() => dialogs.AlertAsync(AppResources.Move_Title, AppResources.Move_Text);

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
        if (!confirmed || !await EnsureNameAsync(homes, dialogs))
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

    /// <summary>Before sharing or joining, ask for the person's name once, so others see "Added by Ravi".</summary>
    public static async Task<bool> EnsureNameAsync(HomeService homes, Dialogs dialogs)
    {
        if (homes.HasChosenName)
        {
            return true;
        }

        var name = await dialogs.PromptAsync(AppResources.Homes_YourName, AppResources.Homes_YourNameHint, "");
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        homes.SetDeviceName(name);
        return true;
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
}
