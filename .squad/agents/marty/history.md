# Project Context

- **Owner:** Michael Jolley
- **Project:** win-paperwalls — A Windows service with system tray icon that fetches 4K wallpapers from the @PaperWalls4K X/Twitter account and rotates the desktop background on a configurable schedule.
- **Stack:** C# / .NET, WinUI 3 (settings UI / tray), Windows Service
- **Created:** 2026-04-24

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-04-24 - Phase 3: System Tray Icon Implementation

**What was built:**
- System tray icon using H.NotifyIcon.WinUI (v2.1.4) package
- Tray context menu with Next Wallpaper, Settings, and Exit options
- Modified app startup to launch minimized to tray (no visible window)
- MainWindow hides instead of closing when user clicks X
- P/Invoke for User32.ShowWindow to properly hide windows
- Placeholder tray-icon.ico created (16x16, minimal)

**Key decisions:**
- TrayIconView created as a UserControl (separate from App.xaml) for cleaner separation
- Used H.NotifyIcon.WinUI's TaskbarIcon component with ContextFlyout for menu
- ICommand implementation for double-click to open settings
- MainWindow registered as singleton in DI, created on-demand when Settings is clicked
- Exit properly calls Application.Current.Exit() to terminate the app completely

**Integration notes:**
- Biff had already created IWallpaperService, IGitHubImageService, ICacheService with full implementations
- Added Microsoft.Extensions.Http using statements to CacheService and GitHubImageService (they use IHttpClientFactory)
- App.xaml.cs already had HttpClient registration and all service DI setup from Biff's work
- WallpaperService.ChangeWallpaperAsync() is fully implemented and can be called from tray menu

**Cross-Agent Notes:**
- Biff (Phase 2) built GitHubImageService, CacheService, and WallpaperService with full implementations and proper DI registration
- All services exposed via well-defined interfaces: IGitHubImageService, ICacheService, IWallpaperService
- App.xaml.cs DI setup from Phase 2 already includes IHttpClientFactory and all service registrations
- Marty (Phase 3) did not need to modify any of Biff's services—tray menu integration was clean

**Next Steps:**
- Phase 4: Scheduler service for automatic wallpaper rotation based on `IntervalMinutes`
- Consider: Unit tests once test project architecture is resolved (see decisions.md)
- Consider: Toast notifications for wallpaper change feedback

### 2026-04-24 - Phase 4: WinUI 3 Settings Window

**What was built:**
- Complete settings window UI replacing placeholder
- Rotation interval selector with 7 preset options (30min to weekly) using ComboBox
- Topic selector with dynamic GitHub API loading using ItemsRepeater
- Select All/Deselect All buttons for topic management
- Wallpaper style selector (Fill/Fit/Stretch/Tile/Center/Span)
- Cache management section with live size display and clear functionality
- Start with Windows toggle using ToggleSwitch
- Save button with success notification using InfoBar
- Loading states with ProgressRing for async GitHub fetch
- Error handling with InfoBar for GitHub API failures

