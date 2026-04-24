using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WinPaperWalls.Services;

internal sealed class SchedulerService : ISchedulerService, IHostedService
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
		_logger.LogInformation("Scheduler service starting");

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
				_logger.LogError(ex, "Failed to change wallpaper on startup");
			}
		}, cancellationToken);

		_logger.LogInformation("Scheduler started with interval of {IntervalMinutes} minutes", intervalMinutes);
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Scheduler service stopping");

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
				_logger.LogError(ex, "Error during scheduler shutdown");
			}
		}

		_cts?.Dispose();
		_cts = null;
		_timerTask = null;

		_logger.LogInformation("Scheduler service stopped");
	}

	private async Task RunTimerAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (await _timer!.WaitForNextTickAsync(cancellationToken))
			{
				try
				{
					_logger.LogInformation("Timer tick - changing wallpaper");
					await _wallpaperService.ChangeWallpaperAsync();

					// Update next change time
					var settings = _settingsService.LoadSettings();
					NextChangeTime = DateTime.Now.AddMinutes(settings.IntervalMinutes);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error during scheduled wallpaper change");
					// Continue running - don't crash the service
				}
			}
		}
		catch (OperationCanceledException)
		{
			_logger.LogDebug("Timer task cancelled");
		}
	}

	private async void OnSettingsChanged(object? sender, EventArgs e)
	{
		try
		{
			var settings = _settingsService.LoadSettings();
			var newIntervalMinutes = Math.Max(1, settings.IntervalMinutes);

			_logger.LogInformation("Settings changed - restarting timer with interval of {IntervalMinutes} minutes", newIntervalMinutes);

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

			_logger.LogInformation("Timer restarted successfully");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error restarting timer after settings change");
		}
	}
}
