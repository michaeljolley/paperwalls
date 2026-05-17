using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PaperWalls.Models;
using PaperWalls.Services;
using PaperWalls.ViewModels;

namespace PaperWalls.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly ISettingsService _settingsService;
    private readonly ICacheService _cacheService;
    private readonly IDesktopWallpaperService _desktopWallpaperService;
    private readonly StartupManager _startupManager;
    private readonly IGitHubImageService _gitHubImageService;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly SettingsViewModel _viewModel;

    public SettingsViewModelTests()
    {
        _settingsService = Substitute.For<ISettingsService>();
        _cacheService = Substitute.For<ICacheService>();
        _desktopWallpaperService = Substitute.For<IDesktopWallpaperService>();
        // StartupManager is sealed — use a real instance with a mocked logger
        _startupManager = new StartupManager(Substitute.For<ILogger<StartupManager>>());
        _gitHubImageService = Substitute.For<IGitHubImageService>();
        _logger = Substitute.For<ILogger<SettingsViewModel>>();

        _settingsService.LoadSettings().Returns(new AppSettings
        {
            ExcludedTopics = new List<string>()
        });

        // Default: GetAllTopicsAsync returns an empty list so LoadAsync does not throw
        _gitHubImageService
            .GetAllTopicsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        _viewModel = new SettingsViewModel(
            _settingsService,
            _cacheService,
            _desktopWallpaperService,
            _startupManager,
            _gitHubImageService,
            _logger);
    }

    // Save ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_Success_SetsSaveSuccessVisible()
    {
        // Use StartWithWindows = false so the real StartupManager calls DeleteValue,
        // which is safe in CI (no-op when the registry key does not exist).
        _viewModel.StartWithWindows = false;

        _viewModel.SaveCommand.Execute(null);

        _viewModel.SaveSuccessVisible.Should().BeTrue();
        _viewModel.SaveErrorVisible.Should().BeFalse();
    }

    [Fact]
    public void Save_WhenSettingsServiceThrows_SetsSaveErrorVisible()
    {
        _settingsService
            .When(x => x.SaveSettings(Arg.Any<AppSettings>()))
            .Do(_ => throw new IOException("disk full"));

        _viewModel.SaveCommand.Execute(null);

        _viewModel.SaveErrorVisible.Should().BeTrue();
        _viewModel.SaveSuccessVisible.Should().BeFalse();
    }

    [Fact]
    public void Save_ResetsFlagsBeforeAttempt()
    {
        // Prime SaveErrorVisible via a failing first call
        _settingsService
            .When(x => x.SaveSettings(Arg.Any<AppSettings>()))
            .Do(_ => throw new IOException("first call fails"));
        _viewModel.SaveCommand.Execute(null);
        _viewModel.SaveErrorVisible.Should().BeTrue("precondition: error flag set by first call");

        // Reconfigure to succeed on next call
        _settingsService.ClearReceivedCalls();
        _settingsService
            .When(x => x.SaveSettings(Arg.Any<AppSettings>()))
            .Do(_ => { });
        _viewModel.StartWithWindows = false;

        _viewModel.SaveCommand.Execute(null);

        _viewModel.SaveErrorVisible.Should().BeFalse();
        _viewModel.SaveSuccessVisible.Should().BeTrue();
    }

    [Fact]
    public void Save_PersistsSelectedStyle()
    {
        _viewModel.StartWithWindows = false;
        _viewModel.SelectedStyleIndex = 2; // "Stretch"

        _viewModel.SaveCommand.Execute(null);

        _settingsService.Received(1).SaveSettings(
            Arg.Is<AppSettings>(s => s.WallpaperStyle == "Stretch"));
    }

    // ClearCache ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearCache_Success_ClearsClearCacheErrorVisible()
    {
        _viewModel.ClearCacheErrorVisible = true;

        await ((IAsyncRelayCommand)_viewModel.ClearCacheCommand).ExecuteAsync(null);

        _viewModel.ClearCacheErrorVisible.Should().BeFalse();
        await _cacheService.Received(1).ClearCacheAsync();
    }

    [Fact]
    public async Task ClearCache_WhenCacheServiceThrows_SetsClearCacheErrorVisible()
    {
        _cacheService
            .ClearCacheAsync()
            .Returns(Task.FromException(new InvalidOperationException("cache locked")));

        await ((IAsyncRelayCommand)_viewModel.ClearCacheCommand).ExecuteAsync(null);

        _viewModel.ClearCacheErrorVisible.Should().BeTrue();
    }

    [Fact]
    public async Task ClearCache_ResetsErrorFlagBeforeAttempt()
    {
        // Prime error flag via a failing first call
        _cacheService
            .ClearCacheAsync()
            .Returns(Task.FromException(new InvalidOperationException("first fail")));
        await ((IAsyncRelayCommand)_viewModel.ClearCacheCommand).ExecuteAsync(null);
        _viewModel.ClearCacheErrorVisible.Should().BeTrue("precondition");

        // Configure success for second call
        _cacheService.ClearCacheAsync().Returns(Task.CompletedTask);

        await ((IAsyncRelayCommand)_viewModel.ClearCacheCommand).ExecuteAsync(null);

        _viewModel.ClearCacheErrorVisible.Should().BeFalse();
    }

    // LoadAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_SetsSettingsLoadedTrue()
    {
        await _viewModel.LoadAsync();

        _viewModel.SettingsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_PopulatesIntervalFromSettings()
    {
        _settingsService.LoadSettings().Returns(new AppSettings
        {
            IntervalMinutes = 60, // "Every hour" -> index 1
            ExcludedTopics = new List<string>()
        });

        await _viewModel.LoadAsync();

        _viewModel.SelectedIntervalIndex.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsync_PopulatesStyleFromSettings()
    {
        _settingsService.LoadSettings().Returns(new AppSettings
        {
            WallpaperStyle = "Fit",
            ExcludedTopics = new List<string>()
        });

        await _viewModel.LoadAsync();

        _viewModel.SelectedStyleIndex.Should().Be(
            Array.IndexOf(SettingsViewModel.StyleOptions, "Fit"));
    }

    [Fact]
    public async Task LoadTopicsAsync_WhenGitHubServiceThrows_SetsTopicsError()
    {
        _gitHubImageService
            .GetAllTopicsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<List<string>>(new HttpRequestException("API down")));

        await _viewModel.LoadAsync();

        _viewModel.TopicsError.Should().BeTrue();
        _viewModel.IsTopicsLoading.Should().BeFalse("finally block must always clear the loading flag");
    }

    [Fact]
    public async Task LoadTopicsAsync_PopulatesTopicItems()
    {
        _gitHubImageService
            .GetAllTopicsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "nature", "space", "abstract" });

        await _viewModel.LoadAsync();

        _viewModel.TopicItems.Should().HaveCount(3);
        _viewModel.TopicsLoaded.Should().BeTrue();
        _viewModel.TopicsError.Should().BeFalse();
    }

    [Fact]
    public async Task LoadTopicsAsync_ExcludedTopics_NotSelectedInTopicItems()
    {
        _settingsService.LoadSettings().Returns(new AppSettings
        {
            ExcludedTopics = new List<string> { "space" }
        });
        _gitHubImageService
            .GetAllTopicsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "nature", "space", "abstract" });

        await _viewModel.LoadAsync();

        _viewModel.TopicItems.Should().HaveCount(3);
        _viewModel.TopicItems.First(t => t.Name == "space").IsSelected.Should().BeFalse(
            "space is in ExcludedTopics");
        _viewModel.TopicItems.First(t => t.Name == "nature").IsSelected.Should().BeTrue(
            "nature is not excluded");
    }
}
