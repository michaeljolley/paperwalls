using System.Collections.ObjectModel;
using System.Net.Http.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WinPaperWalls.Interop;
using WinPaperWalls.Services;

namespace WinPaperWalls.ViewModels;

#pragma warning disable MVVMTK0045 // Field-based [ObservableProperty] for WinRT compat

public sealed partial class SettingsViewModel : ObservableObject
{
	private readonly ISettingsService _settingsService;
	private readonly ICacheService _cacheService;
	private readonly IDesktopWallpaperService _desktopWallpaperService;
	private readonly StartupManager _startupManager;
	private readonly ILogger<SettingsViewModel> _logger;

	private string _savedStyle = "Fill";

	public SettingsViewModel(
		ISettingsService settingsService,
		ICacheService cacheService,
		IDesktopWallpaperService desktopWallpaperService,
		StartupManager startupManager,
		ILogger<SettingsViewModel> logger)
	{
		_settingsService = settingsService;
		_cacheService = cacheService;
		_desktopWallpaperService = desktopWallpaperService;
		_startupManager = startupManager;
		_logger = logger;
	}

	[ObservableProperty]
	private int _selectedIntervalIndex;

	[ObservableProperty]
	private int _selectedStyleIndex;

	[ObservableProperty]
	private double _cacheMaxMB = 500;

	[ObservableProperty]
	private bool _startWithWindows = true;

	[ObservableProperty]
	private bool _isLoading;

	[ObservableProperty]
	private bool _isTopicsLoading;

	[ObservableProperty]
	private bool _topicsLoaded;

	[ObservableProperty]
	private bool _topicsError;

	[ObservableProperty]
	private string _cacheSizeText = "Using 0 MB of 500 MB";

	[ObservableProperty]
	private bool _saveSuccessVisible;

	[ObservableProperty]
	private bool _settingsLoaded;

	public ObservableCollection<TopicItemViewModel> TopicItems { get; } = new();
	public ObservableCollection<TopicItemViewModel> SelectedTopics { get; } = new();
	public ObservableCollection<string> TopicSuggestions { get; } = new();

	public static readonly (string Label, int Minutes)[] IntervalOptions =
	[
		("Every 30 minutes", 30),
		("Every hour", 60),
		("Every 4 hours", 240),
		("Every 12 hours", 720),
		("Every day", 1440),
		("Every 2 days", 2880),
		("Every week", 10080),
	];

	public static readonly string[] StyleOptions =
	[
		"Fill", "Fit", "Stretch", "Tile", "Center", "Span"
	];

	public async Task LoadAsync()
	{
		SettingsLoaded = false;
		var settings = _settingsService.LoadSettings();

		SelectedIntervalIndex = Array.FindIndex(IntervalOptions, o => o.Minutes == settings.IntervalMinutes);
		if (SelectedIntervalIndex < 0) SelectedIntervalIndex = 4;

		SelectedStyleIndex = Array.IndexOf(StyleOptions, settings.WallpaperStyle);
		if (SelectedStyleIndex < 0) SelectedStyleIndex = 0;
		_savedStyle = settings.WallpaperStyle;

		CacheMaxMB = settings.CacheMaxMB;
		StartWithWindows = _startupManager.IsStartWithWindows();
		UpdateCacheSizeDisplay();

		await LoadTopicsAsync(settings.ExcludedTopics);

		SettingsLoaded = true;
	}

	private async Task LoadTopicsAsync(List<string> excludedTopics)
	{
		IsTopicsLoading = true;
		TopicsLoaded = false;
		TopicsError = false;

		try
		{
			var allTopics = await GetAllTopicsFromGitHubAsync();

			TopicItems.Clear();
			foreach (var topic in allTopics)
			{
				TopicItems.Add(new TopicItemViewModel
				{
					Name = topic,
					IsSelected = !excludedTopics.Contains(topic, StringComparer.OrdinalIgnoreCase),
					RemoveCommand = new RelayCommand(() =>
					{
						var t = TopicItems.FirstOrDefault(x => x.Name == topic);
						if (t != null)
						{
							t.IsSelected = true;
							RefreshSelectedTopics();
						}
					})
				});
			}

			RefreshSelectedTopics();
			TopicsLoaded = true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load topics from GitHub");
			TopicsError = true;
			if (TopicItems.Count > 0)
			{
				TopicsLoaded = true;
			}
		}
		finally
		{
			IsTopicsLoading = false;
		}
	}

