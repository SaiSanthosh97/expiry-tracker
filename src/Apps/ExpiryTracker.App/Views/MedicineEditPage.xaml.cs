using ExpiryTracker.App.ViewModels;

namespace ExpiryTracker.App.Views;

public partial class MedicineEditPage : ContentPage
{
    private readonly MedicineEditViewModel _viewModel;

    public MedicineEditPage(MedicineEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadOnceAsync();
    }
}
