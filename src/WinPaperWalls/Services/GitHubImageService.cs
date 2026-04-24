using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using WinPaperWalls.Models;

namespace WinPaperWalls.Services;

internal sealed class GitHubImageService : IGitHubImageService
{
	private const string ApiBaseUrl = "https://api.github.com/repos/burkeholland/paper/contents/wallpapers";
	private const string UserAgent = "WinPaperWalls/1.0";
	private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(1);

	private readonly HttpClient _httpClient;
	private readonly ISettingsService _settingsService;
	private readonly ILogger<GitHubImageService> _logger;

	private DateTime _topicsCacheTime = DateTime.MinValue;
	private List<string>? _cachedTopics;
	private readonly Dictionary<string, (DateTime timestamp, List<WallpaperImage> images)> _imageCache = new();
	private readonly object _cacheLock = new();

	public GitHubImageService(
		IHttpClientFactory httpClientFactory,
		ISettingsService settingsService,
		ILogger<GitHubImageService> logger)
	{
		_httpClient = httpClientFactory.CreateClient("GitHub");
		_httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
		_settingsService = settingsService;
		_logger = logger;
	}

	public async Task<List<string>> GetTopicsAsync()
	{
		lock (_cacheLock)
		{
			if (_cachedTopics != null && DateTime.UtcNow - _topicsCacheTime < CacheExpiry)
			{
				_logger.LogDebug("Returning cached topics");
				return new List<string>(_cachedTopics);
			}
		}

		try
		{
			_logger.LogInformation("Fetching topics from GitHub");

			var response = await _httpClient.GetAsync(ApiBaseUrl).ConfigureAwait(false);

			CheckRateLimit(response);

			response.EnsureSuccessStatusCode();

			var items = await response.Content.ReadFromJsonAsync<List<GitHubContentItem>>().ConfigureAwait(false);
			if (items == null)
			{
				_logger.LogWarning("GitHub API returned null response");
				return new List<string>();
			}

			var topics = items
				.Where(i => i.Type == "dir")
				.Select(i => i.Name)
				.ToList();

			var settings = _settingsService.LoadSettings();
			var filteredTopics = topics
				.Where(t => !settings.ExcludedTopics.Contains(t, StringComparer.OrdinalIgnoreCase))
				.ToList();

			lock (_cacheLock)
			{
				_cachedTopics = filteredTopics;
				_topicsCacheTime = DateTime.UtcNow;
			}

			_logger.LogInformation("Fetched {Count} topics (filtered to {FilteredCount})", topics.Count, filteredTopics.Count);
			return filteredTopics;
		}
		catch (HttpRequestException ex)
		{
			_logger.LogError(ex, "Failed to fetch topics from GitHub");

			// Return cached data if available
			lock (_cacheLock)
			{
				if (_cachedTopics != null)
				{
					_logger.LogWarning("Returning stale cached topics due to error");
					return new List<string>(_cachedTopics);
				}
			}

			throw;
		}
	}

	public async Task<List<WallpaperImage>> GetImagesAsync(string topic)
	{
		lock (_cacheLock)
		{
			if (_imageCache.TryGetValue(topic, out var cached) &&
				DateTime.UtcNow - cached.timestamp < CacheExpiry)
			{
				_logger.LogDebug("Returning cached images for topic {Topic}", topic);
				return new List<WallpaperImage>(cached.images);
			}
		}

		try
		{
			_logger.LogInformation("Fetching images for topic {Topic} from GitHub", topic);

			var url = $"{ApiBaseUrl}/{topic}";
			var response = await _httpClient.GetAsync(url).ConfigureAwait(false);

			CheckRateLimit(response);

			response.EnsureSuccessStatusCode();

			var items = await response.Content.ReadFromJsonAsync<List<GitHubContentItem>>().ConfigureAwait(false);
			if (items == null)
			{
				_logger.LogWarning("GitHub API returned null response for topic {Topic}", topic);
				return new List<WallpaperImage>();
			}

			var images = items
				.Where(i => i.Type == "file" && IsImageFile(i.Name))
				.Select(i => new WallpaperImage
				{
					FileName = i.Name,
					Url = i.DownloadUrl ?? string.Empty,
					Topic = topic
				})
				.ToList();

			lock (_cacheLock)
			{
				_imageCache[topic] = (DateTime.UtcNow, images);
			}

			_logger.LogInformation("Fetched {Count} images for topic {Topic}", images.Count, topic);
			return images;
		}
		catch (HttpRequestException ex)
		{
			_logger.LogError(ex, "Failed to fetch images for topic {Topic} from GitHub", topic);

			// Return cached data if available
			lock (_cacheLock)
			{
				if (_imageCache.TryGetValue(topic, out var cached))
				{
					_logger.LogWarning("Returning stale cached images for topic {Topic} due to error", topic);
					return new List<WallpaperImage>(cached.images);
				}
			}

			throw;
		}
	}

	private void CheckRateLimit(HttpResponseMessage response)
	{
		if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues))
		{
			if (int.TryParse(remainingValues.FirstOrDefault(), out var remaining))
			{
				_logger.LogDebug("GitHub API rate limit remaining: {Remaining}", remaining);

				if (remaining < 10)
				{
					_logger.LogWarning("GitHub API rate limit running low: {Remaining} requests remaining", remaining);
				}
			}
		}

		if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
		{
			if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues))
			{
				if (long.TryParse(resetValues.FirstOrDefault(), out var resetTimestamp))
				{
					var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetTimestamp);
					_logger.LogError("GitHub API rate limit exceeded. Resets at {ResetTime}", resetTime);
				}
			}
		}
	}

	private static bool IsImageFile(string fileName)
	{
		var extension = Path.GetExtension(fileName).ToLowerInvariant();
		return extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp";
	}

	private class GitHubContentItem
	{
		[JsonPropertyName("name")]
		public string Name { get; set; } = string.Empty;

		[JsonPropertyName("type")]
		public string Type { get; set; } = string.Empty;

		[JsonPropertyName("path")]
		public string Path { get; set; } = string.Empty;

		[JsonPropertyName("download_url")]
		public string? DownloadUrl { get; set; }
	}
}
