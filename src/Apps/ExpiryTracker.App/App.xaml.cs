using Microsoft.Extensions.DependencyInjection;

namespace ExpiryTracker.App;

public partial class App : Application
{
	private readonly Services.FamilySyncService _sync;

	public App(Services.FamilySyncService sync)
	{
		InitializeComponent();
		_sync = sync;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
		// Family changes arrive within a minute while the app is on screen.
		window.Created += (_, _) => _sync.SetForeground(true);
		window.Resumed += (_, _) => _sync.SetForeground(true);
		window.Stopped += (_, _) => _sync.SetForeground(false);
		window.Destroying += (_, _) => _sync.SetForeground(false);
		return window;
	}
}