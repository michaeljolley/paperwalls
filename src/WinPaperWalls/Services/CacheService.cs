using Microsoft.Extensions.Logging;

namespace WinPaperWalls.Services;

internal sealed class CacheService : ICacheService
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
			_cacheDirectory = Path.Combine(localAppData, "WinPaperWalls", "cache");
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
			_logger.LogDebug("Image {FileName} already cached", fileName);

			// Update last access time for LRU tracking
			File.SetLastAccessTime(filePath, DateTime.UtcNow);
			return filePath;
		}

		_logger.LogInformation("Downloading image {FileName} from {Url}", fileName, url);

		try
		{
			var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
			response.EnsureSuccessStatusCode();

			var imageBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

			lock (_cacheLock)
			{
				// Double-check in case another thread downloaded it
				if (!File.Exists(filePath))
				{
					File.WriteAllBytes(filePath, imageBytes);
					_logger.LogInformation("Downloaded and cached {FileName} ({Size} bytes)", fileName, imageBytes.Length);
				}
			}

			return filePath;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to download image {FileName} from {Url}", fileName, url);
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
			_logger.LogError(ex, "Failed to calculate cache size");
			return 0;
		}
	}

	public async Task EvictOldestAsync(long targetBytes)
	{
		_logger.LogInformation("Starting cache eviction to reach target size of {TargetMB} MB", targetBytes / 1024 / 1024);

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

						_logger.LogDebug("Evicted {FileName} ({Size} bytes)", file.Name, fileSize);
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Failed to delete cached file {FileName}", file.Name);
					}
				}

				_logger.LogInformation("Cache eviction complete: deleted {Count} files, freed {FreedMB} MB",
					deletedCount, freedBytes / 1024 / 1024);
			}
		});
	}

	public async Task ClearCacheAsync()
	{
		_logger.LogInformation("Clearing all cached images");

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
							_logger.LogError(ex, "Failed to delete file {FileName}", Path.GetFileName(file));
						}
					}

					_logger.LogInformation("Cleared {Count} cached images", files.Length);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to clear cache");
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
			_logger.LogInformation("Created cache directory at {Path}", _cacheDirectory);
		}
	}
}
