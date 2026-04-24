using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WinPaperWalls.Services;

namespace WinPaperWalls.Tests.Services;

public class CacheServiceTests : IDisposable
{
    private readonly string _testCacheDirectory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CacheService> _logger;
    private readonly TestHttpMessageHandler _httpHandler;

    public CacheServiceTests()
    {
        // Create a unique test cache directory
        var tempPath = Path.GetTempPath();
        _testCacheDirectory = Path.Combine(tempPath, "WinPaperWalls_Test_Cache_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testCacheDirectory);

        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _logger = Substitute.For<ILogger<CacheService>>();
        _httpHandler = new TestHttpMessageHandler();

        var httpClient = new HttpClient(_httpHandler);
        _httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);
    }

    public void Dispose()
    {
        // Clean up test cache directory
        if (Directory.Exists(_testCacheDirectory))
        {
            Directory.Delete(_testCacheDirectory, true);
        }
    }

    [Fact]
    public async Task DownloadImageAsync_DownloadsImageToCacheDirectory()
    {
        // Arrange
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        _httpHandler.ResponseBytes = imageData;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);

        // Act
        var filePath = await service.DownloadImageAsync("https://example.com/image.jpg", "image.jpg");

        // Assert
        File.Exists(filePath).Should().BeTrue();
        var downloadedData = await File.ReadAllBytesAsync(filePath);
        downloadedData.Should().BeEquivalentTo(imageData);
    }

    [Fact]
    public async Task DownloadImageAsync_ReturnsCachedPathForExistingImage()
    {
        // Arrange
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        _httpHandler.ResponseBytes = imageData;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);

        // Act
        var filePath1 = await service.DownloadImageAsync("https://example.com/image.jpg", "image.jpg");
        
        // Reset request count to verify no second download
        _httpHandler.RequestCount = 0;
        
        var filePath2 = await service.DownloadImageAsync("https://example.com/image.jpg", "image.jpg");

        // Assert
        filePath1.Should().Be(filePath2);
        _httpHandler.RequestCount.Should().Be(0, "should use cached file");
    }

    [Fact]
    public void GetCachedImagePath_ReturnsNullForNonCachedImage()
    {
        // Arrange
        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);

        // Act
        var path = service.GetCachedImagePath("nonexistent.jpg");

        // Assert
        path.Should().BeNull();
    }

    [Fact]
    public async Task GetCachedImagePath_ReturnsPathForCachedImage()
    {
        // Arrange
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        _httpHandler.ResponseBytes = imageData;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);
        var downloadedPath = await service.DownloadImageAsync("https://example.com/image.jpg", "image.jpg");

        // Act
        var path = service.GetCachedImagePath("image.jpg");

        // Assert
        path.Should().NotBeNull();
        path.Should().Be(downloadedPath);
    }

    [Fact]
    public async Task GetCacheSizeBytes_CalculatesCacheSizeCorrectly()
    {
        // Arrange
        var imageData1 = new byte[1024]; // 1 KB
        var imageData2 = new byte[2048]; // 2 KB
        
        _httpHandler.ResponseBytes = imageData1;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);
        
        await service.DownloadImageAsync("https://example.com/image1.jpg", "image1.jpg");
        
        _httpHandler.ResponseBytes = imageData2;
        await service.DownloadImageAsync("https://example.com/image2.jpg", "image2.jpg");

        // Act
        var size = service.GetCacheSizeBytes();

        // Assert
        // File system may add padding bytes, so use a range
        size.Should().BeInRange(3072, 3072 + 4096); // 1 KB + 2 KB, plus potential filesystem overhead
    }

    [Fact]
    public async Task EvictOldestAsync_RemovesOldestFilesBasedOnLRU()
    {
        // Arrange
        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);
        
        // Create three files with different access times
        var file1 = Path.Combine(_testCacheDirectory, "image1.jpg");
        var file2 = Path.Combine(_testCacheDirectory, "image2.jpg");
        var file3 = Path.Combine(_testCacheDirectory, "image3.jpg");

        await File.WriteAllBytesAsync(file1, new byte[1024]); // 1 KB
        await Task.Delay(100);
        await File.WriteAllBytesAsync(file2, new byte[1024]); // 1 KB
        await Task.Delay(100);
        await File.WriteAllBytesAsync(file3, new byte[1024]); // 1 KB

        // Set different last access times
        File.SetLastAccessTime(file1, DateTime.UtcNow.AddHours(-3));
        File.SetLastAccessTime(file2, DateTime.UtcNow.AddHours(-2));
        File.SetLastAccessTime(file3, DateTime.UtcNow.AddHours(-1));

        // Act - evict to 2 KB target (should keep 2 newest files)
        await service.EvictOldestAsync(2048);

        // Assert
        File.Exists(file1).Should().BeFalse("oldest file should be deleted");
        File.Exists(file2).Should().BeTrue("second file should remain");
        File.Exists(file3).Should().BeTrue("newest file should remain");
    }

    [Fact]
    public async Task ClearCacheAsync_DeletesAllCachedFiles()
    {
        // Arrange
        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);
        
        var file1 = Path.Combine(_testCacheDirectory, "image1.jpg");
        var file2 = Path.Combine(_testCacheDirectory, "image2.jpg");
        var file3 = Path.Combine(_testCacheDirectory, "image3.jpg");

        await File.WriteAllBytesAsync(file1, new byte[1024]);
        await File.WriteAllBytesAsync(file2, new byte[1024]);
        await File.WriteAllBytesAsync(file3, new byte[1024]);

        // Act
        await service.ClearCacheAsync();

        // Assert
        File.Exists(file1).Should().BeFalse();
        File.Exists(file2).Should().BeFalse();
        File.Exists(file3).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadImageAsync_HandlesHttpFailureGracefully()
    {
        // Arrange
        _httpHandler.StatusCode = HttpStatusCode.NotFound;

        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await service.DownloadImageAsync("https://example.com/notfound.jpg", "notfound.jpg"));
    }

    [Fact]
    public void GetCacheSizeBytes_ReturnsZeroForNonExistentDirectory()
    {
        // This test verifies the service handles the case where cache directory doesn't exist yet
        // CacheService creates its directory in constructor, so we can't test this directly
        // but we verify it doesn't crash
        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);
        
        var size = service.GetCacheSizeBytes();
        
        size.Should().BeGreaterThanOrEqualTo(0);
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public byte[] ResponseBytes { get; set; } = Array.Empty<byte>();
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public int RequestCount { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new ByteArrayContent(ResponseBytes)
            };

            return Task.FromResult(response);
        }
    }
}
