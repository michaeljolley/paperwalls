using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaperWalls.Models;
using PaperWalls.Serialization;

namespace PaperWalls.Services;

internal sealed partial class SettingsService : ISettingsService
{
	private readonly string _settingsPath;
	private readonly object _lock = new();
	private readonly ILogger<SettingsService> _logger;

	public event EventHandler? SettingsChanged;

	public SettingsService(ILogger<SettingsService> logger)
	{
		_logger = logger;

		var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		var appFolder = Path.Combine(localAppData, "PaperWalls");

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
				return JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings) ?? new AppSettings();
			}
			catch (Exception ex)
			{
				LogFailedToLoadSettings(ex);
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
		var json = JsonSerializer.Serialize(settings, AppJsonContext.Default.AppSettings);
		File.WriteAllText(_settingsPath, json);
	}

	[LoggerMessage(EventId = 8000, Level = LogLevel.Warning, Message = "Failed to load settings, returning defaults")]
	partial void LogFailedToLoadSettings(Exception ex);
}
