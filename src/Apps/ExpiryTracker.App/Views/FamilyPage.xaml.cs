using ExpiryTracker.App.ViewModels;

namespace ExpiryTracker.App.Views;

public partial class FamilyPage : ContentPage
{
    private readonly FamilyViewModel _viewModel;

    public FamilyPage(FamilyViewModel viewModel)
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
