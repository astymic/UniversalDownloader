# Upcoming Releases & Feature Roadmap

This document tracks planned features, architectural requirements, and specifications for upcoming releases of **Universal Downloader**.

---

## 📋 Feature Priority Backlog

### 1. 🎬 "Download Full Season / All Episodes" (Batch Anime Queue)
* **Goal**: Enable 1-click downloading of entire anime seasons or multi-episode series from the YummyAnime catalog.
* **Key Capabilities**:
  * **"Download All Episodes" Header Action**: Place a prominent batch button inside the episode selector view.
  * **Preferred Dub / Voiceover Selection**: Inherits current voiceover choice (e.g. AniLibria, SHIZA Project, Subtitles) across all episodes.
  * **Automatic Resolution Waterfall**: Prioritizes Alloha (1080p) ➔ Kodik (720p) ➔ Sibnet / CVH fallback per episode.
  * **Structured Directory Hierarchy**: Automatically organizes downloads into subfolders:
    ```
    Downloads/
    └── Anime/
        └── [Anime Title]/
            ├── S01E01 - [AniLibria 1080p].mp4
            ├── S01E02 - [AniLibria 1080p].mp4
            └── ...
    ```
  * **Batch Queue Integration**: Push items sequentially or in parallel into `DownloadQueueManager` with individual progress tracking, pause/resume, and retry support.

---

### 2. 🔌 "Shut Down PC When Downloads Finish" (Queue Automation)
* **Goal**: Allow users to leave large download queues or season batches running unattended (e.g. overnight).
* **Key Capabilities**:
  * **UI Toggle**: Checkbox in the Queue tab: `[ ] Shut down PC when queue completes`.
  * **Execution Options**:
    * Power Off / Shutdown (`shutdown /s /t 60`)
    * Sleep / Hibernate (`rundll32.exe powrprof.dll,SetSuspendState`)
  * **Safety Countdown Dialog**: A 60-second warning modal with a `"Cancel Shutdown"` button in case the user is still at the computer.
  * **Trigger Hook**: Invoked in `DownloadQueueManager` when all active and pending downloads reach `Completed` state without errors.

---

### 3. 🎞️ Advanced GIF & WebP Clip Creator (Enhanced Compression & Quality Control)
* **Goal**: Expand the Media Converter and Trimmer into a dedicated high-fidelity GIF & animated WebP generator with granular quality and compression presets.
* **Key Capabilities**:
  * **Dual Output Formats**: Animated `.gif` and modern `.webp` (smaller file size, 24-bit color, alpha transparency support).
  * **Compression & Quality Profiles**:
    * 🌟 **Maximum Quality (Lossless / High-Fidelity)**:
      * FFmpeg two-pass palette generation (`palettegen` + `paletteuse=dither=sierra2_4a`).
      * High frame rate (30 fps / original fps), 0% lossy artifacts, full resolution.
    * ⚖️ **Balanced (Discord / Web Sharing Optimized)**:
      * Adaptive 256-color palette with dithering.
      * Capped resolution (720p / 480p) and 20–24 fps.
      * Target file size limiter (e.g. under 10 MB or 25 MB for Discord Nitro/free limits).
    * 🗜️ **Maximum Compression (Compact Sticker / Emoji Size)**:
      * Aggressive lossy WebP compression or low-palette GIF (64–128 colors).
      * Frame skipping (12–15 fps) and scale down (320px / 256px).
  * **Interactive Trimming Controls**: Start and end timestamps previewed directly from the video trimmer slider.
  * **Speed & Looping Options**: Custom playback speed (0.5x, 1x, 1.5x, 2x) and loop count (Infinite vs N times).

---

*Last updated: 2026-09-05*
