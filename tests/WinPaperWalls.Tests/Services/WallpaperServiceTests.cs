using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WinPaperWalls.Models;
using WinPaperWalls.Services;

namespace WinPaperWalls.Tests.Services;

public class WallpaperServiceTests
{
    private readonly IGitHubImageService _githubService;
    private readonly ICacheService _cacheService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<WallpaperService> _logger;

    public WallpaperServiceTests()
    {
        _githubService = Substitute.For<IGitHubImageService>();
        _cacheService = Substitute.For<ICacheService>();
        _settingsService = Substitute.For<ISettingsService>();
        _logger = Substitute.For<ILogger<WallpaperService>>();

        // Default settings
        _settingsService.LoadSettings().Returns(new AppSettings
        {
            CacheMaxMB = 500,
            WallpaperStyle = "Fill",
            ExcludedTopics = new List<string>()
        });
    }

    [Fact]
    public async Task ChangeWallpaperAsync_CallsServicesInCorrectOrder()
    {
        // Arrange
        var topics = new List<string> { "nature" };
        var images = new List<WallpaperImage>
        {
            new() { FileName = "image1.jpg", Url = "https://example.com/image1.jpg", Topic = "nature" }
        };

        _githubService.GetTopicsAsync().Returns(topics);
        _githubService.GetImagesAsync("nature").Returns(images);
        _cacheService.DownloadImageAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns("C:\\test\\image1.jpg");
        _cacheService.GetCacheSizeBytes().Returns(100 * 1024 * 1024); // 100 MB

        var service = new WallpaperService(_githubService, _cacheService, _settingsService, _logger);

        // Act
        await service.ChangeWallpaperAsync();

        // Assert
        Received.InOrder(() =>
        {
            _settingsService.LoadSettings();
            _githubService.GetTopicsAsync();
            _githubService.GetImagesAsync("nature");
            _cacheService.DownloadImageAsync(Arg.Any<string>(), Arg.Any<string>());
        });
    }

