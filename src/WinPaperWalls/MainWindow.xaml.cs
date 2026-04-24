using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http.Json;
using WinPaperWalls.Interop;
using WinPaperWalls.Services;

namespace WinPaperWalls;

public sealed partial class MainWindow : Window
{
	private readonly ISettingsService _settingsService;
	private readonly IGitHubImageService _gitHubImageService;
	private readonly ICacheService _cacheService;
	private List<string> _allTopics = new();
	private ObservableCollection<TopicItem> _topicItems = new();
	private string _savedStyle = "Fill";
	private bool _settingsLoaded = false;

	public MainWindow()
	{
		InitializeComponent();

		// Get services from DI
		_settingsService = App.Services.GetRequiredService<ISettingsService>();
		_gitHubImageService = App.Services.GetRequiredService<IGitHubImageService>();
		_cacheService = App.Services.GetRequiredService<ICacheService>();

		// Hide window when closed instead of destroying it
		Closed += OnWindowClosed;

		// Live preview: apply wallpaper style immediately on selection change
		StyleComboBox.SelectionChanged += StyleComboBox_SelectionChanged;

		// Load settings when window is activated
		Activated += OnWindowActivated;
	}

	private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
	{
		if (args.WindowActivationState != WindowActivationState.Deactivated)
		{
			// Only load on first activation
			Activated -= OnWindowActivated;
			await LoadSettingsAsync();
		}
	}

	private async Task LoadSettingsAsync()
	{
		_settingsLoaded = false;
		var settings = _settingsService.LoadSettings();

		// Set rotation interval
		SetSelectedInterval(settings.IntervalMinutes);

		// Set wallpaper style
		SetSelectedStyle(settings.WallpaperStyle);
		_savedStyle = settings.WallpaperStyle;

		// Set cache max size
		CacheMaxNumberBox.Value = settings.CacheMaxMB;

		// Set startup toggle from actual registry state
		var startupManager = App.Services.GetRequiredService<StartupManager>();
		StartWithWindowsToggle.IsOn = startupManager.IsStartWithWindows();

		// Update cache size display
		UpdateCacheSizeDisplay();

		// Load topics from GitHub
		await LoadTopicsAsync(settings.ExcludedTopics);

		_settingsLoaded = true;
	}

	private void SetSelectedInterval(int minutes)
	{
		for (int i = 0; i < IntervalComboBox.Items.Count; i++)
		{
			if (IntervalComboBox.Items[i] is ComboBoxItem item &&
				item.Tag is string tag &&
				int.TryParse(tag, out int value) &&
				value == minutes)
			{
				IntervalComboBox.SelectedIndex = i;
				return;
			}
		}
	}

	private void SetSelectedStyle(string style)
	{
		for (int i = 0; i < StyleComboBox.Items.Count; i++)
		{
			if (StyleComboBox.Items[i] is ComboBoxItem item &&
				item.Tag is string tag &&
				tag == style)
			{
				StyleComboBox.SelectedIndex = i;
				return;
			}
		}
	}

	private async Task LoadTopicsAsync(List<string> excludedTopics)
	{
		TopicsLoadingPanel.Visibility = Visibility.Visible;
		TopicsPanel.Visibility = Visibility.Collapsed;
		TopicsErrorBar.IsOpen = false;

		try
		{
			// Get all topics (need to bypass exclusion filter for settings UI)
			// So we'll call the GitHub API directly through a workaround
			_allTopics = await GetAllTopicsFromGitHubAsync();

			// Create topic items with selection state
			_topicItems.Clear();
			foreach (var topic in _allTopics)
			{
				_topicItems.Add(new TopicItem
				{
					Name = topic,
					IsSelected = !excludedTopics.Contains(topic, StringComparer.OrdinalIgnoreCase)
				});
			}

			TopicsRepeater.ItemsSource = _topicItems;
			TopicsPanel.Visibility = Visibility.Visible;
		}
		catch (Exception)
		{
			TopicsErrorBar.IsOpen = true;
			// Still show panel if we have cached topics
			if (_topicItems.Count > 0)
			{
				TopicsPanel.Visibility = Visibility.Visible;
			}
		}
		finally
		{
			TopicsLoadingPanel.Visibility = Visibility.Collapsed;
		}
	}

	private async Task<List<string>> GetAllTopicsFromGitHubAsync()
	{
		// We need all topics, not filtered by exclusions
		// The GitHubImageService filters by settings, so we need to fetch directly
		// NOTE: This creates a new HttpClient for each call - acceptable for settings UI (infrequent use)
		using var httpClient = new System.Net.Http.HttpClient();
		httpClient.DefaultRequestHeaders.Add("User-Agent", "WinPaperWalls/1.0");

		var response = await httpClient.GetAsync("https://api.github.com/repos/burkeholland/paper/contents/wallpapers");
		response.EnsureSuccessStatusCode();

		var items = await response.Content.ReadFromJsonAsync<List<GitHubContentItem>>();
		return items?
			.Where(i => i.Type == "dir")
			.Select(i => i.Name)
			.OrderBy(n => n)
			.ToList() ?? new List<string>();
	}

