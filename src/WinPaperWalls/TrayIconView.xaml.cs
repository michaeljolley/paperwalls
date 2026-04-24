using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Windows.Input;
using WinPaperWalls.Services;

namespace WinPaperWalls;

public sealed partial class TrayIconView : UserControl, IDisposable
{
    private readonly IWallpaperService _wallpaperService;

    public TrayIconView()
    {
        InitializeComponent();
        _wallpaperService = App.Services.GetRequiredService<IWallpaperService>();
        ShowSettingsCommand = new ShowSettingsCommandImpl();

        // Set icon from filesystem path (ms-appx:/// doesn't work in unpackaged apps)
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
        TrayIcon.IconSource = new BitmapImage(new Uri(iconPath));

        // Force creation of the Win32 notify icon without needing a visual tree
        TrayIcon.ForceCreate();
    }

    public ICommand ShowSettingsCommand { get; }

    public void Dispose()
    {
        TrayIcon.Dispose();
    }

    private async void NextWallpaper_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Run off the UI thread to avoid deadlocking the message pump.
            // SystemParametersInfo with SPIF_SENDCHANGE broadcasts WM_SETTINGCHANGE
            // synchronously, which deadlocks if called on the UI thread.
            await Task.Run(() => _wallpaperService.ChangeWallpaperAsync());
        }
        catch
        {
            // Silently handle errors for now - will add notifications later
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = App.Services.GetRequiredService<MainWindow>();
        mainWindow.Activate();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        app.Exit();
    }

    private class ShowSettingsCommandImpl : ICommand
    {
#pragma warning disable CS0067 // Event is never used but required by ICommand interface
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            var mainWindow = App.Services.GetRequiredService<MainWindow>();
            mainWindow.Activate();
        }
    }
}