	private static async Task<List<string>> GetAllTopicsFromGitHubAsync()
	{
		using var httpClient = new System.Net.Http.HttpClient();
		httpClient.DefaultRequestHeaders.Add("User-Agent", "WinPaperWalls/1.0");

		var response = await httpClient.GetAsync("https://api.github.com/repos/burkeholland/paper/contents/wallpapers");
		response.EnsureSuccessStatusCode();

		var items = await response.Content.ReadFromJsonAsync<List<GitHubContentItem>>();
		return items?
			.Where(i => i.Type == "dir")
			.Select(i => i.Name)
			.OrderBy(n => n)
			.ToList() ?? [];
	}

	private void UpdateCacheSizeDisplay()
	{
		var sizeBytes = _cacheService.GetCacheSizeBytes();
		var sizeMB = sizeBytes / 1024.0 / 1024.0;
		CacheSizeText = $"Using {sizeMB:F1} MB of {CacheMaxMB:F0} MB";
	}

	partial void OnSelectedStyleIndexChanged(int value)
	{
		if (!SettingsLoaded) return;

		var newStyle = StyleOptions[value];
		var currentWallpaper = _desktopWallpaperService.GetCurrentWallpaperPath();

		if (!string.IsNullOrEmpty(currentWallpaper) && File.Exists(currentWallpaper) &&
			Enum.TryParse<WallpaperStyle>(newStyle, out var styleEnum))
		{
			Task.Run(() => _desktopWallpaperService.SetWallpaper(currentWallpaper, styleEnum));
		}
	}

	[RelayCommand]
	private void SelectAllTopics()
	{
		foreach (var item in TopicItems)
		{
			item.IsSelected = true;
		}
		RefreshSelectedTopics();
	}

	[RelayCommand]
	private void DeselectAllTopics()
	{
		foreach (var item in TopicItems)
		{
			item.IsSelected = false;
		}
		RefreshSelectedTopics();
	}

	public void FilterTopics(string query)
	{
		TopicSuggestions.Clear();
		if (string.IsNullOrWhiteSpace(query)) return;

		var matches = TopicItems
			.Where(t => t.IsSelected && t.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
			.Select(t => t.Name);

		foreach (var name in matches)
			TopicSuggestions.Add(name);
	}

	public void AddTopic(string name)
	{
		var topic = TopicItems.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
		if (topic != null && topic.IsSelected)
		{
			topic.IsSelected = false;
			RefreshSelectedTopics();
		}
	}

	private void RefreshSelectedTopics()
	{
		SelectedTopics.Clear();
		foreach (var item in TopicItems.Where(t => !t.IsSelected))
			SelectedTopics.Add(item);
	}

	[RelayCommand]
	private async Task ClearCacheAsync()
	{
		await _cacheService.ClearCacheAsync();
		UpdateCacheSizeDisplay();
	}

	[RelayCommand]
	private void Save()
	{
		var settings = new Models.AppSettings
		{
			IntervalMinutes = IntervalOptions[SelectedIntervalIndex].Minutes,
			ExcludedTopics = TopicItems
				.Where(t => !t.IsSelected)
				.Select(t => t.Name)
				.ToList(),
			WallpaperStyle = StyleOptions[SelectedStyleIndex],
			CacheMaxMB = (int)CacheMaxMB,
			StartWithWindows = StartWithWindows
		};

		_settingsService.SaveSettings(settings);
		_savedStyle = settings.WallpaperStyle;

		_startupManager.SetStartWithWindows(settings.StartWithWindows);

		SaveSuccessVisible = true;
	}

	public void RevertStyleIfNeeded()
	{
		var currentStyle = StyleOptions[SelectedStyleIndex];
		if (currentStyle != _savedStyle)
		{
			var currentWallpaper = _desktopWallpaperService.GetCurrentWallpaperPath();
			if (!string.IsNullOrEmpty(currentWallpaper) && File.Exists(currentWallpaper) &&
				Enum.TryParse<WallpaperStyle>(_savedStyle, out var styleEnum))
			{
				Task.Run(() => _desktopWallpaperService.SetWallpaper(currentWallpaper, styleEnum));
			}
			SelectedStyleIndex = Array.IndexOf(StyleOptions, _savedStyle);
		}
	}

	private sealed class GitHubContentItem
	{
		public string Name { get; set; } = string.Empty;
		public string Type { get; set; } = string.Empty;
	}
}

public sealed partial class TopicItemViewModel : ObservableObject
{
	[ObservableProperty]
	private string _name = string.Empty;

	[ObservableProperty]
	private bool _isSelected;

	public IRelayCommand? RemoveCommand { get; init; }
}

#pragma warning restore MVVMTK0045