	private void UpdateCacheSizeDisplay()
	{
		var sizeBytes = _cacheService.GetCacheSizeBytes();
		var sizeMB = sizeBytes / 1024.0 / 1024.0;
		var maxMB = CacheMaxNumberBox.Value;
		CacheSizeText.Text = $"Using {sizeMB:F1} MB of {maxMB:F0} MB";
	}

	private void SelectAllButton_Click(object sender, RoutedEventArgs e)
	{
		foreach (var item in _topicItems)
		{
			item.IsSelected = true;
		}
	}

	private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
	{
		foreach (var item in _topicItems)
		{
			item.IsSelected = false;
		}
	}

	private async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			ClearCacheButton.IsEnabled = false;
			await _cacheService.ClearCacheAsync();
			UpdateCacheSizeDisplay();

			// Show brief success message
			var dialog = new ContentDialog
			{
				Title = "Cache Cleared",
				Content = "All cached wallpapers have been removed.",
				CloseButtonText = "OK",
				XamlRoot = this.Content.XamlRoot
			};
			await dialog.ShowAsync();
		}
		finally
		{
			ClearCacheButton.IsEnabled = true;
		}
	}

	private async void StyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_settingsLoaded) return;

		var newStyle = GetSelectedStyle();
		var desktopService = App.Services.GetRequiredService<IDesktopWallpaperService>();
		var currentWallpaper = desktopService.GetCurrentWallpaperPath();

		if (!string.IsNullOrEmpty(currentWallpaper) && File.Exists(currentWallpaper))
		{
			if (Enum.TryParse<WallpaperStyle>(newStyle, out var styleEnum))
			{
				await Task.Run(() => desktopService.SetWallpaper(currentWallpaper, styleEnum));
			}
		}
	}

	private async void SaveButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			var settings = new Models.AppSettings
			{
				IntervalMinutes = GetSelectedInterval(),
				ExcludedTopics = GetExcludedTopics(),
				WallpaperStyle = GetSelectedStyle(),
				CacheMaxMB = (int)CacheMaxNumberBox.Value,
				StartWithWindows = StartWithWindowsToggle.IsOn
			};

			_settingsService.SaveSettings(settings);
			_savedStyle = settings.WallpaperStyle;

			// Apply startup setting
			var startupManager = App.Services.GetRequiredService<StartupManager>();
			startupManager.SetStartWithWindows(settings.StartWithWindows);

			// Show success message
			SaveSuccessBar.IsOpen = true;

			// Auto-hide after 3 seconds
			_ = Task.Delay(3000).ContinueWith(_ =>
			{
				DispatcherQueue.TryEnqueue(() =>
				{
					SaveSuccessBar.IsOpen = false;
				});
			});
		}
		catch (Exception ex)
		{
			// Show error dialog
			var dialog = new ContentDialog
			{
				Title = "Error Saving Settings",
				Content = $"Failed to save settings: {ex.Message}",
				CloseButtonText = "OK",
				XamlRoot = this.Content.XamlRoot
			};
			await dialog.ShowAsync();
		}
	}

	private int GetSelectedInterval()
	{
		if (IntervalComboBox.SelectedItem is ComboBoxItem item &&
			item.Tag is string tag &&
			int.TryParse(tag, out int value))
		{
			return value;
		}
		return 1440; // default to daily
	}

	private string GetSelectedStyle()
	{
		if (StyleComboBox.SelectedItem is ComboBoxItem item &&
			item.Tag is string tag)
		{
			return tag;
		}
		return "Fill"; // default
	}

	private List<string> GetExcludedTopics()
	{
		return _topicItems
			.Where(t => !t.IsSelected)
			.Select(t => t.Name)
			.ToList();
	}

	private void OnWindowClosed(object sender, WindowEventArgs args)
	{
		// Prevent the window from actually closing - just hide it
		args.Handled = true;

		// Revert wallpaper style if user didn't save
		var currentStyle = GetSelectedStyle();
		if (currentStyle != _savedStyle)
		{
			var desktopService = App.Services.GetRequiredService<IDesktopWallpaperService>();
			var currentWallpaper = desktopService.GetCurrentWallpaperPath();
			if (!string.IsNullOrEmpty(currentWallpaper) && File.Exists(currentWallpaper) &&
				Enum.TryParse<WallpaperStyle>(_savedStyle, out var styleEnum))
			{
				Task.Run(() => desktopService.SetWallpaper(currentWallpaper, styleEnum));
			}
			SetSelectedStyle(_savedStyle);
		}

		// Reset so settings reload on next open
		_settingsLoaded = false;
		Activated += OnWindowActivated;

		// Hide the window (minimize to tray)
		var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
		PInvoke.User32.ShowWindow(hwnd, PInvoke.User32.WindowShowStyle.SW_HIDE);
	}

	private class GitHubContentItem
	{
		public string Name { get; set; } = string.Empty;
		public string Type { get; set; } = string.Empty;
	}
}

internal sealed class TopicItem : INotifyPropertyChanged
{
	private bool _isSelected;

	public string Name { get; set; } = string.Empty;

	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			if (_isSelected != value)
			{
				_isSelected = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;
}
