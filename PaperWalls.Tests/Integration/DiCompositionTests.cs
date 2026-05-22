using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PaperWalls.Services;
using PaperWalls.ViewModels;

namespace PaperWalls.Tests.Integration;

/// <summary>
/// Validates that the DI container can resolve all registered services.
/// Platform-dependent services (Win32, WinUI) are stubbed out.
/// </summary>
public class DiCompositionTests
{
    private ServiceProvider BuildTestServiceProvider()
    {
        var services = new ServiceCollection();

        // Logging (required by most services)
        services.AddLogging();

        // HTTP clients (same as production)
        services.AddHttpClient("GitHub", client => { client.Timeout = TimeSpan.FromSeconds(20); });
        services.AddHttpClient();

        // Pure C# services — same as production registrations
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IETagCacheService, ETagCacheService>();
        services.AddSingleton<IGitHubImageService, GitHubImageService>();
        services.AddSingleton<ICacheService, CacheService>();
        services.AddSingleton<IWallpaperService, WallpaperService>();
        services.AddSingleton<ILogBundleService, LogBundleService>();

        // Platform-dependent services — stub with mocks
        services.AddSingleton(Substitute.For<IDesktopWallpaperService>());
        services.AddSingleton(Substitute.For<IStartupManager>());

        // SchedulerService — same factory pattern as production
        services.AddSingleton<SchedulerService>();
        services.AddSingleton<ISchedulerService>(sp => sp.GetRequiredService<SchedulerService>());

        // ViewModel
        services.AddSingleton<SettingsViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    [Fact]
    public void AllCoreServices_CanBeResolved()
    {
        using var provider = BuildTestServiceProvider();

        // Services
        provider.GetRequiredService<ISettingsService>().Should().NotBeNull();
        provider.GetRequiredService<IETagCacheService>().Should().NotBeNull();
        provider.GetRequiredService<IGitHubImageService>().Should().NotBeNull();
        provider.GetRequiredService<ICacheService>().Should().NotBeNull();
        provider.GetRequiredService<IDesktopWallpaperService>().Should().NotBeNull();
        provider.GetRequiredService<IWallpaperService>().Should().NotBeNull();
        provider.GetRequiredService<IStartupManager>().Should().NotBeNull();
        provider.GetRequiredService<ILogBundleService>().Should().NotBeNull();

        // Scheduler (factory registration)
        provider.GetRequiredService<SchedulerService>().Should().NotBeNull();
        provider.GetRequiredService<ISchedulerService>().Should().NotBeNull();

        // ViewModel
        provider.GetRequiredService<SettingsViewModel>().Should().NotBeNull();
    }

    [Fact]
    public void SchedulerService_ResolvedAsInterface_IsSameInstance()
    {
        using var provider = BuildTestServiceProvider();

        var direct = provider.GetRequiredService<SchedulerService>();
        var viaInterface = provider.GetRequiredService<ISchedulerService>();

        viaInterface.Should().BeSameAs(direct,
            "ISchedulerService factory must return the same singleton instance as SchedulerService");
    }
}
