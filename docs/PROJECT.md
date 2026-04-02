# Project Architecture — Universal Downloader

## Overview

Universal Downloader is a WPF desktop application targeting `.NET 8.0-windows`.  
It uses two external CLI tools at runtime — **yt-dlp** and **FFmpeg** — which it downloads and manages automatically.

---

## Tech Stack

| Layer | Technology |
|---|---|
| UI Framework | WPF (Windows Presentation Foundation) |
| Target Runtime | .NET 8.0-windows |
| Build SDK | .NET 9 SDK (pinned via `global.json`) |
| JSON Parsing | Newtonsoft.Json 13.0.3 |
| Folder Dialog | Ookii.Dialogs.Wpf 5.0.1 |
| Download Engine | yt-dlp (auto-downloaded at runtime) |
| Trimming Engine | FFmpeg (auto-downloaded at runtime) |

---

## Solution & Project Structure

```
UniversalDownloader/
│
├── Universal Downloader.sln          ← Solution file (open this in VS)
├── global.json                        ← Pins .NET 9 SDK version
├── README.md
├── docs/
│   ├── PROJECT.md   (this file)
│   ├── BUILD.md
│   ├── PUBLISH.md
│   ├── ICON.md
│   └── VERSIONING.md
│
└── Downloader/                        ← The single WPF project
    ├── Universal Downloader.csproj    ← Project file
    ├── App.xaml / App.xaml.cs         ← App entry point, temp directory setup
    ├── AssemblyInfo.cs                ← WPF theme resource location hint
    ├── akashi.ico                     ← Application icon (embedded resource)
    │
    ├── MainWindow.xaml                ← All UI layout, styles, templates
    ├── MainWindow.xaml.cs             ← Core UI logic: URL routing, download orchestration
    ├── MainWindow.Chrome.cs           ← Custom window chrome (minimize/maximize/close/drag)
    ├── MainWindow.Settings.cs         ← JSON settings persistence (last download path)
    ├── MainWindow.Downloads.cs        ← (Partial class, reserved for future download-list logic)
    ├── MainWindow.YtDlp.cs            ← (Partial class, reserved for yt-dlp helpers)
    │
    ├── Utilities.cs                   ← File name sanitization, byte formatting, visual tree helpers
    │
    └── Services/
        ├── DependencyManager.cs       ← Auto-download & auto-update of yt-dlp + FFmpeg
        ├── DownloadService.cs         ← yt-dlp invocation, HTTP direct download, progress parsing
        └── DownloadService.Trim.cs    ← FFmpeg trimming pipeline (partial class of DownloadService)
```

---

## Key Classes & Responsibilities

### `App` (`App.xaml.cs`)
- Creates a unique temp directory per session (`%TEMP%\UniversalDownloader_<guid>`)
- Cleans up temp directory on exit

### `MainWindow` (split across 5 partial class files)
- **xaml.cs** — main orchestrator:
  - Detects link type (YouTube / Spotify / Google Drive / direct)
  - Calls `DependencyManager` on startup to init tools
  - Handles URL text change → fetches YouTube info → populates quality ComboBox
  - Invokes the correct `DownloadService` method based on link type
  - Manages trim slider state (Canvas-based dual-thumb range slider)
  - Disables/enables UI elements based on app state (initializing / processing / downloading)
- **Chrome.cs** — custom window behaviour:
  - Drag to move, double-click to pseudo-maximize, minimize/restore/close buttons
  - "Pseudo-maximize" avoids the WPF `WindowState.Maximized` taskbar overlap bug with `WindowStyle=None`
- **Settings.cs** — JSON file at `%LOCALAPPDATA%\UniversalDownloader\settings.json`:
  - Persists `LastDownloadPath` across sessions
- **Downloads.cs / YtDlp.cs** — empty partial class stubs for future expansion

