using WinPaperWalls.Interop;

namespace WinPaperWalls.Services;

public interface IDesktopWallpaperService
{
    void SetWallpaper(string filePath, WallpaperStyle style);
    string? GetCurrentWallpaperPath();
}
