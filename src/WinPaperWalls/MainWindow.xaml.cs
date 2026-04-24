using Microsoft.UI.Xaml;
using WinPaperWalls.Interop;

namespace WinPaperWalls;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Hide window when closed instead of destroying it
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // Prevent the window from actually closing - just hide it
        args.Handled = true;
        
        // Hide the window (minimize to tray)
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        PInvoke.User32.ShowWindow(hwnd, PInvoke.User32.WindowShowStyle.SW_HIDE);
    }
}
