using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace PaperWalls.Services;

internal sealed partial class LogBundleService : ILogBundleService
{
	private readonly ILogger<LogBundleService> _logger;
	private readonly string _logsDirectory;

	public LogBundleService(ILogger<LogBundleService> logger)
	{
		_logger = logger;
		var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		_logsDirectory = Path.Combine(localAppData, "PaperWalls", "logs");
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
				LogLogsDirectoryDoesNotExist(_logsDirectory);
				Directory.CreateDirectory(_logsDirectory);
			}

			var logFiles = Directory.GetFiles(_logsDirectory, "*.log");
			if (logFiles.Length == 0)
			{
				LogNoLogFilesFound(_logsDirectory);
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
					LogFailedToIncludeLogFile(ex, logFile);
				}
			}

			LogBugReportCreated(zipPath);
			return zipPath;
		});
	}

	// LoggerMessage source-generated methods for Native AOT compatibility
	[LoggerMessage(EventId = 6000, Level = LogLevel.Warning, Message = "Logs directory does not exist: {LogsDirectory}")]
	private partial void LogLogsDirectoryDoesNotExist(string logsDirectory);

	[LoggerMessage(EventId = 6001, Level = LogLevel.Warning, Message = "No log files found in {LogsDirectory}")]
	private partial void LogNoLogFilesFound(string logsDirectory);

	[LoggerMessage(EventId = 6002, Level = LogLevel.Warning, Message = "Failed to include log file in bug report: {LogFile}")]
	private partial void LogFailedToIncludeLogFile(Exception ex, string logFile);

	[LoggerMessage(EventId = 6003, Level = LogLevel.Information, Message = "Bug report created at {ZipPath}")]
	private partial void LogBugReportCreated(string zipPath);
}