**Technical implementation:**
- Uses System.Net.Http.Json for ReadFromJsonAsync extension method
- TopicItem class implements INotifyPropertyChanged for two-way binding
- Fetches all topics directly from GitHub API (bypasses service's exclusion filter)
- Maps ComboBox selections via Tag properties for clean value extraction
- Settings loaded on first window activation (not in constructor)
- ICacheService.GetCacheSizeBytes() used for cache display
- All settings properly map to/from AppSettings model
- Window hides to tray instead of closing (preserves state)

**UI/UX decisions:**
- ScrollViewer with max-width 600px for clean desktop experience
- Sections separated by SubtitleTextBlockStyle headers
- InfoBars for success/error feedback (auto-hide after 3 seconds for success)
- Topics displayed in scrollable bordered container (max 200px height)
- NumberBox for cache size (100-2000 MB range with spinner)
- ContentDialog for cache clear confirmation and errors

**Integration with existing services:**
- ISettingsService for load/save operations
- IGitHubImageService interface defined but needed direct HTTP call for unfiltered topics
- ICacheService for GetCacheSizeBytes() and ClearCacheAsync()
- All services retrieved via App.Services.GetRequiredService<T>()
- Settings changes trigger ISettingsService.SettingsChanged event

**Data binding approach:**
- ObservableCollection<TopicItem> for dynamic topic list
- ItemsRepeater with DataTemplate for CheckBox items
- Two-way binding on TopicItem.IsSelected property
- Tag-based value extraction from ComboBox selections

**Next Steps:**
- Scheduler service needs to listen for SettingsChanged event
- Consider implementing StartupManager for "Start with Windows" functionality
- May need toast notifications when settings are saved
- Future: Add wallpaper preview in settings window

### 2026-04-24 - Cross-Phase: Phase 5 Scheduler & Startup Manager (Biff)

**What Biff built in Phase 5:**
- SchedulerService with PeriodicTimer for automatic wallpaper rotation at user-configured interval
- Immediate wallpaper change on app startup (no initial wait)
- Dynamic settings integration: listens to ISettingsService.SettingsChanged event and restarts timer with new interval
- Thread-safe timer restart logic with proper disposal of old timer and cancellation tokens
- NextChangeTime property exposed for UI display
- StartupManager for registry-based "Start with Windows" support
  - Uses HKCU\Software\Microsoft\Windows\CurrentVersion\Run
  - SetStartWithWindows(bool) and IsStartWithWindows() methods
  - Handles .dll→.exe path conversion for WinUI apps
- DI registration pattern: SchedulerService as singleton accessible via SchedulerService, ISchedulerService, and IHostedService
- Generic host automatically invokes StartAsync/StopAsync on app startup/shutdown

**Integration with Marty's Phase 4 work:**
- Scheduler listens to SettingsChanged event fired by Phase 4 settings window
- StartupManager called from SettingsWindow when user toggles "Start with Windows" toggle
- Phase 4 settings window serves as UI for scheduler interval configuration
- Settings persistence via ISettingsService properly flows to scheduler configuration

**Impact on Phase 4 functionality:**
- Phase 4 settings window now fully functional: interval changes trigger scheduler restart with no manual app restart needed
- "Start with Windows" toggle in Phase 4 now backed by StartupManager registry operations
- Scheduler provides automatic wallpaper rotation when app is running
- Combined with Phase 3 tray menu, user has both automatic (scheduler) and manual (Next Wallpaper menu item) control

### 2026-04-24 - Bugfix: Tray Icon Not Appearing in Debug Mode

**Root cause (two issues):**
1. `TrayIconView` was created as an orphaned UserControl never added to a visual tree — H.NotifyIcon.WinUI's `TaskbarIcon` requires either a loaded visual tree or an explicit `ForceCreate()` call to create the Win32 notify icon.
2. `IconSource="ms-appx:///Assets/logo.ico"` doesn't resolve in unpackaged apps (`WindowsPackageType=None`). The `ms-appx:///` URI scheme is for MSIX-packaged apps only.

**Fix applied:**
- Removed `IconSource` from XAML; set it in code-behind using `Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico")` for filesystem-based resolution
- Called `TrayIcon.ForceCreate()` after setting properties in `TrayIconView` constructor — this creates the Win32 notify icon without needing a visual tree
- Made `TrayIconView` implement `IDisposable` to properly dispose the `TaskbarIcon`
- Updated `App.Exit()` to call `_trayIcon.Dispose()` instead of just nulling the reference

**Key learnings for unpackaged WinUI 3 apps:**
- `ms-appx:///` URI scheme does NOT work for unpackaged apps — use `AppContext.BaseDirectory` for asset paths
- H.NotifyIcon.WinUI's `TaskbarIcon.ForceCreate()` is essential when the control isn't in a loaded visual tree
- `TaskbarIcon` must be disposed on exit to remove the ghost icon from the system tray

### 2026-04-25 - Tray Icon Migration: H.NotifyIcon.WinUI → WinUIEx

**What changed:**
- Replaced H.NotifyIcon.WinUI (v2.1.4) with WinUIEx (v2.9.0) for system tray icon
- Deleted TrayIconView.xaml and TrayIconView.xaml.cs — no longer need a UserControl wrapper
- Tray icon now created directly in App.xaml.cs using WinUIEx's `TrayIcon` class (window-less pattern)
- Upgraded Microsoft.WindowsAppSDK from 1.6.240923002 to 1.8.250907003 (required by WinUIEx 2.9.0)
- Upgraded Microsoft.Windows.SDK.BuildTools from 10.0.26100.1742 to 10.0.26100.4654

**Why:**
- H.NotifyIcon.WinUI's TaskbarIcon required a visual tree or ForceCreate() hack — click handlers were broken
- WinUIEx's TrayIcon class is designed for window-less tray apps with native MenuFlyout support
- WinUIEx is battle-tested (used by PowerToys, Files, Windows Dev Home)

**Key implementation details:**
- `new TrayIcon(id, iconPath, tooltip)` with filesystem path to Assets/logo.ico
- `icon.Selected` event for left-click → opens settings window

### 2025-01-09 - Documentation Update: README Refresh

**What changed:**
- Updated README.md to reflect current project state (tech stack, features, build process)
- Added project logo (128px width) at top with centered HTML img tag
- Updated tech stack: .NET 9 → .NET 10, H.NotifyIcon.WinUI → WinUIEx 2.9.0, Windows App SDK 1.6 → 1.8
- Added Serilog to tech stack documentation (already implemented for logging)
- Simplified build instructions (removed implicit dotnet restore, removed dotnet run for WinUI app)
- Added File Logging and Bug Reporting features to features list
- Added "Report bug" option to tray menu documentation
- Enhanced Architecture section with internal sealed services, Serilog logging, MSIX packaging notes
- Added Testing section referencing 48 xUnit tests

**Why:**
- README was significantly outdated (referenced .NET 9 and old tray icon library)
- Logging and bug reporting features were implemented but not documented
- Build instructions didn't match actual workflow (WinUI apps run via F5 or exe, not dotnet run)
- Project maintainability improved by having authoritative, accurate documentation
- `icon.ContextMenu` event builds MenuFlyout with Refresh, Settings, Exit items
- Task.Run() wrapper kept on ChangeWallpaperAsync() to avoid UI thread deadlock
- TrayIcon.Dispose() called in App.Exit() to prevent ghost icons

**Impact:**
- All 48 tests still pass (no test changes needed — tests didn't reference tray icon types)
- Test project WindowsAppSDK version updated to match main project
- Build warning: NETSDK1198 about missing publish profile (pre-existing, not from this change)

### 2026-04-25 - Real-Time Wallpaper Style Preview in Settings

**What was built:**
- Live wallpaper style preview: changing the Style ComboBox immediately applies the selected style to the current desktop wallpaper
- Revert on close: if user closes Settings without clicking Save, the wallpaper style reverts to the previously saved value
- Settings reload on each window open: `Activated` handler re-subscribes in `OnWindowClosed` so settings refresh every time the window is shown

**Technical implementation:**
- Added `DesktopWallpaper.GetCurrentWallpaperPath()` — reads current wallpaper path from `HKCU\Control Panel\Desktop` registry key
- Added `GetCurrentWallpaperPath()` to `IDesktopWallpaperService` interface and `DesktopWallpaperService` implementation
- `_settingsLoaded` flag prevents `SelectionChanged` handler from firing during initial `LoadSettingsAsync()` population
- `_savedStyle` field tracks the last-saved style for revert logic
- `Task.Run()` wraps `SetWallpaper` calls to avoid UI thread deadlock from `SystemParametersInfo` with `SPIF_SENDCHANGE`
- `OnWindowClosed` resets `_settingsLoaded = false` and re-subscribes `Activated += OnWindowActivated` for fresh reload on next open

**Key learnings:**
- `SystemParametersInfo` with `SPIF_SENDCHANGE` broadcasts `WM_SETTINGCHANGE` to all top-level windows, which can deadlock the WinUI 3 UI thread — always use `Task.Run()`
- WinUI 3 `ComboBox.SelectionChanged` fires during programmatic `SelectedIndex` changes, so a guard flag is essential to distinguish user interaction from code-driven selection
- The `Activated` event pattern (unsubscribe on first fire, re-subscribe on close) works well for "reload settings each time window appears" without duplicate handler accumulation

**Files modified:**
- `src/WinPaperWalls/Interop/DesktopWallpaper.cs` — added `GetCurrentWallpaperPath()`
- `src/WinPaperWalls/Services/IDesktopWallpaperService.cs` — added method to interface
- `src/WinPaperWalls/Services/DesktopWallpaperService.cs` — implemented new method
- `src/WinPaperWalls/MainWindow.xaml.cs` — live preview, revert, and settings reload logic

### 2025-01-10 - Settings UX Overhaul

**What was built:**
- Custom title bar using `ExtendsContentIntoTitleBar = true` + `SetTitleBar()` with Mica backdrop
- Renamed all "WinPaperWalls" references to "PaperWalls" in the Settings UI
- Grouped settings into "General" and "Background" sections with SubtitleTextBlockStyle headers
- Replaced ItemsRepeater with virtualized ListView (MaxHeight=300) for topic list to prevent infinite scroll
- General section: Rotation interval, Cache, Maximum cache size, Start with Windows
- Background section: Wallpaper style, Wallpaper topics (expander)

**Key design decisions:**
- ListView with `SelectionMode="None"` replaces ItemsRepeater — built-in virtualization handles long topic lists
- MaxHeight="300" on ListView keeps topics scrollable internally without expanding the page
- Section headers use SubtitleTextBlockStyle per Fluent Design guidelines (20px)
- Margin="0,24,0,8" for section spacing between groups (4px grid system)
- Custom title bar uses CaptionTextBlockStyle for the app name in the drag region

**Files modified:**
- `src/WinPaperWalls/MainWindow.xaml` — full layout restructure
- `src/WinPaperWalls/MainWindow.xaml.cs` — added ExtendsContentIntoTitleBar and SetTitleBar() in constructor

### 2025-01-10 - Settings Layout & Scroll Fixes

**What changed:**
- Moved "Start with Windows" toggle to be the last setting in the Backgrounds section (after Topics expander), per Michael's request
- Bumped settings window size 25% from 600×800 to 750×1000 for more breathing room
- Fixed topic list scrolling: changed ListView from `MaxHeight="300"` to `Height="300"` — a fixed height gives the ListView a definite viewport so its built-in ScrollViewer activates properly inside the SettingsCard container

**Key learning:**
- Inside a `SettingsCard` (which uses Auto-sizing), `MaxHeight` on a ListView doesn't create a proper scrollable region because the parent doesn't constrain it enough. Using a fixed `Height` forces the ListView to define its own viewport, enabling its built-in virtualized scrolling.

**Files modified:**
- `src/WinPaperWalls/MainWindow.xaml` — reordered Start with Windows, fixed ListView Height
- `src/WinPaperWalls/MainWindow.xaml.cs` — updated window Resize dimensions



## 2026-04-24T17:37:00Z — Settings Layout Fixes
**Status:** SUCCESS
- Move Start with Windows to last in Backgrounds
- Bump window size 25% to 750×1000
- Fix topic ListView scrolling with Height=300
- Build: 0 errors | Tests: 48 pass
- Committed and pushed

### 2026-04-24 - Topic Selector Pill/Tag UI Redesign

**What was built:**
- Replaced checkbox-based topic list with modern AutoSuggestBox + removable pill/tag UI
- AutoSuggestBox for real-time topic search and filtering
- Selected topics displayed as pill badges with X button for removal
- ItemsRepeater with UniformGridLayout for flexible pill layout

**Technical implementation:**
- Added `SelectedTopics` and `TopicSuggestions` ObservableCollections to SettingsViewModel
- `FilterTopics()` method: searches topic list, updates suggestions as user types
- `AddTopic()` method: adds topic to selected list, clears input, refreshes suggestions
- `RefreshSelectedTopics()` method: reloads selected topics from settings
- `RemoveCommand` on `TopicItemViewModel` with parameter binding for pill removal
- Pill styling: TextBlock in HyperlinkButton with accent hover effect
- Suggestions dropdown populated by filtered results

**User experience improvements:**
- Type-to-search for faster topic selection (no scrolling needed)
- Visual feedback with pill badges and hover states
- One-click removal via X button on each pill
- Keyboard support: Enter to add, Backspace to remove (if input empty)

**Files modified:**
- `src/WinPaperWalls/MainWindow.xaml` — added AutoSuggestBox, pill ItemsRepeater
- `src/WinPaperWalls/MainWindow.xaml.cs` — AutoSuggestBox event handlers
- `src/WinPaperWalls/ViewModels/SettingsViewModel.cs` — new collections, filter/add/refresh methods

**Design decisions:**
- AutoSuggestBox placed above pills for natural top-to-bottom scanning
- UniformGridLayout for pills enables flexible wrapping (responsive to window width)
- Pill styling matches Fluent Design accent colors
- No external pill UI library — implemented with HyperlinkButton + TextBlock
- Backward compatible: saved selected topics still load correctly

**Impact:**
- All 48 tests pass
- Build: 0 errors
- Cleaner, more modern settings UX
- Better discoverability for topic selection (searchable vs. static list)

### 2026-04-24 - Settings UI Refinements: Exclusion Model & Expander Layout

**What was built:**
- Widened settings window from 750px to 900px for better breathing room
- Flipped topic selection from "include" model to "exclude" model
- Moved topic UI into a SettingsExpander for better visual hierarchy

**Technical changes:**

*Window sizing:*
- Changed window size from 750×1000 to 900×1000 in MainWindow.xaml.cs line 25

*Exclusion model logic (SettingsViewModel.cs):*
- `RefreshSelectedTopics()`: Now populates from `TopicItems.Where(t => !t.IsSelected)` (excluded topics shown as pills)
- `FilterTopics()`: Now filters `t.IsSelected` topics (included topics can be searched to exclude)
- `AddTopic()`: Now sets `IsSelected = false` to exclude a topic
- `RemoveCommand`: Now sets `IsSelected = true` to re-include the topic
- `SelectAllTopics`: Includes all topics (removes all exclusions)
- `DeselectAllTopics`: Excludes all topics (shows all as pills)

*XAML structure (MainWindow.xaml):*
- Wrapped topic UI in a `SettingsExpander` with `IsExpanded="True"`
- Header: "Excluded topics", Description: "Topics excluded from wallpaper rotation"
- Loading indicator moved to expander header content area
- Single nested `SettingsCard` with `ContentAlignment="Vertical"` and `HorizontalContentAlignment="Stretch"` for full-width content
- InfoBar, search grid, and pills all contained in one vertical StackPanel inside the card
- AutoSuggestBox placeholder: "Search topics to exclude..."
- Button labels: "Include all" and "Exclude all"

**Key learning:**
- `ContentAlignment="Vertical"` on inner SettingsCard prevents right-aligned content (default behavior when SettingsCard is in SettingsExpander.Items)
- `HorizontalContentAlignment="Stretch"` ensures full-width content utilization
- SettingsExpander provides better visual grouping and collapsible UI for complex settings sections

**Files modified:**
- `src/WinPaperWalls/MainWindow.xaml.cs` — window width 750 → 900
- `src/WinPaperWalls/ViewModels/SettingsViewModel.cs` — flipped all topic selection logic to exclusion model
- `src/WinPaperWalls/MainWindow.xaml` — restructured to SettingsExpander, updated labels/placeholders

**Impact:**
- All 48 tests pass
- Build: 0 errors
- Clearer mental model: "exclude topics you don't want" instead of "include topics you want"
- Better visual hierarchy with expandable topic section
- More horizontal space for content with 900px width
