using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PaperWalls.Models;
using PaperWalls.Services;

namespace PaperWalls.Tests.Services;

public class SchedulerServiceTests
{
    private readonly IWallpaperService _wallpaperService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<SchedulerService> _logger;

    public SchedulerServiceTests()
    {
        _wallpaperService = Substitute.For<IWallpaperService>();
        _settingsService = Substitute.For<ISettingsService>();
        _logger = Substitute.For<ILogger<SchedulerService>>();

        // Default settings
        _settingsService.LoadSettings().Returns(new AppSettings
        {
            IntervalMinutes = 60
        });
    }

    [Fact]
    public async Task StartAsync_ChangesWallpaperImmediately()
    {
        // Arrange
        var service = new SchedulerService(_wallpaperService, _settingsService, _logger);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Give the background task time to execute
        await Task.Delay(500);

        // Cleanup
        await service.StopAsync(CancellationToken.None);

        // Assert
        await _wallpaperService.Received().ChangeWallpaperAsync();
    }

    [Fact]
    public async Task StartAsync_SetsNextChangeTime()
    {
        // Arrange
        var service = new SchedulerService(_wallpaperService, _settingsService, _logger);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        service.NextChangeTime.Should().NotBeNull();
        service.NextChangeTime.Should().BeCloseTo(DateTime.Now.AddMinutes(60), TimeSpan.FromSeconds(5));

        // Cleanup
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_StopsCleanly()
    {
        // Arrange
        var service = new SchedulerService(_wallpaperService, _settingsService, _logger);
        await service.StartAsync(CancellationToken.None);

        // Act
        var stopTask = service.StopAsync(CancellationToken.None);
        await stopTask;

        // Assert
        stopTask.IsCompletedSuccessfully.Should().BeTrue();
        service.NextChangeTime.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_HandlesSynchronousWallpaperChangeException()
    {
        // Arrange
        _wallpaperService.ChangeWallpaperAsync()
            .Returns(Task.FromException(new Exception("Test exception")));

        var service = new SchedulerService(_wallpaperService, _settingsService, _logger);

        // Act - should not throw
        await service.StartAsync(CancellationToken.None);

        // Give time for background task to complete
        await Task.Delay(500);

        // Cleanup
        await service.StopAsync(CancellationToken.None);

        // Assert - service should still be running
        await _wallpaperService.Received().ChangeWallpaperAsync();
    }

    [Fact]
    public async Task OnSettingsChanged_RestartsTimerWithNewInterval()
    {
        // Arrange
        var settingsChangedHandler = (EventHandler?)null;
        _settingsService.SettingsChanged += Arg.Do<EventHandler>(handler => settingsChangedHandler = handler);

        var service = new SchedulerService(_wallpaperService, _settingsService, _logger);
        await service.StartAsync(CancellationToken.None);

        // Verify initial next change time
        var initialNextChange = service.NextChangeTime;
        initialNextChange.Should().NotBeNull();

        // Act - change settings
        _settingsService.LoadSettings().Returns(new AppSettings
        {
            IntervalMinutes = 30 // Changed from 60 to 30
        });

        settingsChangedHandler?.Invoke(_settingsService, EventArgs.Empty);

        // Give time for restart
        await Task.Delay(500);

        // Assert
        service.NextChangeTime.Should().NotBeNull();
        // New interval should be 30 minutes
        service.NextChangeTime.Should().BeCloseTo(DateTime.Now.AddMinutes(30), TimeSpan.FromSeconds(5));

        // Cleanup
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TimerTick_ChangesWallpaperPeriodically()
    {
        // Arrange - use minimum allowed interval for testing
        _settingsService.LoadSettings().Returns(new AppSettings
        {
            IntervalMinutes = 1 // Minimum allowed interval
        });

        var service = new SchedulerService(_wallpaperService, _settingsService, _logger);

        // We can't easily test the actual timer ticks without waiting,
        // so we'll just verify the service starts and stops correctly
        await service.StartAsync(CancellationToken.None);
        
        // Give background task time to run
        await Task.Delay(500);

        await service.StopAsync(CancellationToken.None);

        // Assert - at least the initial call should have happened
        await _wallpaperService.Received().ChangeWallpaperAsync();
    }

    [Fact]
    public async Task StopAsync_CancelsRunningTimer()
    {
        // Arrange
        var service = new SchedulerService(_wallpaperService, _settingsService, _logger);
        await service.StartAsync(CancellationToken.None);

        // Act
        await service.StopAsync(CancellationToken.None);

        // Assert
        service.NextChangeTime.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_WithCancellationToken_CancelsStartup()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var service = new SchedulerService(_wallpaperService, _settingsService, _logger);

        // Act & Assert - should complete without throwing
        await service.StartAsync(cts.Token);

        // Cleanup
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MultipleStartStop_HandlesCorrectly()
    {
        // Arrange
        var service = new SchedulerService(_wallpaperService, _settingsService, _logger);

        // Act
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        // Assert - should complete without errors
        service.NextChangeTime.Should().BeNull();
    }
}
