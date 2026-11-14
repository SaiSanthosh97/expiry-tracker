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

	private void OpenFromWidget(Android.Content.Intent? intent)
	{
		if (HomeTracker.Device.HomeWidget.HomeWidget.TakeOpenRequest(intent) is { } request)
		{
			ExpiryTracker.App.Services.WidgetNavigation.Request(request);
		}

		if (intent?.GetBooleanExtra(HomeTracker.Device.Reminders.DailyReminders.ExtraOpen, false) == true)
		{
			if (intent.GetBooleanExtra(HomeTracker.Device.Reminders.DailyReminders.ExtraFullScreen, false) && OperatingSystem.IsAndroidVersionAtLeast(27))
			{
				// Like an alarm clock: shown over the lock screen, only while the reminder screen is open.
				SetShowWhenLocked(true);
				SetTurnScreenOn(true);
				ExpiryTracker.App.Services.WidgetNavigation.ReleaseLockScreen = () =>
				{
					if (OperatingSystem.IsAndroidVersionAtLeast(27))
					{
						SetShowWhenLocked(false);
						SetTurnScreenOn(false);
					}

					ExpiryTracker.App.Services.WidgetNavigation.ReleaseLockScreen = null;
				};
			}

			intent.RemoveExtra(HomeTracker.Device.Reminders.DailyReminders.ExtraOpen);
			intent.RemoveExtra(HomeTracker.Device.Reminders.DailyReminders.ExtraFullScreen);
			ExpiryTracker.App.Services.WidgetNavigation.RequestRoute("reminders");
		}
	}
}
