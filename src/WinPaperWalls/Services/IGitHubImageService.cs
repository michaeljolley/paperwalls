using WinPaperWalls.Models;

namespace WinPaperWalls.Services;

public interface IGitHubImageService
{
    /// <summary>
    /// Gets the list of available wallpaper topics from the GitHub repository.
    /// </summary>
    /// <returns>List of topic names, filtered by excluded topics from settings.</returns>
    Task<List<string>> GetTopicsAsync();

    /// <summary>
    /// Gets the list of images in a specific topic.
    /// </summary>
    /// <param name="topic">The topic folder name.</param>
    /// <returns>List of wallpaper images with download URLs.</returns>
    Task<List<WallpaperImage>> GetImagesAsync(string topic);
}
