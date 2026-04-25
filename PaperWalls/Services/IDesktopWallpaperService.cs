using PaperWalls.Interop;

namespace PaperWalls.Services;

public interface IDesktopWallpaperService
{
	void SetWallpaper(string filePath, WallpaperStyle style);
	string? GetCurrentWallpaperPath();
}
