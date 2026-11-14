using ExpiryTracker.App.ViewModels;

namespace ExpiryTracker.App.Views;

public partial class HomesPage : ContentPage
{
    private readonly HomesViewModel _viewModel;

    public HomesPage(HomesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
