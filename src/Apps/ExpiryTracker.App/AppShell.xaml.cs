using ExpiryTracker.App.Views;

namespace ExpiryTracker.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("add", typeof(AddItemPage));
		Routing.RegisterRoute("item", typeof(ItemPage));
		Routing.RegisterRoute("settings", typeof(SettingsPage));
		Routing.RegisterRoute("family", typeof(FamilyPage));
	}
}
