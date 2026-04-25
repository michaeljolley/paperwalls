namespace PaperWalls.Services;

public interface IWallpaperService
{
	/// <summary>
	/// Changes the wallpaper to a new random image from available topics.
	/// </summary>
	Task ChangeWallpaperAsync();
}
