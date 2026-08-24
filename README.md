# Universal Downloader

A modern, high-performance, cross-platform desktop application (**Windows & Linux**) for searching, identifying, downloading, converting, and queuing media files from YouTube, Spotify, SoundCloud, TikTok, Instagram, Twitter/X, and over 1,000+ supported sites.

---

## 🐧 Cross-Platform & Linux Support
- **Universal Linux Binary**: Standalone, self-contained single-file executable for Ubuntu, Debian, Fedora, Arch Linux, Manjaro, SteamOS, and generic Linux x64 distributions.
- **Arch Linux PKGBUILD**: Included `PKGBUILD` and `.desktop` launcher for fast Arch Linux (`makepkg -si`) installation.
- **Hardware-Accelerated UI**: Built with Avalonia UI (SkiaSharp) for silky smooth 60/120/144Hz performance on both X11 and Wayland.

---

## 🌟 Key Features

### 📻 Live Stream & DJ Scraper ("Passive DJ Mode")
- **Continuous Ambient Listening** — 1-click start/stop toggle button (or press global hotkey `[F9]`) to passively listen to all music playing on your PC (streams, DJ sets, mixes, movies, podcasts, gaming).
- **10s Recognition Cycle** — Efficient continuous sampling loop (5s capture + query + 5s cooldown) identifying songs non-stop with minimal CPU and network footprint.
- **Smart Duplicate Suppression** — Tracks are cross-referenced in real-time so no song is added more than once, with an intelligent 25-second cooldown during playback.
- **Real-Time Live Track Feed** — Songs dynamically appear in real-time on your screen with title, artist, time badge, and 1-click download/queue/search buttons.
- **📜 Expandable Past Sessions Accordion**:
  - Saved session history tagged with date, time, and song count.
  - **Interactive Dropdown**: Click any session card or `[▼ View Tracks]` to drop down and inspect all individual tracks in that session.
  - **Single-Track Downloads**: Download or queue individual tracks directly from past sessions without having to download the entire session.
  - **Batch Actions**: `[⬇ Download All]` to enqueue the entire session, or `[📂 Load Tracks]` to restore it into the live feed.

### 🔍 Dual-Mode Search Hub
- **🎵 Smart Music Hub (Default)** — Multi-source parallel search across YouTube & SoundCloud with intelligent subword/closest-match fallback.
- **📺 Real YouTube Search** — Authentic YouTube search results matching YouTube.com's live ranking with full video metadata.
- **⚡ Instant 0ms Tab Switching** — Both engines query simultaneously in parallel in the background, allowing instant switching between modes.

### 🎙️ Shazam & Audio Song Recognition ("Listen & Identify")
- **Instant 5-Second Acoustic Fingerprinting** — Identifies playing songs using STFT spectral landmark peak extraction and Shazam's acoustic database (100M+ songs).
- **Dual Audio Sources**:
  - **🔊 PC Audio (WASAPI Loopback, Default)**: Direct internal loopback capture of whatever music is currently playing on your computer (browser, Spotify, games, Twitch, etc.).
  - **🎤 Microphone**: Captures ambient audio from speakers or humming.
- **Real-Time Visualizer & Auto-Search** — Live RMS volume meter, visual countdown, and automatic search retrieval upon song detection for immediate 1-click download.

### 📥 Multi-Platform Downloader & Isolated Jobs
- **Isolated Download Pipelines** — Every download runs in an isolated sandbox subdirectory to prevent cross-job interference, ensuring zero partial (`.part`) files leak into output folders.
- **Strict Audio Extraction** — When downloading audio, video containers (`.mp4`, `.webm`) are automatically cleaned up so only the desired `.mp3` is saved.
- **YouTube & Playlists** — Video and audio downloads with customizable quality, playlist indexing, and metadata tagging.
- **Spotify CSV & Exportify Integration** — Dedicated sliding side drawer for importing Spotify playlists of any size via CSV with track checklists and state restoration.
- **SoundCloud, Google Drive & Direct URLs** — Seamless downloading from direct file links, cloud storage, and video portals.
- **Precision Time Trimming** — Crop custom start and end timestamps before downloading.
- **Multi-Connection Acceleration (`aria2c`)** — Optional multi-threaded connection acceleration for maximum bandwidth utilization.
- **Clipboard Auto-Detection** — Automatically captures media links copied to your clipboard.

