# Universal Downloader

A self-contained Windows desktop app (WPF / .NET 8) for downloading files from YouTube, Spotify, SoundCloud, Google Drive, and any direct URL — with a single URL paste.

## Features

- 🎥 **YouTube** — select resolution (4K → 360p) or audio-only (MP3)
- 🎵 **Spotify / SoundCloud** — download as MP3 via yt-dlp
- 📁 **Google Drive** — direct file download
- 🔗 **Any direct URL** — raw file download with progress
- ✂️ **Trim** — cut a time range from YouTube videos before saving
- ⚡ **Zero setup** — yt-dlp and FFmpeg are auto-downloaded and kept up to date
- 🌑 **Premium dark UI** — custom chrome, gradient progress bar, glassmorphism status panel

## Documentation

| File | Description |
|---|---|
| [docs/PROJECT.md](docs/PROJECT.md) | Full project architecture and codebase description |
| [docs/BUILD.md](docs/BUILD.md) | How to build (Debug & Release) in Visual Studio and CLI |
| [docs/PUBLISH.md](docs/PUBLISH.md) | How to publish a single-file executable for distribution |
| [docs/ICON.md](docs/ICON.md) | How to change the application icon |
| [docs/VERSIONING.md](docs/VERSIONING.md) | How to set app version, product name, and assembly info |

## Quick Start

```
Requirements: Visual Studio 2022 (any edition) with .NET Desktop workload
SDK: .NET 9 SDK (pinned via global.json)
```

1. Open `Universal Downloader.sln` in Visual Studio 2022
2. Press `F5` to build and run in Debug mode
3. On first launch the app auto-downloads `yt-dlp.exe` and `ffmpeg.exe`
