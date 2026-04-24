using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using System.Threading;
using WinPaperWalls.Services;

namespace WinPaperWalls;

public partial class App : Application
{
    private static Mutex? _instanceMutex;
    private IHost? _host;
    private TrayIconView? _trayIcon;

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
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Register HTTP client factory
                services.AddHttpClient("GitHub");
                services.AddHttpClient();

                // Register services
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<IGitHubImageService, GitHubImageService>();
                services.AddSingleton<ICacheService, CacheService>();
                services.AddSingleton<IWallpaperService, WallpaperService>();
                
                // Register window (created on-demand but kept as singleton)
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Start the host
        _host.Start();

        // Create and show tray icon (app starts minimized to tray)
        _trayIcon = new TrayIconView();
        
        // Do NOT show MainWindow on startup - it opens when user clicks Settings
    }
}
