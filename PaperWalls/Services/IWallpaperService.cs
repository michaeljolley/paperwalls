namespace PaperWalls.Services;

public interface IWallpaperService
{
	/// <summary>
	/// Changes the wallpaper to a new random image from available topics.
	/// </summary>
	/// <param name="cancellationToken">Token that can interrupt the operation between retry attempts.</param>
	Task ChangeWallpaperAsync(CancellationToken cancellationToken = default);
}
