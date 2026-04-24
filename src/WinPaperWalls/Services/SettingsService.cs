using System.Text.Json;
using WinPaperWalls.Models;

namespace WinPaperWalls.Services;

internal sealed class SettingsService : ISettingsService
{
	private readonly string _settingsPath;
	private readonly object _lock = new();

	public event EventHandler? SettingsChanged;

	public SettingsService()
	{
		var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		var appFolder = Path.Combine(localAppData, "WinPaperWalls");

		Directory.CreateDirectory(appFolder);
		_settingsPath = Path.Combine(appFolder, "settings.json");
	}

	public AppSettings LoadSettings()
	{
		lock (_lock)
		{
			if (!File.Exists(_settingsPath))
			{
				// Create default settings
				var defaultSettings = new AppSettings();
				SaveSettingsInternal(defaultSettings);
				return defaultSettings;
			}

			try
			{
				var json = File.ReadAllText(_settingsPath);
				return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
			}
			catch
			{
				// If deserialization fails, return default settings
				return new AppSettings();
			}
		}
	}

	public void SaveSettings(AppSettings settings)
	{
		lock (_lock)
		{
			SaveSettingsInternal(settings);
			SettingsChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private void SaveSettingsInternal(AppSettings settings)
	{
		var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
		{
			WriteIndented = true
		});
		File.WriteAllText(_settingsPath, json);
	}
}
