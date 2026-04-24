# Decisions

### 2026-04-24T03:02:00Z: Image source
**By:** Michael Jolley (via Doc)
**What:** Use burkeholland/paper GitHub repo instead of @PaperWalls4K X/Twitter feed as the image source. 31 topic folders with 4K JPEGs, public API, no auth required.
**Why:** X API requires authentication and has strict rate limits. GitHub API is free for public repos (60 req/hr unauthenticated), images are organized by topic, and new topics auto-appear.

### 2026-04-24T03:02:00Z: Architecture — single WinUI 3 app with BackgroundService
**By:** Doc
**What:** Build as a single WinUI 3 desktop app with BackgroundService, not a true Windows service. Windows services run in session 0 with no UI access — tray icons require the user's desktop session.
**Why:** Avoids IPC complexity between a service and a tray app. Single process handles both background rotation and tray UI.

### 2026-04-24T03:02:00Z: UI framework — WinUI 3
**By:** Michael Jolley
**What:** Use WinUI 3 for all UI (settings window, tray integration via H.NotifyIcon.WinUI). NOT WPF or WinForms.
**Why:** User preference. Modern Windows UI framework.

### 2026-07-25T00:00:00Z: Enable Native AOT Compilation
**By:** Biff
**Status:** Implemented  
**Branch:** squad/enable-native-aot  
**What:** Enable `PublishAot=true` unconditionally in the main app project for compile-time trimming analysis and native AOT release builds.
**Key Changes:** 
- Source-generated JSON via AppJsonContext 
- All DllImport → LibraryImport (compile-time marshalling)
- Assembly.Location → Environment.ProcessPath (AOT-safe)
- Extracted shared GitHubContentItem model
**Why:** Windows App SDK 1.8 supports Native AOT for WinUI 3 apps. .NET 10 resolved the known AOT+WinUI 3 hang issue. Enables faster startup and self-contained release binaries.
**Impact:** Release binaries are native and self-contained; all 48 tests pass; CI adds AOT publish verification; build times unchanged for debug workflows; publish times ~2-5x longer due to native compilation.
