using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Reflection;

namespace PaperWalls.Services;

public sealed partial class StartupManager
{
	private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string AppName = "PaperWalls";

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
				LogFailedToOpenRegistryKey();
				throw new InvalidOperationException("Cannot access Windows startup registry key");
			}

			if (enabled)
			{
				var exePath = GetExecutablePath();
				key.SetValue(AppName, $"\"{exePath}\"");
				LogAddedToWindowsStartup(exePath);
			}
			else
			{
				key.DeleteValue(AppName, throwOnMissingValue: false);
				LogRemovedFromWindowsStartup();
			}
		}
		catch (Exception ex)
		{
			LogFailedToConfigureWindowsStartup(ex);
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
			LogFailedToCheckWindowsStartupStatus(ex);
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

	// LoggerMessage source-generated methods for Native AOT compatibility
	[LoggerMessage(EventId = 5000, Level = LogLevel.Error, Message = "Failed to open registry key for startup configuration")]
	private partial void LogFailedToOpenRegistryKey();

	[LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Added application to Windows startup: {ExePath}")]
	private partial void LogAddedToWindowsStartup(string exePath);

	[LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Removed application from Windows startup")]
	private partial void LogRemovedFromWindowsStartup();

	[LoggerMessage(EventId = 5003, Level = LogLevel.Error, Message = "Failed to configure Windows startup")]
	private partial void LogFailedToConfigureWindowsStartup(Exception ex);

	[LoggerMessage(EventId = 5004, Level = LogLevel.Error, Message = "Failed to check Windows startup status")]
	private partial void LogFailedToCheckWindowsStartupStatus(Exception ex);
}
