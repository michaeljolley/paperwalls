using Microsoft.Extensions.Logging;
using PaperWalls.Interop;

namespace PaperWalls.Services;

internal sealed partial class WallpaperService : IWallpaperService
{
	private const int RecentHistorySize = 20;

	private readonly IGitHubImageService _githubService;
	private readonly ICacheService _cacheService;
	private readonly ISettingsService _settingsService;
	private readonly IDesktopWallpaperService _desktopWallpaperService;
	private readonly ILogger<WallpaperService> _logger;

	private readonly HashSet<string> _recentlyUsed = new();
	private readonly object _recentLock = new();

	public WallpaperService(
		IGitHubImageService githubService,
		ICacheService cacheService,
		ISettingsService settingsService,
		IDesktopWallpaperService desktopWallpaperService,
		ILogger<WallpaperService> logger)
	{
		_githubService = githubService;
		_cacheService = cacheService;
		_settingsService = settingsService;
		_desktopWallpaperService = desktopWallpaperService;
		_logger = logger;
	}

	public async Task ChangeWallpaperAsync()
	{
		try
		{
			LogStartingWallpaperChange();

			// Get settings
			var settings = _settingsService.LoadSettings();

			// Get available topics
			var topics = await _githubService.GetTopicsAsync();
			if (topics.Count == 0)
			{
				LogNoTopicsAvailable();
				return;
			}

			// Try to find a suitable image
			string? imagePath = null;
			int maxAttempts = Math.Min(10, topics.Count * 3);

			for (int attempt = 0; attempt < maxAttempts && imagePath == null; attempt++)
			{
				// Pick random topic
				var topic = topics[Random.Shared.Next(topics.Count)];
				LogSelectedTopic(topic, attempt + 1);

				// Get images in topic
				var images = await _githubService.GetImagesAsync(topic);
				if (images.Count == 0)
				{
					LogNoImagesFoundInTopic(topic);
					continue;
				}

				// Filter out recently used images
				var availableImages = images
					.Where(img => !IsRecentlyUsed(img.FileName))
					.ToList();

				if (availableImages.Count == 0)
				{
					LogAllImagesRecentlyUsed(topic);

					// If we've tried many times, just use any image from this topic
					if (attempt >= 5)
					{
						availableImages = images;
					}
					else
					{
						continue;
					}
				}

				// Pick random image
				var selectedImage = availableImages[Random.Shared.Next(availableImages.Count)];
				LogSelectedImage(selectedImage.FileName, selectedImage.Topic);

				// Download/get from cache
				try
				{
					imagePath = await _cacheService.DownloadImageAsync(
						selectedImage.Url,
						selectedImage.FileName);

					// Check cache size and evict if needed
					var cacheSize = _cacheService.GetCacheSizeBytes();
					var maxCacheBytes = settings.CacheMaxMB * 1024L * 1024L;

					if (cacheSize > maxCacheBytes)
					{
						LogCacheSizeExceedsLimit(cacheSize / 1024 / 1024, settings.CacheMaxMB);

						await _cacheService.EvictOldestAsync(maxCacheBytes);
					}

					// Mark as recently used
					AddToRecentlyUsed(selectedImage.FileName);
				}
				catch (Exception ex)
				{
					LogFailedToDownloadImage(ex, selectedImage.FileName);
					imagePath = null;
					continue;
				}
			}

			if (imagePath == null)
			{
				LogFailedToFindSuitableWallpaper(maxAttempts);
				return;
			}

			// Parse wallpaper style
			if (!Enum.TryParse<WallpaperStyle>(settings.WallpaperStyle, true, out var style))
			{
				LogInvalidWallpaperStyle(settings.WallpaperStyle);
				style = WallpaperStyle.Fill;
			}

			// Set wallpaper
			_desktopWallpaperService.SetWallpaper(imagePath, style);
			LogSuccessfullyChangedWallpaper(imagePath, style);
		}
		catch (Exception ex)
		{
			LogFailedToChangeWallpaper(ex);
			throw;
		}
	}

	private bool IsRecentlyUsed(string fileName)
	{
		lock (_recentLock)
		{
			return _recentlyUsed.Contains(fileName);
		}
	}

	private void AddToRecentlyUsed(string fileName)
	{
		lock (_recentLock)
		{
			_recentlyUsed.Add(fileName);

			// Trim to size if needed
			if (_recentlyUsed.Count > RecentHistorySize)
			{
				// Remove oldest (first) item - HashSet doesn't maintain insertion order
				// so we'll just remove one at random when we exceed the limit
				// For better LRU, we'd use a LinkedHashSet-like structure
				var toRemove = _recentlyUsed.First();
				_recentlyUsed.Remove(toRemove);
			}
		}
	}

	// LoggerMessage source-generated methods for Native AOT compatibility
	[LoggerMessage(EventId = 4000, Level = LogLevel.Information, Message = "Starting wallpaper change")]
	partial void LogStartingWallpaperChange();

	[LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "No topics available after filtering")]
	partial void LogNoTopicsAvailable();

	[LoggerMessage(EventId = 4002, Level = LogLevel.Debug, Message = "Selected topic: {Topic} (attempt {Attempt})")]
	partial void LogSelectedTopic(string topic, int attempt);

	[LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "No images found in topic {Topic}")]
	partial void LogNoImagesFoundInTopic(string topic);

	[LoggerMessage(EventId = 4004, Level = LogLevel.Debug, Message = "All images in topic {Topic} were recently used")]
	partial void LogAllImagesRecentlyUsed(string topic);

	[LoggerMessage(EventId = 4005, Level = LogLevel.Information, Message = "Selected image: {FileName} from topic {Topic}")]
	partial void LogSelectedImage(string fileName, string topic);

	[LoggerMessage(EventId = 4006, Level = LogLevel.Information, Message = "Cache size ({CurrentMB} MB) exceeds limit ({MaxMB} MB), evicting oldest files")]
	partial void LogCacheSizeExceedsLimit(long currentMB, int maxMB);

	[LoggerMessage(EventId = 4007, Level = LogLevel.Error, Message = "Failed to download image {FileName}")]
	partial void LogFailedToDownloadImage(Exception ex, string fileName);

	[LoggerMessage(EventId = 4008, Level = LogLevel.Error, Message = "Failed to find and download a suitable wallpaper after {Attempts} attempts")]
	partial void LogFailedToFindSuitableWallpaper(int attempts);

	[LoggerMessage(EventId = 4009, Level = LogLevel.Warning, Message = "Invalid wallpaper style {Style}, using Fill")]
	partial void LogInvalidWallpaperStyle(string style);

	[LoggerMessage(EventId = 4010, Level = LogLevel.Information, Message = "Successfully changed wallpaper to {Path} with style {Style}")]
	partial void LogSuccessfullyChangedWallpaper(string path, WallpaperStyle style);

	[LoggerMessage(EventId = 4011, Level = LogLevel.Error, Message = "Failed to change wallpaper")]
	partial void LogFailedToChangeWallpaper(Exception ex);
}
