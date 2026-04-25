using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace PaperWalls.Interop;

public enum WallpaperStyle
{
	Fill,
	Fit,
	Stretch,
	Tile,
	Center,
	Span
}

public static class DesktopWallpaper
{
	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

	private const int SPI_SETDESKWALLPAPER = 0x0014;
	private const int SPIF_UPDATEINIFILE = 0x01;
	private const int SPIF_SENDCHANGE = 0x02;

	public static void SetWallpaper(string filePath, WallpaperStyle style)
	{
		if (!File.Exists(filePath))
		{
			throw new FileNotFoundException($"Wallpaper file not found: {filePath}");
		}

		// Set wallpaper style in registry
		using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
		{
			if (key != null)
			{
				switch (style)
				{
					case WallpaperStyle.Fill:
						key.SetValue("WallpaperStyle", "10");
						key.SetValue("TileWallpaper", "0");
						break;
					case WallpaperStyle.Fit:
						key.SetValue("WallpaperStyle", "6");
						key.SetValue("TileWallpaper", "0");
						break;
					case WallpaperStyle.Stretch:
						key.SetValue("WallpaperStyle", "2");
						key.SetValue("TileWallpaper", "0");
						break;
					case WallpaperStyle.Tile:
						key.SetValue("WallpaperStyle", "0");
						key.SetValue("TileWallpaper", "1");
						break;
					case WallpaperStyle.Center:
						key.SetValue("WallpaperStyle", "0");
						key.SetValue("TileWallpaper", "0");
						break;
					case WallpaperStyle.Span:
						key.SetValue("WallpaperStyle", "22");
						key.SetValue("TileWallpaper", "0");
						break;
				}
			}
		}

		// Apply the wallpaper
		SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, filePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
	}

	public static string? GetCurrentWallpaperPath()
	{
		using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
		return key?.GetValue("Wallpaper") as string;
	}
}
