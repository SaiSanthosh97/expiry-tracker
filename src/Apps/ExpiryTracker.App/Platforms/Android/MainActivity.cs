using Android.App;
using Android.Content.PM;
using Android.OS;

namespace ExpiryTracker.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		OpenFromWidget(Intent);
	}

	protected override void OnNewIntent(Android.Content.Intent? intent)
	{
		base.OnNewIntent(intent);
		OpenFromWidget(intent);
	}

	private static void OpenFromWidget(Android.Content.Intent? intent)
	{
		if (HomeTracker.Device.HomeWidget.HomeWidget.TakeOpenRequest(intent) is { } request)
		{
			ExpiryTracker.App.Services.WidgetNavigation.Request(request);
		}
	}
}
