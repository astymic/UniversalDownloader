# Universal Downloader Local Rules & Conventions

## UI / UX Windows & Dialogs
- **Mandatory Custom Dialog Rule**: NEVER use default legacy Windows / WPF `MessageBox.Show()`.
- **Always use `ModernMessageBox`**: For all user notifications, errors, warnings, success alerts, and confirmation dialogs.
- **Theming & Aesthetic Consistency**:
  - Dark theme matching the main window (`#09090B` background, `#18181B` surface, `#8B5CF6` primary purple accent).
  - Modern border radius (`CornerRadius="16"`), custom title bar with drag move, and window drop shadow.
  - Themed icons with circular badge glows (Warning: Amber, Error: Red, Info: Blue, Question/Confirm: Purple, Success: Green).
  - Buttons styled with rounded corners, subtle hover animations, and standard keyboard support (`Enter` / `Esc`).
