# Universal Downloader

A premium, self-contained Windows desktop application (WPF / .NET) for downloading video and audio files from YouTube, Spotify, SoundCloud, Google Drive, and any direct URL — with a single URL paste.

---

## 🌟 Premium Features

- 🎥 **YouTube Integration** — Paste a YouTube video or playlist URL. Select your desired resolution (from 4K down to 360p) or download audio-only (MP3/Best Audio).
- 🎵 **Spotify CSV Drawer & History** — A dedicated, hardware-accelerated sliding side panel for Spotify playlist imports.
  - **Exportify Integration** — Easily export playlists of any size (even thousands of tracks) to CSV on exportify.net (completely free, bypasses Spotify's 2026 API restrictions).
  - **Imports History** — Session-based in-memory catalog saves previous imports. Clicking any previous import restores the playlist tracks and their exact checklist/selection states.
  - **Auto-Collapse & Focus** — Side drawer smoothly slides shut when a playlist is selected, immediately focusing the track queue.
- 📁 **Google Drive & Direct URLs** — Direct downloads for raw file URLs and Google Drive links.
- ✂️ **Precision Time Trimming** — Crop a specific start/end time range from YouTube videos before downloading, with custom slider controls.
- ⚡ **Zero Setup Dependency Manager** — On launch, the application automatically downloads and updates the latest `yt-dlp.exe` and `ffmpeg.exe` binaries behind the scenes.
- 🌑 **Stunning Premium UI** — A modern dark glassmorphism design with a custom border chrome, glow dropshadows, gradient progress bars, and high-performance GPU-accelerated slide transitions.

---

## 📂 Documentation

| Guide | Description |
|---|---|
| 📐 [docs/PROJECT.md](docs/PROJECT.md) | Full project architecture and codebase description |
| 🛠️ [docs/BUILD.md](docs/BUILD.md) | How to build (Debug & Release) in Visual Studio and CLI |
| 📦 [docs/PUBLISH.md](docs/PUBLISH.md) | How to publish a single-file executable for distribution |
| 🎨 [docs/ICON.md](docs/ICON.md) | How to change the application icon |
| 🏷️ [docs/VERSIONING.md](docs/VERSIONING.md) | How to set app version, product name, and assembly info |

---

## 🚀 Quick Start

### Prerequisites
- Visual Studio 2022 (any edition) with the **.NET Desktop Development** workload installed.
- **.NET 9 SDK** (automatically pinned via `global.json`).

### Running the App
1. Clone the repository and navigate to the project directory.
2. Open `Universal Downloader.sln` in Visual Studio 2022.
3. Press `F5` to build and run in Debug mode.
4. Alternatively, use the CLI:
   ```powershell
   dotnet build
   dotnet run --project Downloader
   ```
5. On the first launch, the application will automatically fetch `yt-dlp.exe` and `ffmpeg.exe` and place them in the application data directory.

---

## 🎨 UI Architecture & Animations

The side drawer animation utilizes **GPU-accelerated horizontal translation** on `TranslateTransform.XProperty` of the drawer's `RenderTransform`.
- Shifting coordinates between `X = 260` (collapsed, exposing only the 60px green Spotify button) and `X = 0` (expanded, showing all 320px) avoids expensive CPU layout passes (`Measure`/`Arrange`).
- This guarantees butter-smooth 60/120/144Hz animations without a single frame drop, even on low-end hardware.
- A semi-transparent dark overlay (`MainContentOverlayBorder`) provides a sleek frosted-glass effect over the main window, blocking other interactions and automatically closing the panel on click.