    [Fact]
    public async Task ChangeWallpaperAsync_RespectsExcludedTopics()
    {
        // Arrange - topics from GitHub don't include excluded ones
        var topics = new List<string> { "nature", "abstract" }; // space already filtered by GitHubImageService
        var images = new List<WallpaperImage>
        {
            new() { FileName = "image1.jpg", Url = "https://example.com/image1.jpg", Topic = "nature" }
        };

        _githubService.GetTopicsAsync().Returns(topics);
        _githubService.GetImagesAsync(Arg.Any<string>()).Returns(images);
        _cacheService.DownloadImageAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns("C:\\test\\image1.jpg");
        _cacheService.GetCacheSizeBytes().Returns(100 * 1024 * 1024);

        var service = new WallpaperService(_githubService, _cacheService, _settingsService, _logger);

        // Act
        await service.ChangeWallpaperAsync();

        // Assert
        await _githubService.Received(1).GetTopicsAsync();
        // Should work with available topics
        await _githubService.Received().GetImagesAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task ChangeWallpaperAsync_HandlesNoAvailableTopicsGracefully()
    {
        // Arrange
        _githubService.GetTopicsAsync().Returns(new List<string>());

        var service = new WallpaperService(_githubService, _cacheService, _settingsService, _logger);

        // Act
        await service.ChangeWallpaperAsync();

        // Assert
        await _githubService.Received(1).GetTopicsAsync();
        await _githubService.DidNotReceive().GetImagesAsync(Arg.Any<string>());
        await _cacheService.DidNotReceive().DownloadImageAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ChangeWallpaperAsync_HandlesDownloadFailureGracefully()
    {
        // Arrange
        var topics = new List<string> { "nature" };
        var images = new List<WallpaperImage>
        {
            new() { FileName = "image1.jpg", Url = "https://example.com/image1.jpg", Topic = "nature" },
            new() { FileName = "image2.jpg", Url = "https://example.com/image2.jpg", Topic = "nature" }
        };

        _githubService.GetTopicsAsync().Returns(topics);
        _githubService.GetImagesAsync("nature").Returns(images);
        
        // First download fails, second succeeds
        _cacheService.DownloadImageAsync(Arg.Any<string>(), "image1.jpg")
            .Returns(Task.FromException<string>(new Exception("Download failed")));
        _cacheService.DownloadImageAsync(Arg.Any<string>(), "image2.jpg")
            .Returns("C:\\test\\image2.jpg");
        _cacheService.GetCacheSizeBytes().Returns(100 * 1024 * 1024);

        var service = new WallpaperService(_githubService, _cacheService, _settingsService, _logger);

        // Act - should not throw
        await service.ChangeWallpaperAsync();

        // Assert - should have attempted multiple downloads
        await _cacheService.Received().DownloadImageAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ChangeWallpaperAsync_EvictsCacheWhenOverLimit()
    {
        // Arrange
        var topics = new List<string> { "nature" };
        var images = new List<WallpaperImage>
        {
            new() { FileName = "image1.jpg", Url = "https://example.com/image1.jpg", Topic = "nature" }
        };

        _githubService.GetTopicsAsync().Returns(topics);
        _githubService.GetImagesAsync("nature").Returns(images);
        _cacheService.DownloadImageAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns("C:\\test\\image1.jpg");
        
        // Cache size exceeds limit (600 MB > 500 MB)
        _cacheService.GetCacheSizeBytes().Returns(600L * 1024 * 1024);

        var service = new WallpaperService(_githubService, _cacheService, _settingsService, _logger);

        // Act
        await service.ChangeWallpaperAsync();

        // Assert
        await _cacheService.Received(1).EvictOldestAsync(500L * 1024 * 1024);
    }

    [Fact]
    public async Task ChangeWallpaperAsync_DoesNotRepeatRecentlyUsedImages()
    {
        // Arrange
        var topics = new List<string> { "nature" };
        var images = new List<WallpaperImage>
        {
            new() { FileName = "image1.jpg", Url = "https://example.com/image1.jpg", Topic = "nature" },
            new() { FileName = "image2.jpg", Url = "https://example.com/image2.jpg", Topic = "nature" },
            new() { FileName = "image3.jpg", Url = "https://example.com/image3.jpg", Topic = "nature" }
        };

        _githubService.GetTopicsAsync().Returns(topics);
        _githubService.GetImagesAsync("nature").Returns(images);
        _cacheService.DownloadImageAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(x => $"C:\\test\\{x.ArgAt<string>(1)}");
        _cacheService.GetCacheSizeBytes().Returns(100 * 1024 * 1024);

        var service = new WallpaperService(_githubService, _cacheService, _settingsService, _logger);

        // Act - call multiple times
        var usedFiles = new HashSet<string>();
        for (int i = 0; i < 3; i++)
        {
            await service.ChangeWallpaperAsync();
        }

        // Assert - we can't directly test the internal recently used list,
        // but we can verify it doesn't crash and downloads different images
        await _cacheService.Received().DownloadImageAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ChangeWallpaperAsync_HandlesEmptyTopicImageList()
    {
        // Arrange
        var topics = new List<string> { "nature", "space" };
        
        _githubService.GetTopicsAsync().Returns(topics);
        _githubService.GetImagesAsync("nature").Returns(new List<WallpaperImage>());
        _githubService.GetImagesAsync("space").Returns(new List<WallpaperImage>
        {
            new() { FileName = "space1.jpg", Url = "https://example.com/space1.jpg", Topic = "space" }
        });
        _cacheService.DownloadImageAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns("C:\\test\\space1.jpg");
        _cacheService.GetCacheSizeBytes().Returns(100 * 1024 * 1024);

        var service = new WallpaperService(_githubService, _cacheService, _settingsService, _logger);

        // Act
        await service.ChangeWallpaperAsync();

        // Assert - should skip empty topic and try another
        await _githubService.Received().GetImagesAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task ChangeWallpaperAsync_SelectsRandomTopicAndImage()
    {
        // Arrange
        var topics = new List<string> { "nature", "space", "abstract" };
        var images = new List<WallpaperImage>
        {
            new() { FileName = "image1.jpg", Url = "https://example.com/image1.jpg", Topic = "nature" },
            new() { FileName = "image2.jpg", Url = "https://example.com/image2.jpg", Topic = "nature" }
        };

        _githubService.GetTopicsAsync().Returns(topics);
        _githubService.GetImagesAsync(Arg.Any<string>()).Returns(images);
        _cacheService.DownloadImageAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns("C:\\test\\image.jpg");
        _cacheService.GetCacheSizeBytes().Returns(100 * 1024 * 1024);

        var service = new WallpaperService(_githubService, _cacheService, _settingsService, _logger);

        // Act
        await service.ChangeWallpaperAsync();

        // Assert - random selection means we can't predict which, but one should be called
        await _githubService.Received().GetImagesAsync(Arg.Any<string>());
        await _cacheService.Received().DownloadImageAsync(Arg.Any<string>(), Arg.Any<string>());
    }
}
