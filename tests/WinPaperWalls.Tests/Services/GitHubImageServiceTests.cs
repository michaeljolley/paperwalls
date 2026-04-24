using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WinPaperWalls.Models;
using WinPaperWalls.Services;

namespace WinPaperWalls.Tests.Services;

public class GitHubImageServiceTests
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<GitHubImageService> _logger;
    private readonly TestHttpMessageHandler _httpHandler;

    public GitHubImageServiceTests()
    {
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _settingsService = Substitute.For<ISettingsService>();
        _logger = Substitute.For<ILogger<GitHubImageService>>();
        _httpHandler = new TestHttpMessageHandler();

        var httpClient = new HttpClient(_httpHandler);
        _httpClientFactory.CreateClient("GitHub").Returns(httpClient);

        // Default settings with no excluded topics
        _settingsService.LoadSettings().Returns(new AppSettings
        {
            ExcludedTopics = new List<string>()
        });
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

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _logger);

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

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _logger);

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

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _logger);

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

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () => 
            await service.GetTopicsAsync());
    }

    [Fact]
    public async Task GetTopicsAsync_HandlesNetworkFailureGracefully()
    {
        // Arrange
        _httpHandler.ShouldThrow = true;

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _logger);

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

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _logger);

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

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _logger);

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

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _logger);

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

        var service = new GitHubImageService(_httpClientFactory, _settingsService, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () => 
            await service.GetImagesAsync("nonexistent"));
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public string ResponseContent { get; set; } = "[]";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public bool ShouldThrow { get; set; }
        public int RequestCount { get; set; }
        public Dictionary<string, string> Headers { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            if (ShouldThrow)
            {
                throw new HttpRequestException("Network error");
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
