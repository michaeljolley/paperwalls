namespace PaperWalls.Services;

public interface IETagCacheService
{
	string? GetETag(string key);
	void SetETag(string key, string etag);
	void Load();
	void Save();
}
