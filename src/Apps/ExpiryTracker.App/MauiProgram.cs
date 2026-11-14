using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using ExpiryTracker.App.ViewModels;
using ExpiryTracker.App.Views;
using HomeTracker.Core.DateExtraction;
using HomeTracker.Core.Items;
using HomeTracker.Core.LabelCapture;
using HomeTracker.Core.ProductCatalog;
using HomeTracker.Core.ShelfLife;
using HomeTracker.Core.Households;
using HomeTracker.Device.BackgroundWork;
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
		var databasePath = Path.Combine(FileSystem.AppDataDirectory, "items.db3");
		services.AddSingleton<IItemRepository>(_ => new SqliteItemRepository(databasePath));
		services.AddSingleton<IHomeRepository>(_ => new SqliteHomeRepository(databasePath));
		services.AddSingleton<HomeTracker.Core.Medicines.IMedicineRepository>(_ => new SqliteMedicineRepository(databasePath));
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
		services.AddSingleton<HomeService>();
		services.AddSingleton<FamilySyncService>();
		services.AddSingleton<MedicineService>();
		services.AddSingleton<DoseEngine>();
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
		services.AddTransient<FamilyViewModel>();
		services.AddTransient<FamilyPage>();
		services.AddTransient<HomesViewModel>();
		services.AddTransient<HomesPage>();
		services.AddSingleton<MedicinesViewModel>();
		services.AddSingleton<MedicinesPage>();
		services.AddTransient<MedicineEditViewModel>();
		services.AddTransient<MedicineEditPage>();

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

		// Medicine doses: on-time alarm, Taken / Later / Skip, missed after an hour.
		var doses = app.Services.GetRequiredService<DoseEngine>();
		doses.Initialize();
		// Family sync is created now so it sends medicine changes too.
		app.Services.GetRequiredService<FamilySyncService>();

		// Family sync in the background (about every 15 minutes, only once a home is shared).
		BackgroundWork.Initialize(token => app.Services.GetRequiredService<FamilySyncService>().SyncNowAsync(token));

		// Home-screen widget: redrawn whenever the list changes.
		HomeWidget.Initialize(
			() => app.Services.GetRequiredService<WidgetSource>(),
			new HomeWidgetOptions(AppResources.Widget_Used, AppResources.Widget_Add));
		app.Services.GetRequiredService<ItemService>().Changed += (_, _) => HomeWidget.Refresh();

		return app;
	}
}
