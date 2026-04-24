using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using WinPaperWalls.Services;
using WinUIEx;

namespace WinPaperWalls;

public partial class App : Application
{
	private static Mutex? _instanceMutex;
	private IHost? _host;
	private TrayIcon? _trayIcon;

	public App()
	{
		InitializeComponent();
	}

	public static IServiceProvider Services => ((App)Current)._host!.Services;

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		// Single instance check
		_instanceMutex = new Mutex(true, "WinPaperWalls_SingleInstance", out bool createdNew);
		if (!createdNew)
		{
			// Another instance is running
			Exit();
			return;
		}

		// Build the host with DI container
		var logPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"WinPaperWalls", "logs", "winpaperwalls-.log");

		_host = Host.CreateDefaultBuilder()
			.UseSerilog((context, configuration) =>
			{
				configuration
					.WriteTo.File(
						logPath,
						rollingInterval: Serilog.RollingInterval.Day,
						retainedFileCountLimit: 14,
						fileSizeLimitBytes: 10 * 1024 * 1024,
						outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}");
			})
			.ConfigureServices((context, services) =>
			{
				// Register HTTP client factory
				services.AddHttpClient("GitHub");
				services.AddHttpClient();

				// Register services
				services.AddSingleton<ISettingsService, SettingsService>();
				services.AddSingleton<IGitHubImageService, GitHubImageService>();
				services.AddSingleton<ICacheService, CacheService>();
				services.AddSingleton<IDesktopWallpaperService, DesktopWallpaperService>();
				services.AddSingleton<IWallpaperService, WallpaperService>();
				services.AddSingleton<StartupManager>();
				services.AddSingleton<ILogBundleService, LogBundleService>();

				// Register scheduler as both ISchedulerService and IHostedService
				services.AddSingleton<SchedulerService>();
				services.AddSingleton<ISchedulerService>(sp => sp.GetRequiredService<SchedulerService>());
				services.AddHostedService(sp => sp.GetRequiredService<SchedulerService>());

				// Register window (created on-demand but kept as singleton)
				services.AddSingleton<MainWindow>();
			})
			.Build();

		// Start the host
		_host.Start();

		// Create and show tray icon (app starts minimized to tray)
		var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
		_trayIcon = new TrayIcon(1, iconPath, "WinPaperWalls");
		_trayIcon.IsVisible = true;

		_trayIcon.Selected += (s, e) =>
		{
			var mainWindow = Services.GetRequiredService<MainWindow>();
			mainWindow.Activate();
		};

		_trayIcon.ContextMenu += (s, e) =>
		{
			var flyout = new MenuFlyout();

			var refreshItem = new MenuFlyoutItem { Text = "Refresh PaperWall" };
			refreshItem.Click += async (_, _) =>
			{
				try
				{
					var wallpaperService = Services.GetRequiredService<IWallpaperService>();
					await Task.Run(() => wallpaperService.ChangeWallpaperAsync());
				}
				catch
				{
					// Silently handle errors for now
				}
			};
			flyout.Items.Add(refreshItem);

			var settingsItem = new MenuFlyoutItem { Text = "Settings..." };
			settingsItem.Click += (_, _) =>
			{
				var mainWindow = Services.GetRequiredService<MainWindow>();
				mainWindow.Activate();
			};
			flyout.Items.Add(settingsItem);

			var reportBugItem = new MenuFlyoutItem { Text = "Report bug" };
			reportBugItem.Click += async (_, _) =>
			{
				try
				{
					var logBundleService = Services.GetRequiredService<ILogBundleService>();
					var zipPath = await Task.Run(() => logBundleService.CreateBugReportAsync());

					var mainWindow = Services.GetRequiredService<MainWindow>();
					mainWindow.Activate();

					var dialog = new ContentDialog
					{
						Title = "Bug Report Created",
						Content = "Bug report .zip has been created on your Desktop.",
						CloseButtonText = "OK",
						XamlRoot = mainWindow.Content.XamlRoot
					};
					await dialog.ShowAsync();
				}
				catch
				{
					// Silently handle errors for now
				}
			};
			flyout.Items.Add(reportBugItem);

			flyout.Items.Add(new MenuFlyoutSeparator());

			var exitItem = new MenuFlyoutItem { Text = "Exit" };
			exitItem.Click += (_, _) => Exit();
			flyout.Items.Add(exitItem);

			e.Flyout = flyout;
		};

		// Do NOT show MainWindow on startup - it opens when user clicks Settings
	}

	public new async void Exit()
	{
		// Dispose tray icon properly
		if (_trayIcon != null)
		{
			_trayIcon.Dispose();
			_trayIcon = null;
		}

		// Stop the host gracefully
		if (_host != null)
		{
			await _host.StopAsync(TimeSpan.FromSeconds(5));
			_host.Dispose();
			_host = null;
		}

		// Release mutex
		_instanceMutex?.ReleaseMutex();
		_instanceMutex?.Dispose();
		_instanceMutex = null;

		// Call base Exit to actually exit the application
		base.Exit();
	}
}
