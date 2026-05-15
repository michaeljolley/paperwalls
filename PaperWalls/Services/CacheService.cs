using Microsoft.Extensions.Logging;

namespace PaperWalls.Services;

internal sealed partial class CacheService : ICacheService
{
	private readonly string _cacheDirectory;
	private readonly HttpClient _httpClient;
	private readonly ILogger<CacheService> _logger;
	private readonly object _cacheLock = new();

	public CacheService(IHttpClientFactory httpClientFactory, ILogger<CacheService> logger, string? cacheDirectory = null)
	{
		if (cacheDirectory != null)
		{
			_cacheDirectory = cacheDirectory;
		}
		else
		{
			var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			_cacheDirectory = Path.Combine(localAppData, "PaperWalls", "cache");
		}

		_httpClient = httpClientFactory.CreateClient();
		_logger = logger;

		EnsureCacheDirectoryExists();
	}

	public async Task<string> DownloadImageAsync(string url, string fileName)
	{
		var filePath = Path.Combine(_cacheDirectory, fileName);

		// If already cached, return immediately
		if (File.Exists(filePath))
		{
			LogImageAlreadyCached(fileName);

			// Update last access time for LRU tracking
			File.SetLastAccessTime(filePath, DateTime.UtcNow);
			return filePath;
		}

		LogDownloadingImage(fileName, url);

		try
		{
			var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
			response.EnsureSuccessStatusCode();

			var imageBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

			if (!IsValidImageBytes(imageBytes))
			{
				var magicBytes = imageBytes.Length >= 8
					? BitConverter.ToString(imageBytes, 0, 8)
					: BitConverter.ToString(imageBytes);
				var statusCode = (int)response.StatusCode;
				var contentType = response.Content.Headers.ContentType?.ToString() ?? "(none)";
				var contentLength = response.Content.Headers.ContentLength?.ToString() ?? "(none)";
				var rateLimitRemaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var rlValues)
					? string.Join(",", rlValues)
					: "(none)";
				LogInvalidImageDownload(fileName, url, statusCode, contentType, contentLength, imageBytes.Length, rateLimitRemaining, magicBytes);
				throw new InvalidOperationException($"Downloaded content for '{fileName}' is not a valid image (status: {statusCode}, content-type: {contentType}, magic bytes: {magicBytes}).");
			}

			lock (_cacheLock)
			{
				// Double-check in case another thread downloaded it
				if (!File.Exists(filePath))
				{
					File.WriteAllBytes(filePath, imageBytes);
					LogDownloadedAndCached(fileName, imageBytes.Length);
				}
			}

			return filePath;
		}
		catch (Exception ex)
		{
			LogFailedToDownloadImage(ex, fileName, url);
			throw;
		}
	}

	public string? GetCachedImagePath(string fileName)
	{
		var filePath = Path.Combine(_cacheDirectory, fileName);

		if (File.Exists(filePath))
		{
			// Update last access time
			File.SetLastAccessTime(filePath, DateTime.UtcNow);
			return filePath;
		}

		return null;
	}

	public long GetCacheSizeBytes()
	{
		if (!Directory.Exists(_cacheDirectory))
		{
			return 0;
		}

		try
		{
			var files = Directory.GetFiles(_cacheDirectory);
			return files.Sum(f => new FileInfo(f).Length);
		}
		catch (Exception ex)
		{
			LogFailedToCalculateCacheSize(ex);
			return 0;
		}
	}

	public async Task EvictOldestAsync(long targetBytes)
	{
		LogStartingCacheEviction(targetBytes / 1024 / 1024);

		if (!Directory.Exists(_cacheDirectory))
		{
			return;
		}

		await Task.Run(() =>
		{
			lock (_cacheLock)
			{
				var files = Directory.GetFiles(_cacheDirectory)
					.Select(f => new FileInfo(f))
					.OrderBy(fi => fi.LastAccessTime)
					.ToList();

				var currentSize = files.Sum(f => f.Length);
				var deletedCount = 0;
				var freedBytes = 0L;

				foreach (var file in files)
				{
					if (currentSize <= targetBytes)
					{
						break;
					}

					try
					{
						var fileSize = file.Length;
						file.Delete();
						currentSize -= fileSize;
						freedBytes += fileSize;
						deletedCount++;

						LogEvictedFile(file.Name, fileSize);
					}
					catch (Exception ex)
					{
						LogFailedToDeleteCachedFile(ex, file.Name);
					}
				}

				LogCacheEvictionComplete(deletedCount, freedBytes / 1024 / 1024);
			}
		});
	}

	public async Task ClearCacheAsync()
	{
		LogClearingAllCachedImages();

		if (!Directory.Exists(_cacheDirectory))
		{
			return;
		}

		await Task.Run(() =>
		{
			lock (_cacheLock)
			{
				try
				{
					var files = Directory.GetFiles(_cacheDirectory);
					foreach (var file in files)
					{
						try
						{
							File.Delete(file);
						}
						catch (Exception ex)
						{
							LogFailedToDeleteFile(ex, Path.GetFileName(file));
						}
					}

					LogClearedCachedImages(files.Length);
				}
				catch (Exception ex)
				{
					LogFailedToClearCache(ex);
					throw;
				}
			}
		});
	}

	private void EnsureCacheDirectoryExists()
	{
		if (!Directory.Exists(_cacheDirectory))
		{
			Directory.CreateDirectory(_cacheDirectory);
			LogCreatedCacheDirectory(_cacheDirectory);
		}
	}

	// LoggerMessage source-generated methods for Native AOT compatibility
	[LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Image {FileName} already cached")]
	partial void LogImageAlreadyCached(string fileName);

	[LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Downloading image {FileName} from {Url}")]
	partial void LogDownloadingImage(string fileName, string url);

	[LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Downloaded and cached {FileName} ({Size} bytes)")]
	partial void LogDownloadedAndCached(string fileName, long size);

	[LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "Failed to download image {FileName} from {Url}")]
	partial void LogFailedToDownloadImage(Exception ex, string fileName, string url);

	[LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "Failed to calculate cache size")]
	partial void LogFailedToCalculateCacheSize(Exception ex);

	[LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Starting cache eviction to reach target size of {TargetMB} MB")]
	partial void LogStartingCacheEviction(long targetMB);

	[LoggerMessage(EventId = 1006, Level = LogLevel.Debug, Message = "Evicted {FileName} ({Size} bytes)")]
	partial void LogEvictedFile(string fileName, long size);

	[LoggerMessage(EventId = 1007, Level = LogLevel.Error, Message = "Failed to delete cached file {FileName}")]
	partial void LogFailedToDeleteCachedFile(Exception ex, string fileName);

	[LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "Cache eviction complete: deleted {Count} files, freed {FreedMB} MB")]
	partial void LogCacheEvictionComplete(int count, long freedMB);

	[LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "Clearing all cached images")]
	partial void LogClearingAllCachedImages();

	[LoggerMessage(EventId = 1010, Level = LogLevel.Error, Message = "Failed to delete file {FileName}")]
	partial void LogFailedToDeleteFile(Exception ex, string fileName);

	[LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "Cleared {Count} cached images")]
	partial void LogClearedCachedImages(int count);

	[LoggerMessage(EventId = 1012, Level = LogLevel.Error, Message = "Failed to clear cache")]
	partial void LogFailedToClearCache(Exception ex);

	[LoggerMessage(EventId = 1013, Level = LogLevel.Information, Message = "Created cache directory at {Path}")]
	partial void LogCreatedCacheDirectory(string path);

	private static bool IsValidImageBytes(byte[] bytes)
	{
		if (bytes.Length < 4)
			return false;

		// JPEG: FF D8 FF
		if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
			return true;

		// PNG: 89 50 4E 47
		if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
			return true;

		// BMP: 42 4D
		if (bytes[0] == 0x42 && bytes[1] == 0x4D)
			return true;

		// WEBP: RIFF....WEBP (bytes 0-3 = 52 49 46 46, bytes 8-11 = 57 45 42 50)
		if (bytes.Length >= 12 &&
			bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
			bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
			return true;

		return false;
	}

	[LoggerMessage(EventId = 1015, Level = LogLevel.Warning, Message = "Downloaded content for {FileName} from {Url} is not a valid image. Status: {StatusCode}, Content-Type: {ContentType}, Content-Length: {ContentLength}, Bytes received: {BytesReceived}, X-RateLimit-Remaining: {RateLimitRemaining}, Magic bytes: {MagicBytes}")]
	partial void LogInvalidImageDownload(string fileName, string url, int statusCode, string contentType, string contentLength, int bytesReceived, string rateLimitRemaining, string magicBytes);
}
