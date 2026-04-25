using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PaperWalls.Services;

internal sealed partial class SchedulerService : ISchedulerService, IHostedService
{
	private readonly IWallpaperService _wallpaperService;
	private readonly ISettingsService _settingsService;
	private readonly ILogger<SchedulerService> _logger;

	private PeriodicTimer? _timer;
	private Task? _timerTask;
	private CancellationTokenSource? _cts;
	private readonly object _timerLock = new();

	public DateTime? NextChangeTime { get; private set; }

	public SchedulerService(
		IWallpaperService wallpaperService,
		ISettingsService settingsService,
		ILogger<SchedulerService> logger)
	{
		_wallpaperService = wallpaperService;
		_settingsService = settingsService;
		_logger = logger;

		_settingsService.SettingsChanged += OnSettingsChanged;
	}

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		LogSchedulerServiceStarting();

		var settings = _settingsService.LoadSettings();
		var intervalMinutes = Math.Max(1, settings.IntervalMinutes);

		lock (_timerLock)
		{
			_cts = new CancellationTokenSource();
			_timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
			NextChangeTime = DateTime.Now.AddMinutes(intervalMinutes);

			_timerTask = RunTimerAsync(_cts.Token);
		}

		// Change wallpaper immediately on first start
		_ = Task.Run(async () =>
		{
			try
			{
				await _wallpaperService.ChangeWallpaperAsync();
			}
			catch (Exception ex)
			{
				LogFailedToChangeWallpaperOnStartup(ex);
			}
		}, cancellationToken);

		LogSchedulerStarted(intervalMinutes);
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		LogSchedulerServiceStopping();

		lock (_timerLock)
		{
			_cts?.Cancel();
			_timer?.Dispose();
			_timer = null;
			NextChangeTime = null;
		}

		if (_timerTask != null)
		{
			try
			{
				await _timerTask;
			}
			catch (OperationCanceledException)
			{
				// Expected when canceling
			}
			catch (Exception ex)
			{
				LogErrorDuringSchedulerShutdown(ex);
			}
		}

		_cts?.Dispose();
		_cts = null;
		_timerTask = null;

		LogSchedulerServiceStopped();
	}

	private async Task RunTimerAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (await _timer!.WaitForNextTickAsync(cancellationToken))
			{
				try
				{
					LogTimerTick();
					await _wallpaperService.ChangeWallpaperAsync();

					// Update next change time
					var settings = _settingsService.LoadSettings();
					NextChangeTime = DateTime.Now.AddMinutes(settings.IntervalMinutes);
				}
				catch (Exception ex)
				{
					LogErrorDuringScheduledWallpaperChange(ex);
					// Continue running - don't crash the service
				}
			}
		}
		catch (OperationCanceledException)
		{
			LogTimerTaskCancelled();
		}
	}

	private async void OnSettingsChanged(object? sender, EventArgs e)
	{
		try
		{
			var settings = _settingsService.LoadSettings();
			var newIntervalMinutes = Math.Max(1, settings.IntervalMinutes);

			LogSettingsChangedRestartingTimer(newIntervalMinutes);

			// Stop current timer
			lock (_timerLock)
			{
				_cts?.Cancel();
				_timer?.Dispose();
			}

			// Wait for timer task to finish
			if (_timerTask != null)
			{
				try
				{
					await _timerTask;
				}
				catch (OperationCanceledException)
				{
					// Expected
				}
			}

			// Start new timer with new interval
			lock (_timerLock)
			{
				_cts?.Dispose();
				_cts = new CancellationTokenSource();
				_timer = new PeriodicTimer(TimeSpan.FromMinutes(newIntervalMinutes));
				NextChangeTime = DateTime.Now.AddMinutes(newIntervalMinutes);

				_timerTask = RunTimerAsync(_cts.Token);
			}

			LogTimerRestartedSuccessfully();
		}
		catch (Exception ex)
		{
			LogErrorRestartingTimerAfterSettingsChange(ex);
		}
	}

	// LoggerMessage source-generated methods for Native AOT compatibility
	[LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Scheduler service starting")]
	partial void LogSchedulerServiceStarting();

	[LoggerMessage(EventId = 3001, Level = LogLevel.Error, Message = "Failed to change wallpaper on startup")]
	partial void LogFailedToChangeWallpaperOnStartup(Exception ex);

	[LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Scheduler started with interval of {IntervalMinutes} minutes")]
	partial void LogSchedulerStarted(int intervalMinutes);

	[LoggerMessage(EventId = 3003, Level = LogLevel.Information, Message = "Scheduler service stopping")]
	partial void LogSchedulerServiceStopping();

	[LoggerMessage(EventId = 3004, Level = LogLevel.Error, Message = "Error during scheduler shutdown")]
	partial void LogErrorDuringSchedulerShutdown(Exception ex);

	[LoggerMessage(EventId = 3005, Level = LogLevel.Information, Message = "Scheduler service stopped")]
	partial void LogSchedulerServiceStopped();

	[LoggerMessage(EventId = 3006, Level = LogLevel.Information, Message = "Timer tick - changing wallpaper")]
	partial void LogTimerTick();

	[LoggerMessage(EventId = 3007, Level = LogLevel.Error, Message = "Error during scheduled wallpaper change")]
	partial void LogErrorDuringScheduledWallpaperChange(Exception ex);

	[LoggerMessage(EventId = 3008, Level = LogLevel.Debug, Message = "Timer task cancelled")]
	partial void LogTimerTaskCancelled();

	[LoggerMessage(EventId = 3009, Level = LogLevel.Information, Message = "Settings changed - restarting timer with interval of {IntervalMinutes} minutes")]
	partial void LogSettingsChangedRestartingTimer(int intervalMinutes);

	[LoggerMessage(EventId = 3010, Level = LogLevel.Information, Message = "Timer restarted successfully")]
	partial void LogTimerRestartedSuccessfully();

	[LoggerMessage(EventId = 3011, Level = LogLevel.Error, Message = "Error restarting timer after settings change")]
	partial void LogErrorRestartingTimerAfterSettingsChange(Exception ex);
}
