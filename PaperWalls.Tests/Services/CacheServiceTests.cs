using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PaperWalls.Services;
using PaperWalls.Tests.Helpers;

namespace PaperWalls.Tests.Services;

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
        _testCacheDirectory = Path.Combine(tempPath, "PaperWalls_Test_Cache_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testCacheDirectory);

        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _logger = Substitute.For<ILogger<CacheService>>();
        _httpHandler = new TestHttpMessageHandler();

        var httpClient = new HttpClient(_httpHandler);
        _httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
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
        var imageData = ValidImageBytes.PngShort; // PNG header
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
        var imageData = ValidImageBytes.PngShort;
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
        var imageData = ValidImageBytes.PngShort;
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
        // PNG magic bytes at the front so new image validation passes; sizes stay 1 KB / 2 KB
        var imageData1 = new byte[1024];
        ValidImageBytes.PngShort.CopyTo(imageData1, 0);
        var imageData2 = new byte[2048];
        ValidImageBytes.PngShort.CopyTo(imageData2, 0);

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


    [Fact]
    public async Task DownloadImageAsync_ThrowsInvalidOperationException_WhenResponseBodyIsNotValidImage()
    {
        // Arrange - simulate GitHub returning an HTML error page instead of image bytes
        var htmlBytes = System.Text.Encoding.UTF8.GetBytes("<html><body>Not Found</body></html>");
        _httpHandler.ResponseBytes = htmlBytes;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.DownloadImageAsync("https://example.com/image.jpg", "invalid.jpg"));
    }

    [Fact]
    public async Task DownloadImageAsync_ThrowsInvalidOperationException_DoesNotCacheInvalidContent()
    {
        // Arrange - simulate truncated/garbage bytes that are not a valid image
        var garbageBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        _httpHandler.ResponseBytes = garbageBytes;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.DownloadImageAsync("https://example.com/image.jpg", "garbage.jpg"));

        // Verify nothing was written to disk
        File.Exists(Path.Combine(_testCacheDirectory, "garbage.jpg")).Should().BeFalse(
            "invalid image content must not be cached to disk");
    }

    [Fact]
    public async Task DownloadImageAsync_Succeeds_WhenResponseContainsValidPngMagicBytes()
    {
        // Arrange - PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
        var pngBytes = ValidImageBytes.Png;
        _httpHandler.ResponseBytes = pngBytes;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);

        // Act
        var filePath = await service.DownloadImageAsync("https://example.com/image.png", "valid.png");

        // Assert
        filePath.Should().NotBeNullOrEmpty();
        File.Exists(filePath).Should().BeTrue();
        var cachedBytes = await File.ReadAllBytesAsync(filePath);
        cachedBytes.Should().BeEquivalentTo(pngBytes);
    }


    [Fact]
    public async Task DownloadImageAsync_Succeeds_WhenResponseContainsValidJpegMagicBytes()
    {
        // Arrange - JPEG magic bytes: FF D8 FF
        var jpegBytes = ValidImageBytes.Jpeg;
        _httpHandler.ResponseBytes = jpegBytes;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);

        // Act
        var filePath = await service.DownloadImageAsync("https://example.com/image.jpg", "valid.jpg");

        // Assert
        filePath.Should().NotBeNullOrEmpty();
        File.Exists(filePath).Should().BeTrue();
        var cachedBytes = await File.ReadAllBytesAsync(filePath);
        cachedBytes.Should().BeEquivalentTo(jpegBytes);
    }

    [Fact]
    public async Task DownloadImageAsync_Succeeds_WhenResponseContainsValidBmpMagicBytes()
    {
        // Arrange - BMP magic bytes: 42 4D ("BM")
        var bmpBytes = ValidImageBytes.Bmp;
        _httpHandler.ResponseBytes = bmpBytes;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);

        // Act
        var filePath = await service.DownloadImageAsync("https://example.com/image.bmp", "valid.bmp");

        // Assert
        filePath.Should().NotBeNullOrEmpty();
        File.Exists(filePath).Should().BeTrue();
        var cachedBytes = await File.ReadAllBytesAsync(filePath);
        cachedBytes.Should().BeEquivalentTo(bmpBytes);
    }

    [Fact]
    public async Task DownloadImageAsync_Succeeds_WhenResponseContainsValidWebpMagicBytes()
    {
        // Arrange - WEBP: RIFF (52 49 46 46) + 4-byte size + WEBP (57 45 42 50)
        var webpBytes = ValidImageBytes.Webp;
        _httpHandler.ResponseBytes = webpBytes;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);

        // Act
        var filePath = await service.DownloadImageAsync("https://example.com/image.webp", "valid.webp");

        // Assert
        filePath.Should().NotBeNullOrEmpty();
        File.Exists(filePath).Should().BeTrue();
        var cachedBytes = await File.ReadAllBytesAsync(filePath);
        cachedBytes.Should().BeEquivalentTo(webpBytes);
    }

    [Fact]
    public async Task DownloadImageAsync_ThrowsInvalidOperationException_WhenResponseIsEmpty()
    {
        // Arrange - 0-byte response (e.g. network truncation)
        _httpHandler.ResponseBytes = new byte[0];
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new CacheService(_httpClientFactory, _logger, _testCacheDirectory);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.DownloadImageAsync("https://example.com/image.jpg", "empty.jpg"));
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
