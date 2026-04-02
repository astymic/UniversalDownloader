# Changing the Application Icon

The app currently uses `akashi.ico` as its icon.  
The icon appears in: the **taskbar**, the **window title bar**, **Alt+Tab**, and the **EXE file** itself.

---

## Where the Icon is Configured

In `Downloader\Universal Downloader.csproj`:

```xml
<ApplicationIcon>akashi.ico</ApplicationIcon>
```

```xml
<ItemGroup>
    <None Remove="akashi.ico" />
</ItemGroup>
<ItemGroup>
    <Resource Include="akashi.ico" />
</ItemGroup>
```

- `<ApplicationIcon>` — embeds the icon into the EXE (visible in File Explorer)
- `<Resource Include="...">` — makes the `.ico` available to WPF at runtime (for the window icon)

---

## Step-by-Step: Change the Icon in Visual Studio

### 1. Prepare your `.ico` file

- Must be `.ico` format (not `.png` or `.jpg` directly)
- Recommended: include multiple sizes in one `.ico` file: **16×16, 32×32, 48×48, 256×256**
- Tools to create `.ico` files:
  - **IcoFX** (paid, professional)
  - **GIMP** (free) — File → Export As → `.ico`
  - **ConvertICO** — https://convertico.com (online, free)
  - **RealFaviconGenerator** — https://realfavicongenerator.net

### 2. Add the new icon file to the project

**Option A — Visual Studio:**
1. Copy your new `.ico` file into the `Downloader\` folder (same folder as the `.csproj`)
2. In **Solution Explorer**, right-click the `Downloader` project → **Add → Existing Item...**
3. Select your new `.ico` file
4. In the **Properties** panel (bottom-right, press F4 if not visible):
   - Set **Build Action** = `Resource`

**Option B — Manual (edit `.csproj`):**
1. Copy `mynewicon.ico` into `Downloader\`
2. Edit `Universal Downloader.csproj` — replace `akashi.ico` with `mynewicon.ico` in:
   ```xml
   <ApplicationIcon>mynewicon.ico</ApplicationIcon>
   ...
   <None Remove="mynewicon.ico" />   <!-- or delete this line -->
   ...
   <Resource Include="mynewicon.ico" />
   ```

### 3. Update the `ApplicationIcon` property

In Visual Studio:
1. Right-click the `Universal Downloader` project → **Properties**
2. Go to the **Application** tab
3. Find the **Icon** field
4. Click **Browse...** → select your new `.ico` file
5. Save (`Ctrl+S`)

This automatically updates `<ApplicationIcon>` in the `.csproj`.

### 4. Rebuild the project

```
Ctrl+Shift+B
```

Or right-click project → **Rebuild**.

The new icon will appear on the EXE and in the window titlebar.

---

## Important: The Window Icon vs. EXE Icon

There are **two separate** icon settings:

| Icon | Where it shows | How it's set |
|---|---|---|
| **EXE icon** | File Explorer, taskbar (pinned), Alt+Tab | `<ApplicationIcon>` in `.csproj` |
| **Window icon** | Custom titlebar (if coded) | WPF `Window.Icon` property in XAML or C# |

In this project the window uses a **fully custom titlebar** (`WindowStyle="None"`) so there is no default WPF window icon visible. The EXE icon is what matters for the taskbar.

If you want to set the window's taskbar icon explicitly in XAML:
```xml
<Window ...
        Icon="akashi.ico">
```

Or in C# (`App.xaml.cs` or `MainWindow.xaml.cs`):
```csharp
this.Icon = new BitmapImage(new Uri("pack://application:,,,/akashi.ico"));
```

---

## Quick Icon Replacement (CLI)

If you just want to swap the file without touching Visual Studio:

1. Replace `Downloader\akashi.ico` with your new `.ico` file (keep the same filename)
2. Rebuild:
   ```powershell
   dotnet build -c Release
   ```

No `.csproj` changes needed since the filename is the same.
