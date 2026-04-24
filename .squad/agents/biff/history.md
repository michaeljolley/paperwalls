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
- Listens to ISettingsService.SettingsChanged event from Phase 4 settings window

### 2026-04-24: Cross-Phase Integration with Marty (Phase 4)

Marty (Phase 4) built the complete settings window UI with all configuration options including rotation interval selector, dynamic topic selector, wallpaper style picker, cache management, and Start-with-Windows toggle. Phase 5 scheduler integrates seamlessly:

- Scheduler reads `AppSettings.IntervalMinutes` set by Phase 4 settings window
- Listens to `SettingsChanged` event from settings window and automatically restarts timer with new interval
- StartupManager is called from SettingsWindow when user toggles "Start with Windows" setting
- No API changes required between phases - all integration through existing interfaces and event patterns

**Build Status:** 
- `dotnet build` succeeds
- All scheduler and startup manager services compile and integrate cleanly
- Settings window properly triggers scheduler restart on interval change

### 2026-04-23: Testability Improvements for Unit Tests

**Problem Context:**
Jennifer's comprehensive unit tests (40+ test cases) uncovered three testability issues preventing 11 tests from passing:

**Issue 1: CacheService Directory Not Injectable**
- Problem: _cacheDirectory hardcoded to %LOCALAPPDATA%\WinPaperWalls\cache
- Tests writing to temp directory but EvictOldestAsync/ClearCacheAsync operated on wrong location
- Fix: Added optional cacheDirectory parameter to constructor
  - Production code: 
ull defaults to %LOCALAPPDATA% path
  - Test code: passes temp directory for isolation
- Interface (ICacheService) unchanged - no API breaking changes

**Issue 2: Static DesktopWallpaper.SetWallpaper Not Mockable**
- Problem: Direct static call prevented mocking in tests
- Tests failed with FileNotFoundException on mock paths like C:\test\image1.jpg
- Fix: Created abstraction layer for testability
  - New interface: IDesktopWallpaperService
  - New wrapper: DesktopWallpaperService (calls static method)
  - WallpaperService now injects IDesktopWallpaperService instead of calling static directly
  - Registered IDesktopWallpaperService in DI container (App.xaml.cs)
- Tests can now mock the interface without file system dependencies

**Issue 3: SchedulerService Doesn't Handle 0-Minute Interval**
- Problem: Test setting IntervalMinutes = 0 creates TimeSpan.Zero for PeriodicTimer
- Causes undefined behavior in timer loop
- Fix: Enforce minimum 1-minute interval in two places:
  - StartAsync(): ar intervalMinutes = Math.Max(1, settings.IntervalMinutes);
  - OnSettingsChanged(): Same guard on settings reload
- Prevents edge case while maintaining realistic production behavior

**Design Patterns Applied:**
- **Dependency Injection for External Dependencies** - Wrap static/non-mockable APIs (file system, OS calls) behind interfaces
- **Optional Parameters for Test Hooks** - Production code uses sensible defaults, tests override with controlled values
- **Input Validation** - Guard against edge cases (zero interval) that break invariants

**Testability Principle:**
Any code that touches external state (file system, registry, OS APIs) should be injectable. This allows tests to verify logic without side effects.

**Build Status:**
- Main project (src/WinPaperWalls/WinPaperWalls.csproj): ✅ Builds successfully
- Test project: Expected errors (Jennifer will update test mocks to match new signatures)
- Zero breaking changes to public APIs or production behavior

### 2026-07-14: Code Cleanup - Unused Usings and Dead Code

**PR #9:** Removed unused using statements, sorted imports, removed dead fields.

**Changes:**
- Removed unused `using System.Text` from SettingsServiceTests.cs
- Removed unused `using Microsoft.Extensions.Http` from GitHubImageServiceTests.cs and CacheServiceTests.cs
- Removed unused `using System.Net.Http.Json` from GitHubImageServiceTests.cs
- Sorted using statements in MainWindow.xaml.cs (System.* first convention)
- Removed unused `_gitHubImageService` field from MainWindow (GitHub API calls use raw HttpClient in GetAllTopicsFromGitHubAsync)
- Removed unused `_allTopics` field from MainWindow (replaced with local variable)
- Made `_topicItems` field readonly

**Key Finding:** MainWindow injects IGitHubImageService but doesn't use it - the settings UI bypasses the service to get unfiltered topics directly from GitHub API. This is by design (see decisions.md).

### 2026-04-24: Code Cleanup Complete

**Agent:** Biff (Code Cleanup Phase)  
**Timestamp:** 2026-04-24T16:27:08Z  
**Status:** ✅ Complete

**PR #9 Summary:**
- Removed 4 unused `using` statements from test files
- Sorted and organized imports in MainWindow.xaml.cs
- Removed 2 dead fields: `_gitHubImageService`, `_allTopics`
- Build: ✅ Clean
- Tests: ✅ 48 tests pass
- No behavioral changes to application

**Impact:** Code is now more maintainable with cleaner imports and no dead code references.
