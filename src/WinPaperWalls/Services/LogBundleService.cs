using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace WinPaperWalls.Services;

internal sealed class LogBundleService : ILogBundleService
{
	private readonly ILogger<LogBundleService> _logger;
	private readonly string _logsDirectory;

	public LogBundleService(ILogger<LogBundleService> logger)
	{
		_logger = logger;
		var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		_logsDirectory = Path.Combine(localAppData, "WinPaperWalls", "logs");
	}

	public Task<string> CreateBugReportAsync()
	{
		return Task.Run(() =>
		{
			var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
			var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
			var zipPath = Path.Combine(desktopPath, $"BugReport-{timestamp}.zip");

			if (!Directory.Exists(_logsDirectory))
			{
				_logger.LogWarning("Logs directory does not exist: {LogsDirectory}", _logsDirectory);
				Directory.CreateDirectory(_logsDirectory);
			}

			var logFiles = Directory.GetFiles(_logsDirectory, "*.log");
			if (logFiles.Length == 0)
			{
				_logger.LogWarning("No log files found in {LogsDirectory}", _logsDirectory);
			}

			using var zipStream = new FileStream(zipPath, FileMode.Create);
			using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

			foreach (var logFile in logFiles)
			{
				try
				{
					var entryName = Path.GetFileName(logFile);
					var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

					using var entryStream = entry.Open();
					// Open with FileShare.ReadWrite since Serilog may still be writing
					using var fileStream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
					fileStream.CopyTo(entryStream);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Failed to include log file in bug report: {LogFile}", logFile);
				}
			}

			_logger.LogInformation("Bug report created at {ZipPath}", zipPath);
			return zipPath;
		});
	}
}
