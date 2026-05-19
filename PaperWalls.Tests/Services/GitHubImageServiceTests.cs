using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PaperWalls.Models;
using PaperWalls.Services;

namespace PaperWalls.Tests.Services;

public class GitHubImageServiceTests : IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<GitHubImageService> _logger;
    private readonly IETagCacheService _etagCacheService;
    private readonly TestHttpMessageHandler _httpHandler;

    public GitHubImageServiceTests()
    {
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _settingsService = Substitute.For<ISettingsService>();
        _logger = Substitute.For<ILogger<GitHubImageService>>();
        _etagCacheService = Substitute.For<IETagCacheService>();
        _httpHandler = new TestHttpMessageHandler();

        var httpClient = new HttpClient(_httpHandler);
        _httpClientFactory.CreateClient("GitHub").Returns(httpClient);

        // Default settings with no excluded topics
        _settingsService.LoadSettings().Returns(new AppSettings
        {
            ExcludedTopics = new List<string>()
        });
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpHandler.Dispose();
    }

    [Fact]
    public async Task GetTopicsAsync_ReturnsTopicsFromApiResponse()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new[]
        {
            new { name = "nature", type = "dir", path = "wallpapers/nature" },
            new { name = "space", type = "dir", path = "wallpapers/space" },
            new { name = "README.md", type = "file", path = "wallpapers/README.md" }
        });

        _httpHandler.ResponseContent = responseContent;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // Act
        var topics = await service.GetTopicsAsync();

        // Assert
        topics.Should().HaveCount(2);
        topics.Should().Contain("nature");
        topics.Should().Contain("space");
        topics.Should().NotContain("README.md");
    }

    [Fact]
    public async Task GetTopicsAsync_FiltersExcludedTopics()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new[]
        {
            new { name = "nature", type = "dir", path = "wallpapers/nature" },
            new { name = "space", type = "dir", path = "wallpapers/space" },
            new { name = "abstract", type = "dir", path = "wallpapers/abstract" }
        });

        _httpHandler.ResponseContent = responseContent;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        _settingsService.LoadSettings().Returns(new AppSettings
        {
            ExcludedTopics = new List<string> { "space", "abstract" }
        });

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // Act
        var topics = await service.GetTopicsAsync();

        // Assert
        topics.Should().HaveCount(1);
        topics.Should().Contain("nature");
        topics.Should().NotContain("space");
        topics.Should().NotContain("abstract");
    }

    [Fact]
    public async Task GetTopicsAsync_CachesResults()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new[]
        {
            new { name = "nature", type = "dir", path = "wallpapers/nature" }
        });

        _httpHandler.ResponseContent = responseContent;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // Act
        var topics1 = await service.GetTopicsAsync();
        
        // Reset handler to verify second call doesn't hit HTTP
        _httpHandler.RequestCount = 0;
        
        var topics2 = await service.GetTopicsAsync();

        // Assert
        topics1.Should().HaveCount(1);
        topics2.Should().HaveCount(1);
        _httpHandler.RequestCount.Should().Be(0, "second call should use cache");
    }

    [Fact]
    public async Task GetTopicsAsync_Handles403RateLimitGracefully()
    {
        // Arrange
        _httpHandler.StatusCode = HttpStatusCode.Forbidden;
        _httpHandler.Headers.Add("X-RateLimit-Remaining", "0");
        _httpHandler.Headers.Add("X-RateLimit-Reset", "1234567890");

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () => 
            await service.GetTopicsAsync());
    }

    [Fact]
    public async Task GetTopicsAsync_HandlesNetworkFailureGracefully()
    {
        // Arrange
        _httpHandler.ShouldThrow = true;

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () => 
            await service.GetTopicsAsync());
    }

    [Fact]
    public async Task GetTopicsAsync_ReturnsEmptyListOnNullResponse()
    {
        // Arrange
        _httpHandler.ResponseContent = "null";
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // Act
        var topics = await service.GetTopicsAsync();

        // Assert
        topics.Should().BeEmpty();
    }

    [Fact]
    public async Task GetImagesAsync_ReturnsImagesForTopic()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new[]
        {
            new { name = "image1.jpg", type = "file", path = "wallpapers/nature/image1.jpg", download_url = "https://example.com/image1.jpg" },
            new { name = "image2.png", type = "file", path = "wallpapers/nature/image2.png", download_url = "https://example.com/image2.png" },
            new { name = "README.md", type = "file", path = "wallpapers/nature/README.md", download_url = "https://example.com/readme" }
        });

        _httpHandler.ResponseContent = responseContent;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // Act
        var images = await service.GetImagesAsync("nature");

        // Assert
        images.Should().HaveCount(2);
        images.Should().OnlyContain(i => i.Topic == "nature");
        images.Should().Contain(i => i.FileName == "image1.jpg" && i.Url == "https://example.com/image1.jpg");
        images.Should().Contain(i => i.FileName == "image2.png" && i.Url == "https://example.com/image2.png");
    }

    [Fact]
    public async Task GetImagesAsync_CachesResults()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new[]
        {
            new { name = "image1.jpg", type = "file", path = "wallpapers/nature/image1.jpg", download_url = "https://example.com/image1.jpg" }
        });

        _httpHandler.ResponseContent = responseContent;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // Act
        var images1 = await service.GetImagesAsync("nature");
        
        // Reset handler to verify second call doesn't hit HTTP
        _httpHandler.RequestCount = 0;
        
        var images2 = await service.GetImagesAsync("nature");

        // Assert
        images1.Should().HaveCount(1);
        images2.Should().HaveCount(1);
        _httpHandler.RequestCount.Should().Be(0, "second call should use cache");
    }

    [Fact]
    public async Task GetImagesAsync_HandlesHttpFailure()
    {
        // Arrange
        _httpHandler.StatusCode = HttpStatusCode.NotFound;

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () => 
            await service.GetImagesAsync("nonexistent"));
    }

    [Fact]
    public async Task GetAllTopicsAsync_ReturnsAllTopics_IgnoresExcludedTopicsFilter()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new[]
        {
            new { name = "nature", type = "dir", path = "wallpapers/nature" },
            new { name = "space", type = "dir", path = "wallpapers/space" },
            new { name = "abstract", type = "dir", path = "wallpapers/abstract" }
        });

        _httpHandler.ResponseContent = responseContent;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        // Configure settings to exclude two of the three topics
        _settingsService.LoadSettings().Returns(new AppSettings
        {
            ExcludedTopics = new List<string> { "space", "abstract" }
        });

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // Act
        var allTopics = await service.GetAllTopicsAsync();

        // Assert — all three topics returned regardless of ExcludedTopics
        allTopics.Should().HaveCount(3);
        allTopics.Should().Contain("nature");
        allTopics.Should().Contain("space");
        allTopics.Should().Contain("abstract");
    }

    [Fact]
    public async Task GetTopicsAsync_SharesCacheWithGetAllTopicsAsync()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new[]
        {
            new { name = "nature", type = "dir", path = "wallpapers/nature" }
        });

        _httpHandler.ResponseContent = responseContent;
        _httpHandler.StatusCode = HttpStatusCode.OK;

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // Act — first call populates the shared cache via GetAllTopicsAsync
        await service.GetAllTopicsAsync();
        _httpHandler.RequestCount = 0;

        // GetTopicsAsync delegates to GetAllTopicsAsync and must reuse the same cache
        await service.GetTopicsAsync();
        _httpHandler.RequestCount.Should().Be(0, "GetTopicsAsync delegates to GetAllTopicsAsync and shares its cache");
    }

    [Fact]
    public async Task GetAllTopicsAsync_ReturnsEmpty_WhenSharedCircuitBreakerIsTripped()
    {
        // Arrange — trip the breaker via GetTopicsAsync failures (3 = MaxConsecutiveFailures)
        _httpHandler.ShouldThrow = true;

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => service.GetTopicsAsync());
        }

        // Stop throwing — circuit breaker should now block all methods
        _httpHandler.ShouldThrow = false;
        _httpHandler.RequestCount = 0;

        // Act — GetAllTopicsAsync should see the tripped breaker and return empty without HTTP
        var topics = await service.GetAllTopicsAsync();

        // Assert
        topics.Should().BeEmpty("circuit breaker is tripped; no HTTP call should be made");
        _httpHandler.RequestCount.Should().Be(0, "circuit breaker should short-circuit before hitting the network");
    }

    // ETag / 304 Not Modified ─────────────────────────────────────────────────

    [Fact]
    public async Task GetAllTopicsAsync_FirstRequest_DoesNotSendIfNoneMatch()
    {
        var responseContent = JsonSerializer.Serialize(new[]
        {
            new { name = "nature", type = "dir", path = "wallpapers/nature" }
        });

        _httpHandler.ResponseContent = responseContent;
        _httpHandler.StatusCode = HttpStatusCode.OK;
        _httpHandler.Headers["ETag"] = "\"abc123\"";

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        await service.GetAllTopicsAsync();

        _httpHandler.CapturedRequests[0].Headers.IfNoneMatch.Should().BeEmpty(
            "first request should not send If-None-Match");
    }

    [Fact]
    public async Task GetAllTopicsAsync_SecondRequest_SendsIfNoneMatchAndHandles304()
    {
        var responseContent = JsonSerializer.Serialize(new[]
        {
            new { name = "nature", type = "dir", path = "wallpapers/nature" },
            new { name = "space", type = "dir", path = "wallpapers/space" }
        });

        // Track ETag state through mock
        string? storedETag = null;
        _etagCacheService.GetETag("topics").Returns(_ => storedETag);
        _etagCacheService.When(x => x.SetETag("topics", Arg.Any<string>()))
            .Do(ci => storedETag = ci.ArgAt<string>(1));

        var callCount = 0;
        _httpHandler.ResponseFactory = request =>
        {
            callCount++;
            if (callCount == 1)
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json")
                };
                resp.Headers.ETag = new EntityTagHeaderValue("\"etag-topics\"");
                return resp;
            }
            else
            {
                // Second call should have If-None-Match
                request.Headers.IfNoneMatch.Should().ContainSingle()
                    .Which.Tag.Should().Be("\"etag-topics\"");
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }
        };

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // First call — populates cache and stores ETag
        var topics1 = await service.GetAllTopicsAsync();
        topics1.Should().HaveCount(2);

        // Expire the in-memory cache timestamp so the service makes a new HTTP request
        var cacheTimeField = typeof(GitHubImageService).GetField("_allTopicsCacheTime",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        cacheTimeField.SetValue(service, DateTime.MinValue);

        // Second call — sends If-None-Match, gets 304, returns cached data
        var topics2 = await service.GetAllTopicsAsync();
        topics2.Should().HaveCount(2);
        topics2.Should().Contain("nature");
        topics2.Should().Contain("space");
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAllTopicsAsync_304WithNoCachedData_ReturnsEmptyList()
    {
        // Edge case: ETag stored but in-memory cache is empty (e.g. after restart)
        _etagCacheService.GetETag("topics").Returns("\"stale-etag\"");
        _httpHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.NotModified);

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        var topics = await service.GetAllTopicsAsync();
        topics.Should().BeEmpty();
    }

    [Fact]
    public async Task GetImagesAsync_SecondRequest_SendsIfNoneMatchAndHandles304()
    {
        var responseContent = JsonSerializer.Serialize(new[]
        {
            new { name = "sunset.jpg", type = "file", path = "wallpapers/nature/sunset.jpg", download_url = "https://example.com/sunset.jpg" }
        });

        // Track ETag state through mock
        string? storedImageETag = null;
        _etagCacheService.GetETag("images:nature").Returns(_ => storedImageETag);
        _etagCacheService.When(x => x.SetETag("images:nature", Arg.Any<string>()))
            .Do(ci => storedImageETag = ci.ArgAt<string>(1));

        var callCount = 0;
        _httpHandler.ResponseFactory = request =>
        {
            callCount++;
            if (callCount == 1)
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json")
                };
                resp.Headers.ETag = new EntityTagHeaderValue("\"etag-nature\"");
                return resp;
            }
            else
            {
                request.Headers.IfNoneMatch.Should().ContainSingle()
                    .Which.Tag.Should().Be("\"etag-nature\"");
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }
        };

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        // First call — populates cache and ETag
        var images1 = await service.GetImagesAsync("nature");
        images1.Should().HaveCount(1);
        images1[0].FileName.Should().Be("sunset.jpg");

        // Expire the image cache timestamp via reflection
        var imageCacheField = typeof(GitHubImageService).GetField("_imageCache",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var imageCache = (Dictionary<string, (DateTime timestamp, List<WallpaperImage> images)>)imageCacheField.GetValue(service)!;
        var cached = imageCache["nature"];
        imageCache["nature"] = (DateTime.MinValue, cached.images);

        // Second call — sends If-None-Match, gets 304, returns cached images
        var images2 = await service.GetImagesAsync("nature");
        images2.Should().HaveCount(1);
        images2[0].FileName.Should().Be("sunset.jpg");
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task GetImagesAsync_304WithNoCachedData_ReturnsEmptyList()
    {
        _etagCacheService.GetETag("images:nature").Returns("\"stale-image-etag\"");
        _httpHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.NotModified);

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _etagCacheService, _logger);

        var images = await service.GetImagesAsync("nature");
        images.Should().BeEmpty();
    }

        private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        public string ResponseContent { get; set; } = "[]";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public bool ShouldThrow { get; set; }
        public int RequestCount { get; set; }
        public Dictionary<string, string> Headers { get; } = new();
        public List<HttpRequestMessage> CapturedRequests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            CapturedRequests.Add(request);

            if (ShouldThrow)
            {
                throw new HttpRequestException("Network error");
            }

            if (ResponseFactory != null)
            {
                return Task.FromResult(ResponseFactory(request));
            }

            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseContent, System.Text.Encoding.UTF8, "application/json")
            };

            foreach (var header in Headers)
            {
                response.Headers.Add(header.Key, header.Value);
            }

            return Task.FromResult(response);
        }
    }
}
