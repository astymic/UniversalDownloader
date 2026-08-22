# Universal Downloader

A modern, high-performance, cross-platform desktop application (**Windows & Linux**) for searching, identifying, downloading, converting, and queuing media files from YouTube, Spotify, SoundCloud, TikTok, Instagram, Twitter/X, and over 1,000+ supported sites.

---

## 🐧 Cross-Platform & Linux Support
- **Universal Linux Binary**: Standalone, self-contained single-file executable for Ubuntu, Debian, Fedora, Arch Linux, Manjaro, SteamOS, and generic Linux x64 distributions.
- **Arch Linux PKGBUILD**: Included `PKGBUILD` and `.desktop` launcher for fast Arch Linux (`makepkg -si`) installation.
- **Hardware-Accelerated UI**: Built with Avalonia UI (SkiaSharp) for silky smooth 60/120/144Hz performance on both X11 and Wayland.

---

## 🌟 Key Features

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

### 📻 Live Stream & DJ Mix Scraper (Continuous PC Audio Recognition)
- **Continuous Ambient Listening** — 1-click start/stop toggle button to passively listen to all music playing on your PC (streams, DJ sets, movies, podcasts).
- **Configurable Global Hotkey** — Press `[F9]` (or custom key configured in Settings) from anywhere to instantly start or pause continuous listening.
- **Smart Duplicate Suppression** — Tracks are cross-referenced in real-time so no song is added more than once, with an intelligent 25s cooldown during playback.
- **Real-Time Feed & Session History** — Logs detected tracks with live timestamps, album art, 1-click individual downloads, and persistent history with session dates.
- **Batch Download** — 1-click `[⬇ Download All]` to enqueue every song detected during a live stream.

### 📥 Multi-Platform Downloader
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

---

## 🎨 Modern UI & Glassmorphism

- Custom borderless dark glassmorphism theme with animated glow accents and drop shadows.
- High-performance GPU-accelerated drawer transitions (`TranslateTransform`) ensuring silky 60/120/144Hz framerates.
- Compact quick-access right navigation rail for **Search**, **Shazam**, **Spotify**, **History**, **Converter**, and **Queue**.

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
- Windows 10 / 11 (64-bit)
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