### 📋 Download Queue Manager
- Batch queue management with sequential background downloads.
- Real-time progress bars, pause/cancel controls, retry capability, and a live counter badge in the navigation rail.

### 🔄 Built-in Media Converter
- Convert local or downloaded files between **MP4, MKV, AVI, MOV, WebM, MP3, AAC, FLAC, WAV, and OGG**.
- Select target video/audio quality, format presets, and automatic FFmpeg conversion pipelines.

### 📜 Download History & Metadata
- Filterable and searchable history log with direct file opening, folder navigation, and URL copying.
- Automatic ID3 tag embedding (Artist, Title, Album, Thumbnail Artwork).
- Optional auto-download for **Synchronized Lyrics (`.lrc`)** companion files (configurable in Settings).

### ⚡ Zero-Setup Dependency Manager
- Automatically downloads, verifies, and updates `yt-dlp.exe`, `ffmpeg.exe`, `ffprobe.exe`, and `aria2c.exe` in the background with zero user setup required.

### 🔄 In-App Auto-Updater (GitHub Releases)
- **Automatic Background Version Checks** — Checks GitHub Releases for new updates on launch.
- **Top Header Update Button** — When a new version is released, an `✨ Update (vX.X.X)` button dynamically appears in the top window bar next to Settings.
- **Cross-Platform OS Detection** — Automatically detects Windows vs Linux and matches the correct platform package/binary asset.
- **1-Click In-App Modal** — View version comparison, changelog, and click `[⬇ Update & Restart]` to automatically download, install, and restart into the latest version.
- **Manual Checks in Settings** — Always available `[🔄 Check for Updates]` button in the Settings menu.

---

## 🎨 Modern UI & Glassmorphism

- Custom borderless dark glassmorphism theme with animated glow accents and drop shadows.
- High-performance GPU-accelerated drawer transitions (`TranslateTransform`) ensuring silky 60/120/144Hz framerates.
- Compact quick-access right navigation rail for **Search**, **Shazam**, **Live Stream / DJ Scraper**, **Spotify**, **History**, **Converter**, and **Queue**.

---

## 📂 Documentation & Architecture

| Guide | Description |
|---|---|
| 📐 [docs/PROJECT.md](docs/PROJECT.md) | Full project architecture, modules, and codebase design |
| 🛠️ [docs/BUILD.md](docs/BUILD.md) | How to build (Debug & Release) in Visual Studio and CLI |
| 📦 [docs/PUBLISH.md](docs/PUBLISH.md) | How to publish a single-file portable executable |
| 🎨 [docs/ICON.md](docs/ICON.md) | How to change application icons and assets |
| 🏷️ [docs/VERSIONING.md](docs/VERSIONING.md) | Versioning and assembly metadata configuration |

---

## 🚀 Getting Started

### Prerequisites
- Windows 10 / 11 (64-bit) or Linux (x64)
- Visual Studio 2022 (with **.NET Desktop Development** workload) OR [.NET 8 / 9 SDK](https://dotnet.microsoft.com/download)

### Build & Run

1. **Clone the repository**:
   ```bash
   git clone https://github.com/astymic/UniversalDownloader.git
   cd UniversalDownloader
   ```

2. **Build via CLI**:
   ```powershell
   dotnet build "Downloader/Universal Downloader.csproj" -c Release
   ```

3. **Run the application**:
   ```powershell
   dotnet run --project "Downloader/Universal Downloader.csproj"
   ```

---

## 📄 License
This project is licensed under the MIT License.
