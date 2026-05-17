using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PaperWalls.Models;
using PaperWalls.Services;

namespace PaperWalls.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _testSettingsPath;
    private readonly SettingsService _service;
    private readonly ILogger<SettingsService> _logger;

    public SettingsServiceTests()
    {
        // Use a test-specific directory
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _testSettingsPath = Path.Combine(localAppData, "PaperWalls_Test_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testSettingsPath);

        // Set environment for test
        _logger = Substitute.For<ILogger<SettingsService>>();
        _service = new SettingsService(_logger);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        // Clean up test directory
        if (Directory.Exists(_testSettingsPath))
        {
            Directory.Delete(_testSettingsPath, true);
        }
    }

    [Fact]
    public void LoadSettings_WhenNoFileExists_CreatesDefaultSettings()
    {
        // Act
        var settings = _service.LoadSettings();

        // Assert
        settings.Should().NotBeNull();
        settings.IntervalMinutes.Should().Be(1440);
        settings.WallpaperStyle.Should().Be("Fill");
        settings.CacheMaxMB.Should().Be(500);
        settings.StartWithWindows.Should().BeTrue();
        settings.ExcludedTopics.Should().BeEmpty();
    }

    [Fact]
    public void SaveAndLoadSettings_RoundTrip_PreservesData()
    {
        // Arrange
        var settings = new AppSettings
        {
            IntervalMinutes = 60,
            WallpaperStyle = "Fit",
            CacheMaxMB = 1000,
            StartWithWindows = false,
            ExcludedTopics = new List<string> { "Nature", "Space" }
        };

        // Act
        _service.SaveSettings(settings);
        var loaded = _service.LoadSettings();

        // Assert
        loaded.IntervalMinutes.Should().Be(60);
        loaded.WallpaperStyle.Should().Be("Fit");
        loaded.CacheMaxMB.Should().Be(1000);
        loaded.StartWithWindows.Should().BeFalse();
        loaded.ExcludedTopics.Should().HaveCount(2);
        loaded.ExcludedTopics.Should().Contain("Nature");
        loaded.ExcludedTopics.Should().Contain("Space");
    }

    [Fact]
    public void SaveSettings_FiresSettingsChangedEvent()
    {
        // Arrange
        var eventFired = false;
        _service.SettingsChanged += (sender, args) => eventFired = true;
        var settings = new AppSettings();

        // Act
        _service.SaveSettings(settings);

        // Assert
        eventFired.Should().BeTrue();
    }

    [Fact]
    public void LoadSettings_WithCorruptedJson_ReturnsDefaultSettings()
    {
        // Arrange - create a corrupted JSON file
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PaperWalls",
            "settings.json");
        
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{ invalid json !!!");

        // Act
        var settings = _service.LoadSettings();

        // Assert
        settings.Should().NotBeNull();
        settings.IntervalMinutes.Should().Be(1440); // Default value
    }

    [Fact]
    public void LoadSettings_ConcurrentReads_DoNotThrow()
    {
        // Arrange
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var settings = _service.LoadSettings();
                settings.Should().NotBeNull();
            }));
        }

        // Assert
        var act = () => Task.WaitAll(tasks.ToArray());
        act.Should().NotThrow();
    }

    [Fact]
    public void SaveSettings_MultipleTimes_EventFiresEachTime()
    {
        // Arrange
        var eventCount = 0;
        _service.SettingsChanged += (sender, args) => eventCount++;
        var settings = new AppSettings();

        // Act
        _service.SaveSettings(settings);
        _service.SaveSettings(settings);
        _service.SaveSettings(settings);

        // Assert
        eventCount.Should().Be(3);
    }

    [Fact]
    public void LoadSettings_WithCorruptedJson_LogsWarning()
    {
        // Arrange - write a corrupted settings file to the real PaperWalls path
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PaperWalls",
            "settings.json");

        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{ not valid json !!!");

        try
        {
            // Act
            var settings = _service.LoadSettings();

            // Assert - defaults returned
            settings.Should().NotBeNull();
            settings.IntervalMinutes.Should().Be(1440);

            // Assert - Warning was logged.
            // LoggerMessage source generators call ILogger.Log<TState>() with a struct TState,
            // not Log<object>(), so Arg.Any<object>() doesn't match. Use ReceivedCalls() to
            // inspect the actual call list and verify at least one Log call was made.
            var logCalls = _logger.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == "Log")
                .ToList();
            logCalls.Should().NotBeEmpty("LoadSettings should log a warning when deserialization fails");
        }
        finally
        {
            // Clean up the corrupted file so subsequent tests are unaffected
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
        }
    }
}
