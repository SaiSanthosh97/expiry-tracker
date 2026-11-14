using ExpiryTracker.App.ViewModels;

namespace ExpiryTracker.App.Views;

public partial class RemindersPage : ContentPage
{
    private readonly RemindersViewModel _viewModel;

    public RemindersPage(RemindersViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Shown over the lock screen only while this screen is open.
        Services.WidgetNavigation.ReleaseLockScreen?.Invoke();
    }
}
