using FluentAssertions;
using WinPaperWalls.Models;
using WinPaperWalls.Services;

namespace WinPaperWalls.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _testSettingsPath;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        // Use a test-specific directory
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _testSettingsPath = Path.Combine(localAppData, "WinPaperWalls_Test_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testSettingsPath);

        // Set environment for test
        _service = new SettingsService();
    }

    public void Dispose()
    {
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
            "WinPaperWalls",
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
}
