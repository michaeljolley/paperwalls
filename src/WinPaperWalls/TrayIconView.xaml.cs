using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Windows.Input;
using WinPaperWalls.Services;

namespace WinPaperWalls;

public sealed partial class TrayIconView : UserControl
{
    private readonly IWallpaperService _wallpaperService;

    public TrayIconView()
    {
        InitializeComponent();
        _wallpaperService = App.Services.GetRequiredService<IWallpaperService>();
        ShowSettingsCommand = new ShowSettingsCommandImpl();
    }

    public ICommand ShowSettingsCommand { get; }

    private async void NextWallpaper_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _wallpaperService.ChangeWallpaperAsync();
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
