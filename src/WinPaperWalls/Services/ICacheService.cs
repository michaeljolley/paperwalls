namespace WinPaperWalls.Services;

public interface ICacheService
{
    /// <summary>
    /// Downloads an image from the specified URL and caches it locally.
    /// </summary>
    /// <param name="url">The image download URL.</param>
    /// <param name="fileName">The filename to save as.</param>
    /// <returns>The local file path of the cached image.</returns>
    Task<string> DownloadImageAsync(string url, string fileName);

    /// <summary>
    /// Gets the cached file path if it exists.
    /// </summary>
    /// <param name="fileName">The filename to look up.</param>
    /// <returns>The local file path if cached, null otherwise.</returns>
    string? GetCachedImagePath(string fileName);

    /// <summary>
    /// Gets the total size of the cache directory in bytes.
    /// </summary>
    long GetCacheSizeBytes();

    /// <summary>
    /// Evicts oldest cached images until cache size is under target.
    /// </summary>
    /// <param name="targetBytes">Target size in bytes.</param>
    Task EvictOldestAsync(long targetBytes);

    /// <summary>
    /// Clears all cached images.
    /// </summary>
    Task ClearCacheAsync();
}
