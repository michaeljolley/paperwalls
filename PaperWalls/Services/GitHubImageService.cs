using Microsoft.Extensions.Logging;
using PaperWalls.Models;
using PaperWalls.Serialization;
using System.Net.Http.Json;

namespace PaperWalls.Services;

internal sealed partial class GitHubImageService : IGitHubImageService
{
	private const string ApiBaseUrl = "https://api.github.com/repos/burkeholland/paper/contents/wallpapers";
	private const string UserAgent = "PaperWalls/1.0";
	private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(24);

	private readonly HttpClient _httpClient;
	private readonly ISettingsService _settingsService;
	private readonly ILogger<GitHubImageService> _logger;

	private const int MaxConsecutiveFailures = 3;
	private static readonly TimeSpan CoolDownDuration = TimeSpan.FromMinutes(5);

	private DateTime _allTopicsCacheTime = DateTime.MinValue;
	private List<string>? _cachedAllTopics;
	private readonly Dictionary<string, (DateTime timestamp, List<WallpaperImage> images)> _imageCache = new();
	private readonly object _cacheLock = new();
	private int _consecutiveFailures;
	private DateTimeOffset? _coolDownUntil;

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

	public async Task<List<string>> GetTopicsAsync(CancellationToken cancellationToken = default)
	{
		var allTopics = await GetAllTopicsAsync(cancellationToken).ConfigureAwait(false);

		if (allTopics.Count == 0)
			return allTopics;

		var settings = _settingsService.LoadSettings();
		var filtered = allTopics
			.Where(t => !settings.ExcludedTopics.Contains(t, StringComparer.OrdinalIgnoreCase))
			.ToList();

		LogFilteredTopics(filtered.Count, allTopics.Count - filtered.Count);
		return filtered;
	}

	public async Task<List<string>> GetAllTopicsAsync(CancellationToken cancellationToken = default)
	{
		lock (_cacheLock)
		{
			if (_cachedAllTopics != null && DateTime.UtcNow - _allTopicsCacheTime < CacheExpiry)
			{
				LogReturningCachedTopics();
				return new List<string>(_cachedAllTopics);
			}

			if (_coolDownUntil.HasValue)
			{
				if (DateTimeOffset.UtcNow < _coolDownUntil.Value)
				{
					LogGitHubApiInCoolDown(_coolDownUntil.Value);
					if (_cachedAllTopics != null)
					{
						LogReturningStaleCachedTopics();
						return new List<string>(_cachedAllTopics);
					}
					return new List<string>();
				}
				else
				{
					_coolDownUntil = null;
					_consecutiveFailures = 0;
				}
			}
		}

		try
		{
			LogFetchingTopicsFromGitHub();

			var response = await _httpClient.GetAsync(ApiBaseUrl, cancellationToken).ConfigureAwait(false);

			CheckRateLimit(response);

			response.EnsureSuccessStatusCode();

			var items = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.ListGitHubContentItem, cancellationToken).ConfigureAwait(false);
			if (items == null)
			{
				LogGitHubApiReturnedNull();
				return new List<string>();
			}

			var allTopics = items
				.Where(i => i.Type == "dir")
				.Select(i => i.Name)
				.OrderBy(n => n)
				.ToList();

			lock (_cacheLock)
			{
				_cachedAllTopics = allTopics;
				_allTopicsCacheTime = DateTime.UtcNow;
				ResetCircuitBreaker();
			}

			LogFetchedTopics(allTopics.Count, allTopics.Count);
			return new List<string>(allTopics);
		}
		catch (HttpRequestException ex)
		{
			LogFailedToFetchTopics(ex);
			RecordFailure();

			lock (_cacheLock)
			{
				if (_cachedAllTopics != null)
				{
					LogReturningStaleCachedTopics();
					return new List<string>(_cachedAllTopics);
				}
			}

			throw;
		}
	}

	public async Task<List<WallpaperImage>> GetImagesAsync(string topic, CancellationToken cancellationToken = default)
	{
		lock (_cacheLock)
		{
			if (_imageCache.TryGetValue(topic, out var cached) &&
				DateTime.UtcNow - cached.timestamp < CacheExpiry)
			{
				LogReturningCachedImages(topic);
				return new List<WallpaperImage>(cached.images);
			}

			if (_coolDownUntil.HasValue)
			{
				if (DateTimeOffset.UtcNow < _coolDownUntil.Value)
				{
					LogGitHubApiInCoolDown(_coolDownUntil.Value);
					if (_imageCache.TryGetValue(topic, out var staleCached))
					{
						LogReturningStaleCachedImages(topic);
						return new List<WallpaperImage>(staleCached.images);
					}
					return new List<WallpaperImage>();
				}
				else
				{
					// Cool-down expired — clear so the breaker can re-trip if failures resume
					_coolDownUntil = null;
					_consecutiveFailures = 0;
				}
			}
		}

		try
		{
			LogFetchingImagesForTopic(topic);

			var url = $"{ApiBaseUrl}/{topic}";
			var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

			CheckRateLimit(response);

			response.EnsureSuccessStatusCode();

			var items = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.ListGitHubContentItem, cancellationToken).ConfigureAwait(false);
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
				ResetCircuitBreaker();
			}

			LogFetchedImages(images.Count, topic);
			return images;
		}
		catch (HttpRequestException ex)
		{
			LogFailedToFetchImages(ex, topic);
			RecordFailure();

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

	// Must be called inside _cacheLock
	private void ResetCircuitBreaker()
	{
		if (_consecutiveFailures > 0)
		{
			LogGitHubApiCircuitBreakerReset(_consecutiveFailures);
		}
		_consecutiveFailures = 0;
		_coolDownUntil = null;
	}

	private void RecordFailure()
	{
		lock (_cacheLock)
		{
			_consecutiveFailures++;
			if (_consecutiveFailures >= MaxConsecutiveFailures)
			{
				_coolDownUntil = DateTimeOffset.UtcNow + CoolDownDuration;
				LogGitHubApiEnteringCoolDown(_consecutiveFailures, _coolDownUntil.Value);
			}
		}
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

	[LoggerMessage(EventId = 2015, Level = LogLevel.Warning, Message = "GitHub API in cool-down until {CoolDownUntil} — skipping API call")]
	partial void LogGitHubApiInCoolDown(DateTimeOffset coolDownUntil);

	[LoggerMessage(EventId = 2016, Level = LogLevel.Warning, Message = "GitHub API entering cool-down after {FailureCount} consecutive failures. Will retry after {CoolDownUntil}")]
	partial void LogGitHubApiEnteringCoolDown(int failureCount, DateTimeOffset coolDownUntil);

	[LoggerMessage(EventId = 2017, Level = LogLevel.Information, Message = "GitHub API circuit breaker reset after {FailureCount} recorded failures")]
	partial void LogGitHubApiCircuitBreakerReset(int failureCount);

	[LoggerMessage(EventId = 2018, Level = LogLevel.Debug, Message = "Filtered to {Count} topics after excluding {ExcludedCount}")]
	partial void LogFilteredTopics(int count, int excludedCount);
}
