# Corporate PCs, Permission Prompts & Drag-and-Drop Troubleshooting Guide

This guide explains the standard fix for permission prompts and blocked Drag-and-Drop when running **Universal Downloader** on corporate, managed, or restricted Windows PCs.

---

## 🔍 The Issue

When portable Windows applications are placed in system directories such as:
- C:\Program Files\
- C:\Program Files (x86)\
- C:\Windows\

Windows applies strict **User Account Control (UAC)** and **User Interface Privilege Isolation (UIPI)** security policies:

1. **Permission Prompts**: Windows requires Administrator elevation to write to these directories, resulting in annoying UAC prompts or enterprise IT blocks on launch and update.
2. **Blocked Drag-and-Drop (UIPI)**: Windows UIPI strictly blocks drag-and-drop messages from lower-integrity processes (such as standard File Explorer or web browsers) to elevated (Admin) processes. As a result, dragging URLs, audio files, or video files into the application is blocked with a 🚫 (no-entry) cursor.
3. **Compatibility Shim Lock-In**: Windows Program Compatibility Assistant (PCA) often silently tags the executable with a RUNASADMIN registry flag under HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers, forcing future launches to request administrator privileges even when unnecessary.

---

## ⚡ The Standard Fix (Default Recommended Setup)

### ✅ Rule 1: Always Run from User-Writable Folders (Default Location)
Never place the standalone portable .exe directly into C:\Program Files (x86)\ or C:\Program Files\ without an administrative installer.

**Recommended standard locations:**
- **Local AppData (Default Best Practice)**:  
  %LOCALAPPDATA%\UniversalDownloader\  
  *(e.g., C:\Users\<Username>\AppData\Local\UniversalDownloader\)*
- **Desktop**:  
  %USERPROFILE%\Desktop\UniversalDownloader\
- **User Documents or Tools**:  
  %USERPROFILE%\Applications\UniversalDownloader\

> **Why?** Standard user directories are owned by your user account. The app has full write permissions to save configs, dependencies (yt-dlp, fmpeg), and updates without ever needing or asking for administrative privileges.

---

### 🔧 Step-by-Step Fix if Permission Prompts or Drag-and-Drop is Blocked

If your application currently asks for permissions or Drag-and-Drop is blocked:

#### 1. Move to a User Directory
1. Close Universal Downloader.
2. Move the Universal Downloader folder from C:\Program Files (x86)\ to your **Desktop** or %LOCALAPPDATA%\UniversalDownloader\.

#### 2. Clear Compatibility Flags (RUNASADMIN)
1. Right-click Universal Downloader.exe ➔ select **Properties**.
2. Click the **Compatibility** tab.
3. Verify that **Run this program as an administrator** is **UNCHECKED**. If checked, uncheck it and click **Apply**.
4. *(Optional via PowerShell)*: Remove any lingering Windows PCA registry shims:
   `powershell
   Remove-ItemProperty -Path HKCU:\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers -Name *Universal Downloader.exe -ErrorAction SilentlyContinue
   `

#### 3. Unblock the File (SmartScreen / Mark-of-the-Web)
If downloaded from a browser on a corporate network:
`powershell
Unblock-File -LiteralPath .\Universal Downloader.exe
`
Or right-click Universal Downloader.exe ➔ **Properties** ➔ check **Unblock** at the bottom ➔ click **OK**.

#### 4. Launch the Application
Launch Universal Downloader.exe normally (double-click without Run as Administrator).  
Drag-and-Drop will now work flawlessly from File Explorer, Chrome, Edge, and other apps.

---

## 🛡️ Built-in Engine Protections in Universal Downloader

Universal Downloader includes built-in safeguards to automatically protect against these issues:

1. **Explicit sInvoker Application Manifest**:
   - Downloader/app.manifest explicitly defines:
     `xml
     <requestedExecutionLevel level=asInvoker uiAccess=false />
     `
   - This explicitly instructs Windows that the application does NOT require elevation, preventing Windows from auto-prompting for admin rights.

2. **Automatic AppCompatFlags Registry Purge**:
   - On startup, the application checks HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers and automatically strips any forced RUNASADMIN flags attached to its executable name.

3. **Win32 UIPI Message Filter Bypass**:
   - The application invokes ChangeWindowMessageFilterEx on initialization for WM_DROPFILES, WM_COPYDATA, and WM_COPYGLOBALDATA, allowing file drop messages even if the process environment has mixed integrity.

4. **Zero-Prompt Silent Updater Fallback**:
   - When checking for updates, the updater tests whether the application folder is writable.
   - If running from a read-only directory (such as C:\Program Files (x86)\Downloader\), it automatically copies the updated binary to %LOCALAPPDATA%\UniversalDownloader\Universal Downloader.exe and launches it without using -Verb RunAs or prompting for UAC.
