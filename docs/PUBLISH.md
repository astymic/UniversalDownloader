# Publish Guide — Debug → Distribution

This project is configured to publish as a **single self-contained EXE** for Windows x64.  
No .NET runtime installation is required on the end user's machine.

---

## Publish Settings (already configured in `.csproj`)

```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
```

| Setting | Meaning |
|---|---|
| `PublishSingleFile` | Bundles all DLLs into one EXE |
| `SelfContained` | Includes the .NET runtime — no install needed |
| `RuntimeIdentifier: win-x64` | Targets 64-bit Windows only |
| `IncludeNativeLibrariesForSelfExtract` | Bundles native DLLs (WPF rendering, etc.) |

> ⚠️ The published EXE will be **~100–150 MB** because it includes the full .NET runtime.  
> This is normal for self-contained single-file WPF apps.

---

## Method 1 — Publish From Visual Studio (Recommended)

### Step-by-step

1. Right-click the project **`Universal Downloader`** in Solution Explorer
2. Click **Publish...**

   ![Publish menu](https://i.imgur.com/placeholder.png)

3. On the first time, a wizard opens. Choose:
   - **Target:** Folder
   - **Specific target:** Folder
   - **Folder location:** e.g. `publish\` (relative to project) or any absolute path

4. Click **Finish** — the publish profile is created and saved to  
   `Downloader\Properties\PublishProfiles\FolderProfile.pubxml`

5. On the Publish summary screen, verify the settings match:

   | Field | Expected value |
   |---|---|
   | Configuration | Release |
   | Target framework | net8.0-windows |
   | Deployment mode | Self-contained |
   | Target runtime | win-x64 |
   | Produce single file | ✅ Yes |

6. Click the **Publish** button

7. When done, VS shows:  
   `Build succeeded` and the output path (e.g. `Downloader\bin\Release\net8.0-windows\win-x64\publish\`)

8. Your distributable file is:  
   **`Universal Downloader.exe`** in that publish folder

---

## Method 2 — Publish From Command Line

Open a terminal in the solution root:

```powershell
dotnet publish "Downloader\Universal Downloader.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish
```

Output will be in `.\publish\Universal Downloader.exe`.

---

## What to Distribute

You only need to share **one file**:

```
Universal Downloader.exe   ← everything is bundled inside
```

On first launch, the app will auto-download `yt-dlp.exe` and `ffmpeg.exe`  
into the same folder as the EXE. These are **not** bundled in the EXE.

### Final distribution folder after first run:
```
Universal Downloader.exe   ← your shipped file
yt-dlp.exe                 ← auto-downloaded on first run
ffmpeg.exe                 ← auto-downloaded on first run
```

---

## Debug vs Release vs Publish — Key Differences

| Aspect | Debug | Release (build) | Publish |
|---|---|---|---|
| Optimizations | ❌ None | ✅ Yes | ✅ Yes |
| Debug symbols | ✅ Full `.pdb` | Minimal | ❌ None |
| .NET runtime | Required on machine | Required on machine | ✅ Bundled |
| Single EXE | ❌ Many files | ❌ Many files | ✅ One EXE |
| File size | ~5 MB | ~5 MB | ~120–150 MB |
| Console window | Optional | Hidden | Hidden |
| Use case | Development | Local testing | Ship to users |

---

## Switching Between Debug and Release in Visual Studio

The toolbar shows the active configuration:

```
Solution Configurations  →  [Debug ▼] or [Release ▼]
```

- **Debug** — press `F5` or `Ctrl+F5`
- **Release** — switch dropdown to Release → `Ctrl+Shift+B` to build, then run the EXE from `bin\Release\...`
- **Publish** — always uses Release settings regardless of toolbar selection

---

## Re-Publishing After Code Changes

If you already have a publish profile set up:

1. Open **Build → Publish `Universal Downloader`**  
   — OR right-click project → **Publish...**
2. Click **Publish** on the summary screen
3. The previous output is overwritten

From CLI: just re-run the `dotnet publish` command. It overwrites the output folder.

---

## Troubleshooting Publish

| Issue | Fix |
|---|---|
| EXE crashes immediately on another PC | Make sure `win-x64` runtime ID and `SelfContained=true` are set |
| "This app can't run on your PC" | Target PC is 32-bit; change `RuntimeIdentifier` to `win-x86` if needed |
| Publish takes very long | Normal for self-contained — it bundles ~200 assemblies |
| EXE blocked by Windows SmartScreen | Expected for unsigned apps; click "More info → Run anyway" |
| Users need to click "Run anyway" | Sign the EXE with a code-signing certificate to eliminate this |
