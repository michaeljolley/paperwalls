using WinPaperWalls.Interop;

namespace WinPaperWalls.Services;

public class DesktopWallpaperService : IDesktopWallpaperService
{
    public void SetWallpaper(string filePath, WallpaperStyle style)
    {
        DesktopWallpaper.SetWallpaper(filePath, style);
    }

    public string? GetCurrentWallpaperPath()
    {
        return DesktopWallpaper.GetCurrentWallpaperPath();
    }
}
