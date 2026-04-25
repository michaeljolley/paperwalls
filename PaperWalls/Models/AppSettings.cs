namespace PaperWalls.Models;

public class AppSettings
{
	public int IntervalMinutes { get; set; } = 1440; // daily
	public List<string> ExcludedTopics { get; set; } = new();
	public string WallpaperStyle { get; set; } = "Fill";
	public int CacheMaxMB { get; set; } = 500;
	public bool StartWithWindows { get; set; } = true;
}
