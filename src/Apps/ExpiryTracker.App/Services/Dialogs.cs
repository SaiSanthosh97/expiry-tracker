using ExpiryTracker.App.Resources.Strings;

namespace ExpiryTracker.App.Services;

/// <summary>Pop-up dialogs, so view models don't depend on pages.</summary>
public sealed class Dialogs
{
    private static Page Page => Shell.Current?.CurrentPage ?? Application.Current!.Windows[0].Page!;

    public Task AlertAsync(string title, string message) =>
        Page.DisplayAlertAsync(title, message, AppResources.Common_OK);

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        Page.DisplayAlertAsync(title, message, accept, cancel);

    /// <returns>The chosen option, or null when cancelled.</returns>
    public async Task<string?> ChooseAsync(string title, params string[] options)
    {
        var choice = await Page.DisplayActionSheetAsync(title, AppResources.Common_Cancel, null, options);
        return choice is null || choice == AppResources.Common_Cancel ? null : choice;
    }
}
