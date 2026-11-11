using ExpiryTracker.App.ViewModels;

namespace ExpiryTracker.App.Views;

public partial class ItemPage : ContentPage
{
    private readonly ItemViewModel _viewModel;

    public ItemPage(ItemViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // Reload every time, so changes made on the Edit screen show up when coming back.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
