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

### 3. 🎞️ Advanced GIF & WebP Clip Creator (Enhanced Compression & Quality Control) ✅ *Completed & Released*
* **Status**: Implemented with dedicated studio view, built-in preview player, precision loop trimmer, two-pass adaptive palettegen for GIF, animated WebP encoding, customizable presets (Max Quality, Balanced, Max Compression, Target Size, Custom), and Download History integration.

---

### 4. 🗜️ "Smart Video Compressor" (Lossless / High-Efficiency Video Compression) ✅ *Completed & Released (v1.0.14)*
* **Status**: Implemented with GPU/CPU acceleration, ETA timers, elapsed time tracking, folder drag-and-drop, and Discord target sizing.

---

*Last updated: 2026-09-13*

