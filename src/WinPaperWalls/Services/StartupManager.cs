using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Reflection;

namespace WinPaperWalls.Services;

internal sealed class StartupManager
{
	private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string AppName = "WinPaperWalls";

	private readonly ILogger<StartupManager> _logger;

	public StartupManager(ILogger<StartupManager> logger)
	{
		_logger = logger;
	}

	public void SetStartWithWindows(bool enabled)
	{
		try
		{
			using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
			if (key == null)
			{
				_logger.LogError("Failed to open registry key for startup configuration");
				throw new InvalidOperationException("Cannot access Windows startup registry key");
			}

			if (enabled)
			{
				var exePath = GetExecutablePath();
				key.SetValue(AppName, $"\"{exePath}\"");
				_logger.LogInformation("Added application to Windows startup: {ExePath}", exePath);
			}
			else
			{
				key.DeleteValue(AppName, throwOnMissingValue: false);
				_logger.LogInformation("Removed application from Windows startup");
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to configure Windows startup");
			throw;
		}
	}

	public bool IsStartWithWindows()
	{
		try
		{
			using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
			if (key == null)
			{
				return false;
			}

			var value = key.GetValue(AppName);
			return value != null;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to check Windows startup status");
			return false;
		}
	}

	private static string GetExecutablePath()
	{
		// Get the path to the currently executing assembly
		var assembly = Assembly.GetExecutingAssembly();
		var location = assembly.Location;

		// For .NET applications, we need the actual .exe path
		// Location might be .dll, so we look for the .exe
		if (location.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
		{
			location = location.Substring(0, location.Length - 4) + ".exe";
		}

		return location;
	}
}
