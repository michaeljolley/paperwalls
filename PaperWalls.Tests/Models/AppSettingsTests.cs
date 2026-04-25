using System.Text.Json;
using FluentAssertions;
using PaperWalls.Models;

namespace PaperWalls.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert
        settings.IntervalMinutes.Should().Be(1440, "default is daily (24 hours)");
        settings.ExcludedTopics.Should().NotBeNull().And.BeEmpty();
        settings.WallpaperStyle.Should().Be("Fill");
        settings.CacheMaxMB.Should().Be(500);
        settings.StartWithWindows.Should().BeTrue();
    }

    [Fact]
    public void Serialization_RoundTrip_PreservesValues()
    {
        // Arrange
        var original = new AppSettings
        {
            IntervalMinutes = 120,
            ExcludedTopics = new List<string> { "Space", "Abstract", "Nature" },
            WallpaperStyle = "Fit",
            CacheMaxMB = 1000,
            StartWithWindows = false
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.IntervalMinutes.Should().Be(120);
        deserialized.ExcludedTopics.Should().BeEquivalentTo(new[] { "Space", "Abstract", "Nature" });
        deserialized.WallpaperStyle.Should().Be("Fit");
        deserialized.CacheMaxMB.Should().Be(1000);
        deserialized.StartWithWindows.Should().BeFalse();
    }

    [Fact]
    public void Serialization_WithIndentation_ProducesReadableJson()
    {
        // Arrange
        var settings = new AppSettings
        {
            IntervalMinutes = 60,
            ExcludedTopics = new List<string> { "Space" },
            WallpaperStyle = "Stretch",
            CacheMaxMB = 250,
            StartWithWindows = true
        };

        // Act
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        // Assert
        json.Should().Contain("IntervalMinutes");
        json.Should().Contain("ExcludedTopics");
        json.Should().Contain("WallpaperStyle");
        json.Should().Contain("CacheMaxMB");
        json.Should().Contain("StartWithWindows");
        json.Should().Contain("\n"); // Should have line breaks for indentation
    }

    [Fact]
    public void Deserialization_WithMissingProperties_UsesDefaults()
    {
        // Arrange
        var json = "{}";

        // Act
        var settings = JsonSerializer.Deserialize<AppSettings>(json);

        // Assert
        settings.Should().NotBeNull();
        settings!.IntervalMinutes.Should().Be(1440);
        settings.WallpaperStyle.Should().Be("Fill");
        settings.CacheMaxMB.Should().Be(500);
        settings.StartWithWindows.Should().BeTrue();
        settings.ExcludedTopics.Should().NotBeNull();
    }

    [Fact]
    public void Deserialization_WithPartialProperties_MergesWithDefaults()
    {
        // Arrange
        var json = @"{
            ""IntervalMinutes"": 240,
            ""WallpaperStyle"": ""Center""
        }";

        // Act
        var settings = JsonSerializer.Deserialize<AppSettings>(json);

        // Assert
        settings.Should().NotBeNull();
        settings!.IntervalMinutes.Should().Be(240, "should use provided value");
        settings.WallpaperStyle.Should().Be("Center", "should use provided value");
        settings.CacheMaxMB.Should().Be(500, "should use default value");
        settings.StartWithWindows.Should().BeTrue("should use default value");
    }

    [Fact]
    public void ExcludedTopics_CanBeModified()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.ExcludedTopics.Add("Space");
        settings.ExcludedTopics.Add("Nature");
        settings.ExcludedTopics.Add("Abstract");

        // Assert
        settings.ExcludedTopics.Should().HaveCount(3);
        settings.ExcludedTopics.Should().Contain("Space");
        settings.ExcludedTopics.Should().Contain("Nature");
        settings.ExcludedTopics.Should().Contain("Abstract");
    }

    [Fact]
    public void Properties_CanBeSetIndependently()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.IntervalMinutes = 360;
        settings.WallpaperStyle = "Tile";
        settings.CacheMaxMB = 2000;
        settings.StartWithWindows = false;

        // Assert
        settings.IntervalMinutes.Should().Be(360);
        settings.WallpaperStyle.Should().Be("Tile");
        settings.CacheMaxMB.Should().Be(2000);
        settings.StartWithWindows.Should().BeFalse();
    }
}
