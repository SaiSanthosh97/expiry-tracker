using ExpiryTracker.App.ViewModels;

namespace ExpiryTracker.App.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Refresh();
        if (Window is { } window)
        {
            // Coming back from Android's settings screens doesn't re-trigger OnAppearing.
            window.Resumed += OnWindowResumed;
        }
    }

    protected override void OnDisappearing()
    {
        if (Window is { } window)
        {
            window.Resumed -= OnWindowResumed;
        }

        base.OnDisappearing();
    }

    private void OnWindowResumed(object? sender, EventArgs e) => _viewModel.Refresh();
}
