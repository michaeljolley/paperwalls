<p align="center">
  <img src="src/WinPaperWalls/Assets/logo.png" alt="WinPaperWalls" width="128" />
</p>

# WinPaperWalls

A Windows desktop application that automatically rotates your desktop wallpaper with beautiful 4K images from the [burkeholland/paper](https://github.com/burkeholland/paper) GitHub repository.

## Features

- **Automatic Wallpaper Rotation:** Change your desktop wallpaper on a configurable schedule (30 minutes to weekly)
- **Topic Selection:** Choose from 30+ curated topic categories (nature, abstract, space, cityscapes, etc.)
- **Smart Caching:** Downloaded wallpapers are cached locally with configurable size limits and LRU eviction
- **System Tray Integration:** Runs minimized in the system tray with quick access to settings and manual wallpaper changes
- **Start with Windows:** Optional automatic startup when Windows boots
- **Wallpaper Styles:** Support for Fill, Fit, Stretch, Tile, Center, and Span display modes
- **File Logging:** Serilog-based file logging with daily rotation (14-day retention) to `%LocalAppData%\WinPaperWalls\logs\`
- **Bug Reporting:** One-click bug report from the tray menu — zips all logs to your Desktop

## Tech Stack

- **Framework:** .NET 10 with WinUI 3
- **UI:** WinUI 3 for modern Windows 11-style interface
- **System Tray:** WinUIEx 2.9.0 for tray icon integration
- **Logging:** Serilog for structured file logging with daily rotation
- **Dependency Injection:** Microsoft.Extensions.Hosting and DI
- **Background Processing:** IHostedService with PeriodicTimer for scheduled wallpaper changes
- **Image Source:** GitHub API accessing burkeholland/paper repository
- **Windows App SDK:** 1.8

## Requirements

- Windows 10 version 1809 (17763) or later
- Windows 11 recommended for best WinUI 3 experience
- .NET 10 Runtime (included with the application)

## Building from Source

### Prerequisites

- Visual Studio 2022 (version 17.8 or later) or .NET 10 SDK
- Windows App SDK 1.8 or later

### Build Steps

```bash
# Clone the repository
git clone https://github.com/michaeljolley/win-paperwalls.git
cd win-paperwalls

# Build
dotnet build win-paperwalls.slnx

# Test
dotnet test win-paperwalls.slnx
```

### Visual Studio

1. Open `win-paperwalls.slnx` in Visual Studio 2022
2. Right-click the solution in Solution Explorer and select "Restore NuGet Packages"
3. Press F5 to build and run

## Configuration

Settings are stored in: `%LocalAppData%\WinPaperWalls\settings.json`

The application provides a graphical settings UI accessible from the system tray icon. You can configure:

- **Rotation Interval:** How frequently to change wallpapers
- **Topic Selection:** Which topic categories to include/exclude
- **Wallpaper Style:** How the image is displayed on your desktop
- **Cache Size:** Maximum disk space for cached wallpapers (default 500 MB)
- **Startup Behavior:** Whether to start with Windows

## Usage

1. Launch WinPaperWalls - it will minimize to the system tray
2. Right-click the tray icon to access:
   - **Refresh PaperWall:** Immediately change to a new random wallpaper
   - **Settings:** Open the settings window to configure the application
   - **Report bug:** One-click bug report that zips all logs to your Desktop
   - **Exit:** Close the application
3. The wallpaper will automatically change based on your configured interval

## Cache Management

Downloaded wallpapers are cached in: `%LocalAppData%\WinPaperWalls\cache`

The cache uses a Least Recently Used (LRU) eviction strategy when it exceeds the configured size limit. You can manually clear the cache from the Settings window.

## Architecture

- **Services Layer:** All business logic is in injectable services (SettingsService, GitHubImageService, CacheService, WallpaperService, SchedulerService). All service classes are `internal sealed` with explicit public API surface.
- **Background Scheduler:** Uses .NET PeriodicTimer with IHostedService for reliable background processing
- **Thread Safety:** Settings and cache operations are thread-safe for concurrent access
- **Error Handling:** Comprehensive error handling with fallback to cached data when GitHub API is unavailable
- **Logging:** Serilog with file sink, daily rolling, structured output
- **Packaging:** MSIX via Package.appxmanifest

## Testing

```bash
dotnet test win-paperwalls.slnx
```

48 xUnit tests covering services, models, and edge cases.

## License

See [LICENSE](LICENSE) for details.

## Credits

Wallpaper images provided by the excellent [burkeholland/paper](https://github.com/burkeholland/paper) repository.