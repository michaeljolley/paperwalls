using Microsoft.Extensions.Logging;
using WinPaperWalls.Interop;

namespace WinPaperWalls.Services;

internal sealed class WallpaperService : IWallpaperService
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
			_logger.LogInformation("Starting wallpaper change");

			// Get settings
			var settings = _settingsService.LoadSettings();

			// Get available topics
			var topics = await _githubService.GetTopicsAsync();
			if (topics.Count == 0)
			{
				_logger.LogWarning("No topics available after filtering");
				return;
			}

			// Try to find a suitable image
			string? imagePath = null;
			int maxAttempts = Math.Min(10, topics.Count * 3);

			for (int attempt = 0; attempt < maxAttempts && imagePath == null; attempt++)
			{
				// Pick random topic
				var topic = topics[Random.Shared.Next(topics.Count)];
				_logger.LogDebug("Selected topic: {Topic} (attempt {Attempt})", topic, attempt + 1);

				// Get images in topic
				var images = await _githubService.GetImagesAsync(topic);
				if (images.Count == 0)
				{
					_logger.LogWarning("No images found in topic {Topic}", topic);
					continue;
				}

				// Filter out recently used images
				var availableImages = images
					.Where(img => !IsRecentlyUsed(img.FileName))
					.ToList();

				if (availableImages.Count == 0)
				{
					_logger.LogDebug("All images in topic {Topic} were recently used", topic);

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
				_logger.LogInformation("Selected image: {FileName} from topic {Topic}",
					selectedImage.FileName, selectedImage.Topic);

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
						_logger.LogInformation("Cache size ({CurrentMB} MB) exceeds limit ({MaxMB} MB), evicting oldest files",
							cacheSize / 1024 / 1024, settings.CacheMaxMB);

						await _cacheService.EvictOldestAsync(maxCacheBytes);
					}

					// Mark as recently used
					AddToRecentlyUsed(selectedImage.FileName);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to download image {FileName}", selectedImage.FileName);
					imagePath = null;
					continue;
				}
			}

			if (imagePath == null)
			{
				_logger.LogError("Failed to find and download a suitable wallpaper after {Attempts} attempts", maxAttempts);
				return;
			}

			// Parse wallpaper style
			if (!Enum.TryParse<WallpaperStyle>(settings.WallpaperStyle, true, out var style))
			{
				_logger.LogWarning("Invalid wallpaper style {Style}, using Fill", settings.WallpaperStyle);
				style = WallpaperStyle.Fill;
			}

			// Set wallpaper
			_desktopWallpaperService.SetWallpaper(imagePath, style);
			_logger.LogInformation("Successfully changed wallpaper to {Path} with style {Style}", imagePath, style);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to change wallpaper");
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
}
