# Versioning — App Version, Product Name & Assembly Info

---

## Where Version Info Lives in a .NET 8 WPF Project

Unlike older .NET Framework projects (which used `AssemblyInfo.cs` heavily),  
modern SDK-style projects store version info **directly in the `.csproj` file**.

---

## Setting the Version in Visual Studio

### Method 1 — Project Properties UI (Easiest)

1. Right-click **`Universal Downloader`** project in Solution Explorer
2. Click **Properties**
3. Go to the **Package** tab (or **Application** tab depending on VS version)
4. Fill in:

| Field | Maps to | Example |
|---|---|---|
| Assembly version | `<AssemblyVersion>` | `1.0.0.0` |
| File version | `<FileVersion>` | `1.2.3.0` |
| Package version | `<Version>` | `1.2.3` |
| Product name | `<Product>` | `Universal Downloader` |
| Company | `<Company>` | `Your Name` |
| Description | `<Description>` | `Download anything from anywhere` |
| Copyright | `<Copyright>` | `© 2026 Your Name` |

5. Save (`Ctrl+S`)

---

### Method 2 — Edit `.csproj` Directly (Recommended)

Open `Downloader\Universal Downloader.csproj` and add to the `<PropertyGroup>`:

```xml
<PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ApplicationIcon>akashi.ico</ApplicationIcon>

    <!-- ── Version Info ── -->
    <Version>1.2.3</Version>
    <AssemblyVersion>1.2.3.0</AssemblyVersion>
    <FileVersion>1.2.3.0</FileVersion>
    <InformationalVersion>1.2.3</InformationalVersion>

    <!-- ── Product Info ── -->
    <Product>Universal Downloader</Product>
    <Company>Your Name or Studio</Company>
    <Description>Download YouTube, Spotify, Google Drive, and direct links from one place.</Description>
    <Copyright>© 2026 Your Name. All rights reserved.</Copyright>
    <NeutralLanguage>en-US</NeutralLanguage>
</PropertyGroup>
```

After saving and rebuilding, right-click the EXE → **Properties → Details** tab to verify.

---

## Version Number Conventions

Use **Semantic Versioning**: `MAJOR.MINOR.PATCH`

| Part | When to increment |
|---|---|
| `MAJOR` | Breaking change or complete redesign |
| `MINOR` | New feature added (backwards-compatible) |
| `PATCH` | Bug fix or small improvement |

Examples:
- `1.0.0` — initial public release
- `1.1.0` — added Spotify trimming support
- `1.1.1` — fixed download cancel crash

### `AssemblyVersion` vs `FileVersion` vs `Version`

| Property | Used by | Format |
|---|---|---|
| `Version` | NuGet, dotnet CLI | `1.2.3` |
| `AssemblyVersion` | .NET type loader (CLR) | `1.2.3.0` (4 parts) |
| `FileVersion` | Windows file properties dialog | `1.2.3.0` (4 parts) |
| `InformationalVersion` | Human-readable, shown in About dialogs | `1.2.3-beta` (any string) |

**Best practice:** Keep all in sync. Update all four when releasing.

---

## Showing the Version in the App UI

To display the app version in your window (e.g. title bar or About section):

```csharp
using System.Reflection;

// Get the informational version (e.g. "1.2.3")
string version = Assembly
    .GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "unknown";

// Or get file version (e.g. "1.2.3.0")
string fileVersion = Assembly
    .GetExecutingAssembly()
    .GetName()
    .Version
    ?.ToString() ?? "unknown";

// Example usage in title bar TextBlock:
TitleVersionTextBlock.Text = $"v{version}";
```

In XAML you could also bind to it via a static property on `App`.

---

## Debug vs Release Identification

You can add a conditional version suffix in the `.csproj`:

```xml
<InformationalVersion Condition="'$(Configuration)' == 'Debug'">$(Version)-dev</InformationalVersion>
<InformationalVersion Condition="'$(Configuration)' == 'Release'">$(Version)</InformationalVersion>
```

This way:
- Debug builds show: `1.2.3-dev`  
- Release builds show: `1.2.3`

---

## Checklist Before Publishing a New Release

- [ ] Update `<Version>`, `<AssemblyVersion>`, `<FileVersion>` in `.csproj`
- [ ] Update `<Copyright>` year if needed
- [ ] Switch toolbar to **Release** configuration
- [ ] Build once: `Ctrl+Shift+B` — confirm **0 errors**
- [ ] Run in Release mode and test the main flows
- [ ] Publish: right-click project → **Publish** → click **Publish** button
- [ ] Test the published EXE on a clean machine (or VM without .NET installed)
- [ ] Commit and tag in Git: `git tag v1.2.3`
