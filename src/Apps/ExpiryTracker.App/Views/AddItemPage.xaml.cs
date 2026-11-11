using ExpiryTracker.App.ViewModels;

namespace ExpiryTracker.App.Views;

public partial class AddItemPage : ContentPage
{
    public AddItemPage(AddItemViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
