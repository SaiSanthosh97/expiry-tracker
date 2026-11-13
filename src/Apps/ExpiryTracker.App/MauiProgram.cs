using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using ExpiryTracker.App.ViewModels;
using ExpiryTracker.App.Views;
using HomeTracker.Core.DateExtraction;
using HomeTracker.Core.Items;
using HomeTracker.Core.LabelCapture;
using HomeTracker.Core.ProductCatalog;
using HomeTracker.Core.ShelfLife;
using HomeTracker.Device.HomeWidget;
using HomeTracker.Device.Reminders;
using HomeTracker.Device.Vision;
using HomeTracker.Storage.Sqlite;
using Microsoft.Extensions.Logging;

namespace ExpiryTracker.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var services = builder.Services;

		// Components
		services.AddSingleton<IItemRepository>(_ => new SqliteItemRepository(Path.Combine(FileSystem.AppDataDirectory, "items.db3")));
		services.AddSingleton(_ => new ShelfLifeCatalog());
		services.AddSingleton<ItemEvaluator>();
		services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
		services.AddSingleton(_ => new LabelDateExtractor());
		services.AddSingleton<IBarcodeReader, MlKitBarcodeReader>();
		services.AddSingleton<ITextReader, MlKitTextReader>();
		services.AddSingleton<LabelReader>();
		services.AddSingleton<HouseholdProductProvider>();
		services.AddSingleton(sp => new OpenFoodFactsProvider(
			sp.GetRequiredService<HttpClient>(), "HomeTracker", AppInfo.Current.VersionString, UpdateService.RepositoryUrl.ToString()));
		// Household history first (instant, offline), then Open Food Facts.
		services.AddSingleton(sp => new ProductLookup(
		[
			sp.GetRequiredService<HouseholdProductProvider>(),
			sp.GetRequiredService<OpenFoodFactsProvider>(),
		]));

		// App services
		services.AddSingleton<AppSettings>();
		services.AddSingleton<Clock>();
		services.AddSingleton<ItemService>();
		services.AddSingleton<ReminderSource>();
		services.AddSingleton<ReminderHealth>();
		services.AddSingleton<UpdateService>();
		services.AddSingleton<UndoService>();
		services.AddSingleton<ScanService>();
		services.AddSingleton<Dialogs>();
		services.AddSingleton<PhotoStore>();
		services.AddSingleton<WidgetSource>();

		// Screens
		services.AddSingleton<HomeViewModel>();
		services.AddSingleton<HomePage>();
		services.AddTransient<AddItemViewModel>();
		services.AddTransient<AddItemPage>();
		services.AddTransient<ItemViewModel>();
		services.AddTransient<ItemPage>();
		services.AddTransient<SettingsViewModel>();
		services.AddTransient<SettingsPage>();
		services.AddTransient<UpdatePage>();

		var app = builder.Build();

		// Also runs when Android starts the app in the background for a reminder or after a restart.
		var settings = app.Services.GetRequiredService<AppSettings>();
		DailyReminders.Initialize(
			() => app.Services.GetRequiredService<ReminderSource>(),
			() => settings.ReminderTime,
			new ReminderOptions(
				AppResources.Reminder_Channel,
				AppResources.Reminder_ChannelDescription,
				Resource.Drawable.notification_icon,
				AppResources.Reminder_UsedUp,
				AppResources.Reminder_Thrown,
				AppResources.Reminder_Tomorrow));

		// Home-screen widget: redrawn whenever the list changes.
		HomeWidget.Initialize(
			() => app.Services.GetRequiredService<WidgetSource>(),
			new HomeWidgetOptions(AppResources.Widget_Used, AppResources.Widget_Add));
		app.Services.GetRequiredService<ItemService>().Changed += (_, _) => HomeWidget.Refresh();

		return app;
	}
}
