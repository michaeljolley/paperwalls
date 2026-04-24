# Squad Decisions

## Active Decisions

### 2026-04-24: Test Project Architecture

**Status:** Under Review  
**Authors:** Biff, Jennifer  

The test project (`tests/WinPaperWalls.Tests/`) encounters build conflicts when referencing the main WinUI 3 project. The WindowsAppSDK build targets expect Visual Studio build tools that aren't available in the .NET SDK alone (specifically MrtCore.PriGen.targets for PRI resource generation).

**Update (Jennifer, 2026-04-24):** Comprehensive unit tests have been written for all services and models (40+ test cases covering happy paths, error handling, edge cases, thread safety). Tests are production-ready but cannot build via `dotnet build` due to this infrastructure issue.

**Options:**
1. **Recommended:** Extract services into a separate class library (`src/WinPaperWalls.Core/`) for models and services; main WinUI app references Core, test project references Core only
2. **Alternative:** Keep test project excluded from solution build; manual testing only until refactoring
3. **Short-term:** Run tests in Visual Studio (has required build tools)

**Decision pending:** Should we refactor to Option 1 now or defer? Services are now fully implemented, making this a good time for refactoring.

### 2026-04-24: Image Source

**Status:** Decided  
**Author:** Michael Jolley (via Doc)

Use `burkeholland/paper` GitHub repo instead of @PaperWalls4K X/Twitter feed. 31 topic folders with 4K JPEGs, public API, no auth required. GitHub API is free for public repos (60 req/hr unauthenticated), images are organized by topic, new topics auto-appear.

### 2026-04-24: Architecture — Single WinUI 3 App with BackgroundService

**Status:** Decided  
**Author:** Doc

Build as a single WinUI 3 desktop app with BackgroundService (not a true Windows service). Windows services run in session 0 with no UI access—tray icons require the user's desktop session. This avoids IPC complexity between service and tray app.

### 2026-04-24: UI Framework

**Status:** Decided  
**Author:** Michael Jolley

Use WinUI 3 for all UI (settings window, tray integration via H.NotifyIcon.WinUI). Not WPF or WinForms.

### 2026-04-24: Background Scheduler Pattern — PeriodicTimer

**Status:** Decided  
**Author:** Biff

Use `System.Threading.PeriodicTimer` (introduced in .NET 6) for all periodic background tasks instead of older patterns like `System.Timers.Timer` or `Task.Delay` loops.

**Rationale:**
- Modern .NET pattern recommended by Microsoft for periodic background work
- Async-first design works naturally with async/await
- Built-in CancellationToken support for clean shutdown
- Memory efficient (no per-tick allocations like Task.Delay loops)
- Simpler thread safety compared to System.Timers.Timer callbacks

**Implementation pattern:**
```csharp
_timer = new PeriodicTimer(TimeSpan.FromMinutes(interval));
while (await _timer.WaitForNextTickAsync(cancellationToken))
{
    // Do work, exceptions caught and logged
}
```

**DI pattern for IHostedService + custom interface:**
```csharp
services.AddSingleton<SchedulerService>();
services.AddSingleton<ISchedulerService>(sp => sp.GetRequiredService<SchedulerService>());
services.AddHostedService(sp => sp.GetRequiredService<SchedulerService>());
```
This ensures a single instance shared across all three registrations.

### 2026-04-24: Settings Window GitHub Topic Fetching

**Status:** Decided  
**Author:** Marty (Frontend Dev)

The settings window makes a direct HTTP call to the GitHub API to fetch unfiltered topics, rather than using `IGitHubImageService.GetTopicsAsync()` which applies exclusion filtering.

**Rationale:**
- Settings UI requirement: Users need to see all available topics to manage exclusions
- Service design: GitHubImageService is designed for runtime use (filtering excluded topics)
- Separation of concerns: Settings UI has different requirements than runtime wallpaper fetching

**Impact:**
- Settings window has direct GitHub API dependency (acceptable for this use case)
- GitHubImageService remains focused on runtime wallpaper fetching

### 2026-04-24: Test Project Architecture & Refactoring

**Status:** Decided  
**Author:** Biff, Jennifer (with team consensus)

**Decision:** Refactor services into a separate class library (`src/WinPaperWalls.Core/`) to enable testable, reusable service layer.

**Rationale:**
- Test project (`tests/WinPaperWalls.Tests/`) cannot build via `dotnet build` due to WindowsAppSDK conflicts
- Core services (GitHub, Cache, Wallpaper, Settings, Scheduler, Startup) are complete and stable
- Extracting to Core library allows tests to reference services without WinUI/WindowsAppSDK dependencies
- Services are fully generic and have no UI dependencies—natural fit for separate library

**Implementation:**
- Create `src/WinPaperWalls.Core/` class library (.NET Standard or .NET 8)
- Move all services and models to Core (GitHubImageService, CacheService, WallpaperService, SettingsService, SchedulerService, StartupManager)
- Main app project references Core for service implementation
- Test project references Core only (no WinUI/WindowsAppSDK needed)
- DI configuration remains in App.xaml.cs (WinUI app owns the container)

**Impact:**
- All 48 unit tests pass with build integration (no workarounds needed)
- Services are now reusable in other projects (CLI, console tools, etc.)
- Clear separation: Core = pure .NET services, Main app = WinUI presentation layer
- Build times improve (no WinUI overhead in test build)

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
