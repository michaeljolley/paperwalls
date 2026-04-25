using Microsoft.Extensions.Logging;
using PaperWalls.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PaperWalls.Services;

internal sealed partial class GitHubImageService : IGitHubImageService
{
	private const string ApiBaseUrl = "https://api.github.com/repos/burkeholland/paper/contents/wallpapers";
	private const string UserAgent = "PaperWalls/1.0";
	private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(24);

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
				LogReturningCachedTopics();
				return new List<string>(_cachedTopics);
			}
		}

		try
		{
			LogFetchingTopicsFromGitHub();

			var response = await _httpClient.GetAsync(ApiBaseUrl).ConfigureAwait(false);

			CheckRateLimit(response);

			response.EnsureSuccessStatusCode();

			var items = await response.Content.ReadFromJsonAsync<List<GitHubContentItem>>().ConfigureAwait(false);
			if (items == null)
			{
				LogGitHubApiReturnedNull();
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

			LogFetchedTopics(topics.Count, filteredTopics.Count);
			return filteredTopics;
		}
		catch (HttpRequestException ex)
		{
			LogFailedToFetchTopics(ex);

			// Return cached data if available
			lock (_cacheLock)
			{
				if (_cachedTopics != null)
				{
					LogReturningStaleCachedTopics();
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
				LogReturningCachedImages(topic);
				return new List<WallpaperImage>(cached.images);
			}
		}

		try
		{
			LogFetchingImagesForTopic(topic);

			var url = $"{ApiBaseUrl}/{topic}";
			var response = await _httpClient.GetAsync(url).ConfigureAwait(false);

			CheckRateLimit(response);

			response.EnsureSuccessStatusCode();

			var items = await response.Content.ReadFromJsonAsync<List<GitHubContentItem>>().ConfigureAwait(false);
			if (items == null)
			{
				LogGitHubApiReturnedNullForTopic(topic);
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

			LogFetchedImages(images.Count, topic);
			return images;
		}
		catch (HttpRequestException ex)
		{
			LogFailedToFetchImages(ex, topic);

			// Return cached data if available
			lock (_cacheLock)
			{
				if (_imageCache.TryGetValue(topic, out var cached))
				{
					LogReturningStaleCachedImages(topic);
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
				LogGitHubApiRateLimit(remaining);

				if (remaining < 10)
				{
					LogGitHubApiRateLimitRunningLow(remaining);
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
					LogGitHubApiRateLimitExceeded(resetTime);
				}
			}
		}
	}

	private static bool IsImageFile(string fileName)
	{
		var extension = Path.GetExtension(fileName).ToLowerInvariant();
		return extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp";
	}

	// LoggerMessage source-generated methods for Native AOT compatibility
	[LoggerMessage(EventId = 2000, Level = LogLevel.Debug, Message = "Returning cached topics")]
	partial void LogReturningCachedTopics();

	[LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Fetching topics from GitHub")]
	partial void LogFetchingTopicsFromGitHub();

	[LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "GitHub API returned null response")]
	partial void LogGitHubApiReturnedNull();

	[LoggerMessage(EventId = 2003, Level = LogLevel.Information, Message = "Fetched {Count} topics (filtered to {FilteredCount})")]
	partial void LogFetchedTopics(int count, int filteredCount);

	[LoggerMessage(EventId = 2004, Level = LogLevel.Error, Message = "Failed to fetch topics from GitHub")]
	partial void LogFailedToFetchTopics(Exception ex);

	[LoggerMessage(EventId = 2005, Level = LogLevel.Warning, Message = "Returning stale cached topics due to error")]
	partial void LogReturningStaleCachedTopics();

	[LoggerMessage(EventId = 2006, Level = LogLevel.Debug, Message = "Returning cached images for topic {Topic}")]
	partial void LogReturningCachedImages(string topic);

	[LoggerMessage(EventId = 2007, Level = LogLevel.Information, Message = "Fetching images for topic {Topic} from GitHub")]
	partial void LogFetchingImagesForTopic(string topic);

	[LoggerMessage(EventId = 2008, Level = LogLevel.Warning, Message = "GitHub API returned null response for topic {Topic}")]
	partial void LogGitHubApiReturnedNullForTopic(string topic);

	[LoggerMessage(EventId = 2009, Level = LogLevel.Information, Message = "Fetched {Count} images for topic {Topic}")]
	partial void LogFetchedImages(int count, string topic);

	[LoggerMessage(EventId = 2010, Level = LogLevel.Error, Message = "Failed to fetch images for topic {Topic} from GitHub")]
	partial void LogFailedToFetchImages(Exception ex, string topic);

	[LoggerMessage(EventId = 2011, Level = LogLevel.Warning, Message = "Returning stale cached images for topic {Topic} due to error")]
	partial void LogReturningStaleCachedImages(string topic);

	[LoggerMessage(EventId = 2012, Level = LogLevel.Debug, Message = "GitHub API rate limit remaining: {Remaining}")]
	partial void LogGitHubApiRateLimit(int remaining);

	[LoggerMessage(EventId = 2013, Level = LogLevel.Warning, Message = "GitHub API rate limit running low: {Remaining} requests remaining")]
	partial void LogGitHubApiRateLimitRunningLow(int remaining);

	[LoggerMessage(EventId = 2014, Level = LogLevel.Error, Message = "GitHub API rate limit exceeded. Resets at {ResetTime}")]
	partial void LogGitHubApiRateLimitExceeded(DateTimeOffset resetTime);

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
