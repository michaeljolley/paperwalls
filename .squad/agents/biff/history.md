# Project Context

- **Owner:** Michael Jolley
- **Project:** win-paperwalls — A Windows service with system tray icon that fetches 4K wallpapers from the @PaperWalls4K X/Twitter account and rotates the desktop background on a configurable schedule.
- **Stack:** C# / .NET, WinUI 3 (settings UI / tray), Windows Service
- **Created:** 2026-04-24

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-04-24: Phase 1 Foundation Complete

**Solution Structure:**
- `win-paperwalls.sln` - Main solution file at repo root
- `src/WinPaperWalls/` - Unpackaged WinUI 3 app (net9.0-windows10.0.19041.0)
- `tests/WinPaperWalls.Tests/` - xUnit test project (created but not in solution yet)

**Core Components Built:**
1. **App.xaml.cs** - Generic host pattern with `Microsoft.Extensions.Hosting`, single-instance mutex (`WinPaperWalls_SingleInstance`)
2. **Models/** - `AppSettings`, `WallpaperTopic`, `WallpaperImage`
3. **Services/SettingsService** - Thread-safe JSON persistence to `%LOCALAPPDATA%\WinPaperWalls\settings.json`, fires `SettingsChanged` event
4. **Interop/DesktopWallpaper** - P/Invoke wrapper for `SystemParametersInfo`, supports Fill/Fit/Stretch/Tile/Center/Span via registry keys

**DI Container:**
- All services registered via `Host.CreateDefaultBuilder()` in `App.xaml.cs`
- Services: `ISettingsService`, `MainWindow`

**Key Patterns:**
- Generic host for DI and lifetime management
- Lock-based thread safety in SettingsService
- Registry-based wallpaper style configuration before `SPI_SETDESKWALLPAPER` call

**NuGet Packages:**
- `Microsoft.WindowsAppSDK` 1.6.240923002
- `Microsoft.Extensions.Hosting` 9.0.0
- `Microsoft.Extensions.DependencyInjection` 9.0.0
- `H.NotifyIcon.WinUI` 2.1.4 (referenced, not yet wired up)

**Test Project Note:**
- Test project created but excluded from solution due to transitive `WindowsAppSDK` build target conflicts
- TODO: Refactor services into separate class library to enable proper unit testing
- Alternative: Use `Directory.Build.props` to fully exclude WindowsAppSDK targets from test projects

**Build Command:** `dotnet build` at repo root succeeds.

### 2026-04-24: Phase 2 Image Source Complete

**GitHub API Integration:**
- **GitHubImageService** - Fetches topics and images from burkeholland/paper repo via GitHub API v3
  - `GetTopicsAsync()` - Lists wallpaper topics from `/repos/burkeholland/paper/contents/wallpapers`
  - `GetImagesAsync(topic)` - Lists images in a specific topic folder
  - In-memory caching with 1-hour expiry to minimize API calls
  - Rate limit tracking via `X-RateLimit-Remaining` header with warnings at <10 requests
  - Filters topics by `AppSettings.ExcludedTopics`
  - Sets `User-Agent: WinPaperWalls/1.0` header (required by GitHub API)
  - Graceful fallback to stale cache on network errors

**Cache Management:**
- **CacheService** - LRU cache in `%LOCALAPPDATA%\WinPaperWalls\cache\`
  - `DownloadImageAsync()` - Downloads and caches images, updates last access time
  - `GetCachedImagePath()` - Returns path if cached
  - `GetCacheSizeBytes()` - Calculates total cache size
  - `EvictOldestAsync(targetBytes)` - LRU eviction based on `LastAccessTime` to stay under `CacheMaxMB`
  - `ClearCacheAsync()` - Deletes all cached images
  - Thread-safe file operations with lock

**Wallpaper Orchestration:**
- **WallpaperService** - Main orchestrator
  - `ChangeWallpaperAsync()` - Core flow: select random topic → random image → download → set wallpaper
  - Tracks last 20 used images to avoid repeats (HashSet with simple LRU trim)
  - Smart retry logic: up to 10 attempts across different topics/images
  - After 5 failed attempts, allows recently-used images to increase success rate
  - Cache eviction triggered when size exceeds `CacheMaxMB`
  - Integrates with `DesktopWallpaper.SetWallpaper()` for final application
  - Comprehensive error handling (no internet, API down, empty topics)

**DI Registration:**
- Updated `App.xaml.cs` to register:
  - `IHttpClientFactory` via `services.AddHttpClient()`
  - Named client "GitHub" for API calls
  - `IGitHubImageService`, `ICacheService`, `IWallpaperService` as singletons

**NuGet Additions:**
- `Microsoft.Extensions.Http` 9.0.0 for `IHttpClientFactory` pattern

**Key Design Decisions:**
- `System.Random.Shared` for thread-safe random selection
- Synchronous `LoadSettings()` instead of async (matches SettingsService interface)
- JSON deserialization uses `System.Text.Json` with `[JsonPropertyName]` attributes
- Cache uses file system `LastAccessTime` for LRU tracking (simple, OS-managed)
- Image file detection: `.jpg`, `.jpeg`, `.png`, `.bmp`, `.webp`
- Recent history uses HashSet (not ideal LRU, but simple and good enough for 20 items)

**Error Handling:**
- Network failures return stale cached data when available
- GitHub rate limiting logged with reset time
- Missing topics/images logged but don't crash
- Download failures trigger retry with different image

### 2026-04-24: Cross-Agent Integration with Marty (Phase 3)

Marty (Phase 3) built the tray icon component (H.NotifyIcon.WinUI) and app startup integration. The tray menu calls `IWallpaperService.ChangeWallpaperAsync()` to change wallpapers on demand, fully utilizing Biff's Phase 2 services:
- GitHubImageService for topic/image discovery
- CacheService for image caching and LRU eviction
- WallpaperService for orchestration and retry logic

No API changes required. Marty's TrayIconView component cleanly integrates all Phase 2 interfaces without modification.

### 2026-04-24: Phase 5 Background Scheduler Complete

**Core Scheduler Implementation:**
- **SchedulerService** - Implements both `ISchedulerService` and `IHostedService`
  - Uses `PeriodicTimer` (modern .NET approach) for interval-based wallpaper changes
  - Reads `AppSettings.IntervalMinutes` to configure timer period
  - Changes wallpaper immediately on first start (no initial wait)
  - On each timer tick: calls `IWallpaperService.ChangeWallpaperAsync()`
  - Tracks `NextChangeTime` property for UI display
  - Exception handling in tick loop - logs errors but continues running (no crash)
  
**Dynamic Settings Integration:**
- Listens to `ISettingsService.SettingsChanged` event
- When settings change (new interval):
  1. Cancels current timer safely
  2. Waits for timer task to complete
  3. Disposes old timer and cancellation token
  4. Creates new timer with updated interval
  5. Updates `NextChangeTime` to reflect new schedule
- Thread-safe timer restart using lock on shared state

**Windows Startup Integration:**
- **StartupManager** - Registry-based "Start with Windows" support
  - Uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  - `SetStartWithWindows(bool)` - Adds/removes registry entry with quoted exe path
  - `IsStartWithWindows()` - Checks if registry entry exists
  - Gets executable path from `Assembly.GetExecutingAssembly().Location`
  - Handles both .dll and .exe paths (converts .dll → .exe for WinUI apps)
  - Comprehensive error logging for registry operations

**DI Registration Pattern:**
- SchedulerService registered three ways in `App.xaml.cs`:
  1. As singleton `SchedulerService` (concrete class)
  2. As `ISchedulerService` interface (resolves to same instance)
  3. As `IHostedService` (resolves to same instance for host lifecycle)
- This pattern ensures single instance with multiple interface access points
- Generic host automatically calls `StartAsync()`/`StopAsync()` on app start/shutdown

**Key Design Decisions:**
- `PeriodicTimer` instead of `System.Timers.Timer` or `Task.Delay` loops (recommended modern pattern)
- Async/await throughout with proper cancellation token support
- Event handler for settings change is async void (safe in this context - all exceptions caught and logged)
- Lock-based thread safety for timer restart to prevent race conditions
- Immediate wallpaper change on startup improves first-run experience
- NextChangeTime exposed for UI status display

**NuGet Package Added:**
- `System.Net.Http.Json` 10.0.7 - Required for `ReadFromJsonAsync` extension method in MainWindow.xaml.cs
- This resolved build error where `HttpContent.ReadFromJsonAsync` was not found

**Integration Notes:**
- Scheduler uses existing `IWallpaperService` interface - no changes needed
- No direct UI dependencies - fully headless background operation
- Respects settings changes without restart
- Clean shutdown via `IHostedService` lifecycle

**Build Status:** 
- `dotnet build` succeeds with 2 warnings (unused event in TrayIconView - pre-existing)
- All new services compile and integrate cleanly
