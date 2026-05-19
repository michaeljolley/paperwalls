using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace PaperWalls.Services;

internal sealed partial class ETagCacheService : IETagCacheService
{
	private static readonly string CacheFilePath = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"PaperWalls", "etag-cache.json");

	private readonly ILogger<ETagCacheService> _logger;
	private Dictionary<string, string> _etags = new();

	public ETagCacheService(ILogger<ETagCacheService> logger)
	{
		_logger = logger;
	}

	public string? GetETag(string key)
	{
		_etags.TryGetValue(key, out var value);
		return value;
	}

	public void SetETag(string key, string etag) => _etags[key] = etag;

	public void Load()
	{
		if (!File.Exists(CacheFilePath))
			return;

		try
		{
			var json = File.ReadAllText(CacheFilePath);
			var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
			if (loaded != null)
				_etags = loaded;
		}
		catch (Exception ex)
		{
			LogLoadFailed(ex);
			_etags = new Dictionary<string, string>();
		}
	}

	public void Save()
	{
		try
		{
			var dir = Path.GetDirectoryName(CacheFilePath)!;
			Directory.CreateDirectory(dir);
			var json = JsonSerializer.Serialize(_etags);
			File.WriteAllText(CacheFilePath, json);
		}
		catch (Exception ex)
		{
			LogSaveFailed(ex);
		}
	}

	[LoggerMessage(EventId = 3000, Level = LogLevel.Warning, Message = "Failed to load ETag cache from disk; starting fresh")]
	partial void LogLoadFailed(Exception ex);

	[LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Failed to save ETag cache to disk")]
	partial void LogSaveFailed(Exception ex);
}
