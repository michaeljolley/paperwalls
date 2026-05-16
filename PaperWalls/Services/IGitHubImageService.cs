using PaperWalls.Models;

namespace PaperWalls.Services;

public interface IGitHubImageService
{
	/// <summary>
	/// Gets the list of available wallpaper topics from the GitHub repository,
	/// filtered by excluded topics from settings. Used by WallpaperService.
	/// </summary>
	/// <returns>List of topic names, filtered by excluded topics from settings.</returns>
	Task<List<string>> GetTopicsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the complete list of available wallpaper topics from the GitHub repository,
	/// without applying the excluded-topics filter. Used by SettingsViewModel so the
	/// settings page can display all topics and let the user manage which are excluded.
	/// </summary>
	Task<List<string>> GetAllTopicsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the list of images in a specific topic.
	/// </summary>
	/// <param name="topic">The topic folder name.</param>
	/// <returns>List of wallpaper images with download URLs.</returns>
	Task<List<WallpaperImage>> GetImagesAsync(string topic, CancellationToken cancellationToken = default);
}
