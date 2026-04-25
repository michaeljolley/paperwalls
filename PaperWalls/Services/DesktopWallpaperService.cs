using PaperWalls.Interop;

namespace PaperWalls.Services;

internal sealed class DesktopWallpaperService : IDesktopWallpaperService
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
