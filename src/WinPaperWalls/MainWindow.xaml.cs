using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinPaperWalls.Interop;
using WinPaperWalls.ViewModels;

namespace WinPaperWalls;

public sealed partial class MainWindow : Window
{
	public SettingsViewModel ViewModel { get; }

	public MainWindow()
	{
		InitializeComponent();

		// Custom title bar
		ExtendsContentIntoTitleBar = true;
		SetTitleBar(AppTitleBar);

		// Set a compact window size for settings
		var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
		var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
		var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
		appWindow.Resize(new Windows.Graphics.SizeInt32(900, 1200));

		ViewModel = App.Services.GetRequiredService<SettingsViewModel>();

		// Populate ComboBox items from ViewModel arrays
		foreach (var (label, _) in SettingsViewModel.IntervalOptions)
		{
			IntervalComboBox.Items.Add(label);
		}

		foreach (var style in SettingsViewModel.StyleOptions)
		{
			StyleComboBox.Items.Add(style);
		}

		// Hide window when closed instead of destroying it
		Closed += OnWindowClosed;

		// Load settings when window is activated
		Activated += OnWindowActivated;
	}

	private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
	{
		if (args.WindowActivationState != WindowActivationState.Deactivated)
		{
			Activated -= OnWindowActivated;
			await ViewModel.LoadAsync();
		}
	}

	private void OnWindowClosed(object sender, WindowEventArgs args)
	{
		// Prevent the window from actually closing — just hide it
		args.Handled = true;

		// Revert wallpaper style if user didn't save
		ViewModel.RevertStyleIfNeeded();

		// Reset so settings reload on next open
		ViewModel.SettingsLoaded = false;
		Activated += OnWindowActivated;

		// Hide the window (minimize to tray)
		var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
		PInvoke.User32.ShowWindow(hwnd, PInvoke.User32.WindowShowStyle.SW_HIDE);
	}

	// Helper for x:Bind visibility conversion
	public Visibility BoolToVisibility(bool value) =>
		value ? Visibility.Visible : Visibility.Collapsed;

	private void TopicSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
	{
		if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
		{
			ViewModel.FilterTopics(sender.Text);
		}
	}

	private void TopicSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
	{
		if (args.SelectedItem is string topicName)
		{
			ViewModel.AddTopic(topicName);
			sender.Text = string.Empty;
		}
	}
}
