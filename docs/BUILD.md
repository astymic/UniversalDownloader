# Build Guide — Universal Downloader

## Requirements

| Tool | Version | Where to get |
|---|---|---|
| Visual Studio 2022 | Any edition (Community is free) | https://visualstudio.microsoft.com |
| .NET Desktop Development workload | Included in VS installer | Select during VS install |
| .NET 9 SDK | 9.0.3xx | https://dotnet.microsoft.com/download (auto-installed with VS) |

> **Note:** The project targets `.NET 8.0-windows` but uses the `.NET 9 SDK` (pinned in `global.json`). Both must be installed.

---

## Opening the Project in Visual Studio

1. Double-click `Universal Downloader.sln`  
   — OR —  
   Open Visual Studio → **File → Open → Project/Solution** → select `Universal Downloader.sln`

2. Wait for NuGet packages to restore automatically  
   (Newtonsoft.Json and Ookii.Dialogs.Wpf will be downloaded)

3. If restore does not happen automatically:  
   Right-click the solution in **Solution Explorer → Restore NuGet Packages**

---

## Build Configurations

The project has two standard configurations:

| Configuration | Purpose | Output folder |
|---|---|---|
| **Debug** | Development — no optimizations, full debug symbols | `Downloader\bin\Debug\net8.0-windows\win-x64\` |
| **Release** | Testing the optimized build locally | `Downloader\bin\Release\net8.0-windows\win-x64\` |

Switch configuration using the **dropdown in the toolbar** (next to the green play button):

```
[Debug ▼]  [Any CPU ▼]  ▶ Universal Downloader
```

---

## Building in Visual Studio

### Debug build (run from IDE)

1. Ensure **Debug** is selected in toolbar
2. Press **F5** — builds and launches with debugger attached
3. Press **Ctrl+F5** — builds and launches without debugger (faster startup)
4. Press **Ctrl+Shift+B** — builds only, no launch

### Release build

1. Switch toolbar to **Release**
2. Press **Ctrl+Shift+B**
3. Output EXE: `Downloader\bin\Release\net8.0-windows\win-x64\Universal Downloader.exe`

> **Note:** The Release build is still a framework-dependent multi-file build.  
> For a single self-contained EXE for distribution, use [Publish](PUBLISH.md).

---

## Building from the Command Line

Open a terminal in the solution root (`UniversalDownloader\`).

```powershell
# Restore packages first
dotnet restore

# Debug build
dotnet build -c Debug

# Release build
dotnet build -c Release
```

Run the built EXE directly:
```powershell
.\Downloader\bin\Release\net8.0-windows\win-x64\"Universal Downloader.exe"
```

---

## NuGet Packages

Defined in `Downloader\Universal Downloader.csproj`:

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
<PackageReference Include="Ookii.Dialogs.Wpf" Version="5.0.1" />
```

- **Newtonsoft.Json** — parses yt-dlp JSON output (`-J` flag)
- **Ookii.Dialogs.Wpf** — provides the Vista-style folder browser dialog

To update a package:  
Right-click project → **Manage NuGet Packages** → **Updates** tab.

---

## Troubleshooting Common Build Errors

| Error | Cause | Fix |
|---|---|---|
| `The SDK 'Microsoft.NET.Sdk' was not found` | .NET SDK not installed | Install .NET 9 SDK from microsoft.com |
| `NETSDK1045: current .NET SDK does not support .NET 8` | SDK too old | Update VS or install .NET 9 SDK |
| `Could not restore packages` | No internet / NuGet feed down | Check internet; try again or use VPN |
| `CS0246: type or namespace not found` | Missing NuGet package | Restore NuGet packages (see above) |
| `akashi.ico: not found` | Icon file missing from project folder | Copy `akashi.ico` back to `Downloader\` |
