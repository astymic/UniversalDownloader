# Universal Downloader Project Rules & Guidelines

## 1. UI & Dialog Design Rules
- **NEVER use standard / legacy Win32 / WPF `MessageBox.Show()`** anywhere in the application.
- **ALWAYS use `ModernMessageBox`** (or the app's custom modern dialog styling) for all alerts, confirmations, errors, information, and prompt windows.
- **Design Specifications for Dialogs & Windows**:
  - Dark Theme matching the app:
    - Window Background: `#09090B` / `#121118`
    - Card / Content Surface: `#18181B`
    - Border: `#2A273F` / `#3F3F46` with `CornerRadius="16"`
    - Window Drop Shadow: `BlurRadius="30" ShadowDepth="8" Opacity="0.7"`
    - Typography: `FontFamily="Inter, Segoe UI, Arial"`
    - Text Colors: Primary `#FAFAFA`, Secondary `#A1A1AA`
  - Custom Title Bar with:
    - Clean drag & move support (`MouseLeftButtonDown -> DragMove()`)
    - Smooth minimalist close button (`✕`)
  - Themed Icon Badges:
    - **Information**: Blue / Indigo badge (`#3B82F6`)
    - **Warning**: Amber / Yellow badge (`#F59E0B`)
    - **Error**: Rose / Red badge (`#EF4444`)
    - **Question / Confirmation**: Purple badge (`#8B5CF6`)
    - **Success**: Emerald Green badge (`#22C55E`)
  - Modern Button Styles:
    - Primary Action: Purple gradient `#8B5CF6` -> `#6D28D9` (with hover glow, rounded corners `CornerRadius="8"`)
    - Secondary Action: Dark surface button `#27272A` with `#FAFAFA` text and border `#3F3F46`
  - Centering: Always set `WindowStartupLocation = CenterOwner` (or `CenterScreen` fallback)
  - Accessibility: Support `Enter` key for Primary action and `Escape` for Cancel/Close.