### `DependencyManager` (`Services/DependencyManager.cs`)
- Checks for `yt-dlp.exe` and `ffmpeg.exe` next to the executable
- On first run: downloads them from their official GitHub release URLs
- On subsequent runs: checks the latest yt-dlp version tag via GitHub API and auto-updates if behind
- FFmpeg is only downloaded once (no version check — the build is stable)
- Reports progress back via `ProgressUpdated` event → shown in `StatusTextBlock`

### `DownloadService` (`Services/DownloadService.cs`)
- **`IsYouTubeLink` / `IsGoogleDriveLink` / `IsSpotifyLink` / `IsSoundCloudLink`** — regex URL matchers
- **`GetYouTubeInfoAsync`** — runs `yt-dlp -J` to get video JSON (title, duration, format list)
- **`ExtractQualitiesFromYouTubeInfo`** *(in MainWindow.xaml.cs)* — parses JSON → `List<YouTubeQualityItem>`
- **`DownloadWithYtDlpAsync`** — builds yt-dlp arguments and streams progress from stdout
- **`DownloadDirectFileAsync`** — raw HTTP download with `HttpClient`, chunked streaming, byte-level progress
- **`ParseYtDlpProgress`** — regex parses yt-dlp `[download] X% of Y` lines
- **`ParseFfmpegProgress`** — regex parses FFmpeg `time=HH:MM:SS` lines for trim progress

### `DownloadService.Trim` (`Services/DownloadService.Trim.cs`)
- Runs after `DownloadWithYtDlpAsync` when trimming is enabled
- FFmpeg arguments: `-ss <start> -i <file> -t <duration> -c copy -movflags +faststart`
- Streams copy (no re-encode) for video — blazing fast
- Re-encodes for audio extraction (libmp3lame / aac / flac / pcm_s16le)
- Deletes the full downloaded file once trimmed copy is saved

---

## Data Flow — YouTube Download

```
User pastes YouTube URL
        │
        ▼
UrlTextBox_TextChanged → ProcessUrlChange()
        │
        ├─► DependencyManager.IsYtDlpReady? ──No──► Show error
        │
        ├─► DownloadService.GetYouTubeInfoAsync()   [yt-dlp -J]
        │         └─► Parse JSON → title, duration, formats
        │
        ├─► Populate YouTubeQualityComboBox
        ├─► Show QualitySection + TrimmingSection
        │
User clicks "Start Download"
        │
        ▼
DownloadButton_Click → DownloadService.DownloadWithYtDlpAsync()
        │         [yt-dlp -f <format> -o <temp>]
        │         └─► ParseYtDlpProgress → StatusTextBlock
        │
        ├─► [if trim enabled] TrimLocalVideoAsync()
        │         [ffmpeg -ss -i -t -c copy]
        │         └─► ParseFfmpegProgress → StatusTextBlock
        │
        └─► CopyToFinalDestinationAndClean() → Done ✓
```

---

## UI Design System (XAML Resources)

All styles and brushes are defined in `MainWindow.xaml` `<Window.Resources>`:

| Resource Key | Type | Value |
|---|---|---|
| `PrimaryBrush` | SolidColorBrush | `#8B5CF6` (purple) |
| `AccentBrush` | SolidColorBrush | `#EC4899` (pink) |
| `SuccessBrush` | SolidColorBrush | `#22C55E` (green) |
| `BackgroundBrush` | SolidColorBrush | `#09090B` |
| `SurfaceBrush` | SolidColorBrush | `#18181B` |
| `PrimaryGradient` | LinearGradientBrush | Purple → Pink |
| `GlowEffect` | DropShadowEffect | Purple glow for buttons |
| `ModernButton` | Style\<Button\> | Gradient bg, scale animation on hover |
| `SecondaryButton` | Style\<Button\> | Bordered, surface bg |
| `ModernTextBox` | Style\<TextBox\> | Rounded corners, focus border |
| `ModernComboBox` | Style\<ComboBox\> | Custom dropdown with arrow toggle |
| `ModernProgressBar` | Style\<ProgressBar\> | Gradient fill with glow |
| `ModernCheckBox` | Style\<CheckBox\> | Custom rounded checkbox |
