# Red Watcher and Carbon Crimson Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the approved Red Watcher logo to LANtern Control and retheme the existing WPF dashboard with the restrained Carbon Crimson palette.

**Architecture:** Implement the logo as a reusable WPF vector control so it remains crisp in the sidebar and tests. Generate the Windows multi-size `.ico` from that same control with a small project-local WPF asset tool, then point the application project at the generated icon. Centralize all theme colors in `App.xaml`; the main window and live chart consume those resources so no retired teal/blue colors remain in runtime UI code.

**Tech Stack:** .NET 8, WPF/XAML, C#, xUnit, `RenderTargetBitmap`, `IconBitmapEncoder`, PowerShell, `dotnet publish`.

## Global Constraints

- Preserve the current dashboard layout, spacing, typography, controls, table behavior, one-second chart sampling, and one-hour history.
- The logo is a geometric eye with a near-black body, crimson outline, dark-red iris, and uninterrupted black pupil.
- Do not include an X, white catchlight, white pupil detail, text inside the mark, or large bloom effects.
- Red remains an accent; large backgrounds stay neutral near-black.
- Keep online indicators muted green and destructive actions distinct.
- Continue producing a self-contained Windows 10/11 x64 executable.

## File structure

- `src/Lantern.App/Controls/RedWatcherLogo.xaml`: reusable vector logo and its named visual parts.
- `src/Lantern.App/Controls/RedWatcherLogo.xaml.cs`: control initialization only.
- `tools/Lantern.BrandAssets/Lantern.BrandAssets.csproj`: WPF-compatible asset generator project referencing `Lantern.App`.
- `tools/Lantern.BrandAssets/Program.cs`: renders the vector control at Windows icon sizes into `Assets/RedWatcher.ico`.
- `src/Lantern.App/Assets/RedWatcher.ico`: generated executable/window icon.
- `src/Lantern.App/App.xaml`: centralized Carbon Crimson resources and control-state colors.
- `src/Lantern.App/MainWindow.xaml`: sidebar logo placement and replacement of remaining hard-coded blue surfaces.
- `src/Lantern.App/MainWindow.xaml.cs`: status-color fallbacks using the approved palette.
- `src/Lantern.App/Controls/LiveTrafficChart.cs`: chart fallback colors and non-white point outline.
- `tests/Lantern.App.Tests/BrandVisualTests.cs`: logo, resource, main-window, and icon regression coverage.
- `src/Lantern.App/Lantern.App.csproj`: application icon and version `0.3.3`.

---

### Task 1: Reusable Red Watcher logo and application icon

**Files:**
- Create: `src/Lantern.App/Controls/RedWatcherLogo.xaml`
- Create: `src/Lantern.App/Controls/RedWatcherLogo.xaml.cs`
- Create: `tools/Lantern.BrandAssets/Lantern.BrandAssets.csproj`
- Create: `tools/Lantern.BrandAssets/Program.cs`
- Create: `src/Lantern.App/Assets/RedWatcher.ico`
- Create: `tests/Lantern.App.Tests/BrandVisualTests.cs`
- Modify: `src/Lantern.App/MainWindow.xaml`
- Modify: `src/Lantern.App/Lantern.App.csproj`

**Interfaces:**
- Produces: `Lantern.App.Controls.RedWatcherLogo : UserControl` with `x:Name="RedWatcherPupil"` and vector-only children.
- Produces: `Assets/RedWatcher.ico` containing 16, 24, 32, 48, 64, 128, and 256 pixel frames.
- Consumes: the existing `Accent`, `AccentDark`, and `WindowBackground` application brushes.

- [ ] **Step 1: Write the failing logo tests**

Create `BrandVisualTests.cs` with an STA helper and these assertions:

```csharp
[Fact]
public void RedWatcher_HasBlackPupilAndRendersWithoutCatchlight()
{
    RunSta(() =>
    {
        EnsureApplicationResources();
        var logo = new RedWatcherLogo();
        var pupil = Assert.IsType<Ellipse>(logo.FindName("RedWatcherPupil"));
        Assert.Equal(Color.FromRgb(2, 2, 3), ((SolidColorBrush)pupil.Fill).Color);
        Assert.Null(logo.FindName("RedWatcherCatchlight"));
        Render(logo, 256, 256);
    });
}

[Fact]
public void MainWindow_UsesRedWatcherAndProjectIconExists()
{
    RunSta(() =>
    {
        EnsureApplicationResources();
        using var window = new MainWindow();
        Assert.IsType<RedWatcherLogo>(window.FindName("BrandLogo"));
    });
    var icon = Path.Combine(ProjectRoot(), "src", "Lantern.App", "Assets", "RedWatcher.ico");
    Assert.True(File.Exists(icon));
    Assert.Equal(new byte[] { 0, 0, 1, 0 }, File.ReadAllBytes(icon)[..4]);
}
```

- [ ] **Step 2: Run the logo tests to verify failure**

Run:

```powershell
$out = 'C:\Users\moham\AppData\Local\Temp\LanternBrandRed\'
dotnet test tests\Lantern.App.Tests\Lantern.App.Tests.csproj -c Release --no-restore -p:BaseOutputPath=$out --filter FullyQualifiedName~BrandVisualTests
```

Expected: compilation fails because `RedWatcherLogo` does not exist.

- [ ] **Step 3: Implement the vector logo control**

Create a 220-by-180 `Viewbox` containing:

```xml
<Path Data="M25,91 C48,53 75,36 110,36 C145,36 172,53 195,91 C172,128 145,145 110,145 C75,145 48,128 25,91 Z"
      Fill="#050506" Stroke="{DynamicResource Accent}" StrokeThickness="7" />
<Ellipse Width="78" Height="78" Fill="#080608" Stroke="#6F101F" StrokeThickness="4" />
<Ellipse Width="60" Height="60" Fill="#B30F28" />
<Ellipse x:Name="RedWatcherPupil" Width="34" Height="34" Fill="#020203" />
```

Do not add a catchlight element. Wrap the control in a square transparent layout so it scales predictably.

- [ ] **Step 4: Place the logo in the sidebar**

Replace the current chart-like sidebar `Path` inside the 42-pixel brand border with:

```xml
<controls:RedWatcherLogo x:Name="BrandLogo" Width="31" Height="31" />
```

Retain the current LANtern/CONTROL wordmark and sidebar layout.

- [ ] **Step 5: Generate the multi-size icon from the same control**

Create a `net8.0-windows` executable tool with `<UseWPF>true</UseWPF>` and a project reference to `Lantern.App`. In an `[STAThread]` entry point, initialize application resources, render `RedWatcherLogo` with `RenderTargetBitmap` at `16, 24, 32, 48, 64, 128, 256`, append each frame to `IconBitmapEncoder`, and save the target path supplied as argument 0.

Run:

```powershell
dotnet run --project tools\Lantern.BrandAssets\Lantern.BrandAssets.csproj -- src\Lantern.App\Assets\RedWatcher.ico
```

Expected: the `.ico` starts with bytes `00 00 01 00` and contains seven frames.

- [ ] **Step 6: Configure the project icon and rerun tests**

Add:

```xml
<ApplicationIcon>Assets\RedWatcher.ico</ApplicationIcon>
```

Run the filtered `BrandVisualTests` command again. Expected: PASS.

- [ ] **Step 7: Commit the logo task**

```powershell
git add src/Lantern.App/Controls/RedWatcherLogo.xaml src/Lantern.App/Controls/RedWatcherLogo.xaml.cs src/Lantern.App/Assets/RedWatcher.ico src/Lantern.App/MainWindow.xaml src/Lantern.App/Lantern.App.csproj tools/Lantern.BrandAssets tests/Lantern.App.Tests/BrandVisualTests.cs
git commit -m "feat: add Red Watcher application branding"
```

### Task 2: Carbon Crimson resource system and dashboard restyle

**Files:**
- Modify: `tests/Lantern.App.Tests/BrandVisualTests.cs`
- Modify: `src/Lantern.App/App.xaml`
- Modify: `src/Lantern.App/MainWindow.xaml`
- Modify: `src/Lantern.App/MainWindow.xaml.cs`
- Modify: `src/Lantern.App/Controls/LiveTrafficChart.cs`

**Interfaces:**
- Consumes: WPF resources resolved by `Application.Current.TryFindResource(string)`.
- Produces: exact `SolidColorBrush` resources named `WindowBackground`, `SidebarBackground`, `Surface`, `SurfaceRaised`, `SurfaceHover`, `InputBackground`, `Border`, `PrimaryText`, `SecondaryText`, `Accent`, `AccentDark`, `DownloadAccent`, `UploadAccent`, `Success`, `Danger`, and `DangerDark`.

- [ ] **Step 1: Write failing palette tests**

Add a theory that initializes `App` and asserts exact resource colors:

```csharp
[Theory]
[InlineData("WindowBackground", "#08090B")]
[InlineData("SidebarBackground", "#060708")]
[InlineData("Surface", "#101114")]
[InlineData("SurfaceRaised", "#171317")]
[InlineData("InputBackground", "#0C0B0D")]
[InlineData("Border", "#2B2226")]
[InlineData("PrimaryText", "#F4EEF0")]
[InlineData("SecondaryText", "#A89095")]
[InlineData("Accent", "#D72C43")]
[InlineData("AccentDark", "#261014")]
[InlineData("DownloadAccent", "#D72C43")]
[InlineData("UploadAccent", "#9B6670")]
[InlineData("Success", "#68C08A")]
[InlineData("Danger", "#F06473")]
public void CarbonCrimson_UsesApprovedColor(string key, string expected)
{
    RunSta(() =>
    {
        EnsureApplicationResources();
        var brush = Assert.IsType<SolidColorBrush>(Application.Current.Resources[key]);
        Assert.Equal((Color)ColorConverter.ConvertFromString(expected), brush.Color);
    });
}
```

- [ ] **Step 2: Run palette tests to verify failure**

Run the filtered `BrandVisualTests` command. Expected: failures showing the current blue/teal values.

- [ ] **Step 3: Replace the central application resources**

Set the resource values exactly as declared by the theory. Add `SidebarBackground=#060708`, set `SurfaceHover=#21171B`, and retain `DangerDark` as a restrained `#2C1117`. Update row/header/switch hard-coded fills in `App.xaml` to dark neutral or wine values derived from those resources.

- [ ] **Step 4: Remove hard-coded blue surfaces from the main window**

Change the sidebar to `{StaticResource SidebarBackground}`. Replace `#081421`, `#0A1A2A`, `#192A43`, and `#091625` with central resources or neutral Carbon Crimson equivalents. Do not change layout dimensions, grid definitions, margins, or typography.

- [ ] **Step 5: Update chart and status fallbacks**

In `LiveTrafficChart`, use the approved RGB fallbacks and draw point outlines with `WindowBackground` instead of `Brushes.White`. In `MainWindow.xaml.cs`, change the status fallback colors to success `104,192,138`, danger `240,100,115`, and secondary `168,144,149`.

- [ ] **Step 6: Run palette tests and the existing UI smoke test**

Run:

```powershell
$out = 'C:\Users\moham\AppData\Local\Temp\LanternBrandGreen\'
dotnet test tests\Lantern.App.Tests\Lantern.App.Tests.csproj -c Release --no-restore -p:BaseOutputPath=$out --filter "FullyQualifiedName~BrandVisualTests|FullyQualifiedName~DeviceViewModelTests.LimitEditors"
```

Expected: PASS with no XAML load or rendering failures.

- [ ] **Step 7: Commit the theme task**

```powershell
git add src/Lantern.App/App.xaml src/Lantern.App/MainWindow.xaml src/Lantern.App/MainWindow.xaml.cs src/Lantern.App/Controls/LiveTrafficChart.cs tests/Lantern.App.Tests/BrandVisualTests.cs
git commit -m "feat: apply Carbon Crimson dashboard theme"
```

### Task 3: Full verification and v0.3.3 package

**Files:**
- Modify: `src/Lantern.App/Lantern.App.csproj`
- Create: `publish/v0.3.3/LANtern Control.exe`
- Create: `outputs/LANtern-Control-v0.3.3.exe` outside the repository worktree

**Interfaces:**
- Consumes: all Task 1 and Task 2 assets/resources.
- Produces: versioned self-contained x64 executable `LANtern-Control-v0.3.3.exe`.

- [ ] **Step 1: Set the application version**

Set `<Version>0.3.3</Version>` in `Lantern.App.csproj`.

- [ ] **Step 2: Run complete test suites with fresh output directories**

```powershell
$coreOut = 'C:\Users\moham\AppData\Local\Temp\LanternBrandCore033\'
$appOut = 'C:\Users\moham\AppData\Local\Temp\LanternBrandApp033\'
dotnet test tests\Lantern.Core.Tests\Lantern.Core.Tests.csproj -c Release --no-restore -p:BaseOutputPath=$coreOut
dotnet test tests\Lantern.App.Tests\Lantern.App.Tests.csproj -c Release --no-restore -p:BaseOutputPath=$appOut
```

Expected: both suites pass with zero failures.

- [ ] **Step 3: Inspect repository changes**

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; only intended source, test, tool, asset, plan, and existing user changes remain.

- [ ] **Step 4: Publish the new executable**

```powershell
dotnet publish src\Lantern.App\Lantern.App.csproj -c Release --no-restore -o publish\v0.3.3 -p:BaseOutputPath=C:\Users\moham\AppData\Local\Temp\LanternBrandPublish033\
Copy-Item -LiteralPath 'publish\v0.3.3\LANtern Control.exe' -Destination '..\..\..\..\outputs\LANtern-Control-v0.3.3.exe'
```

Expected: publish exits 0 and the destination exists.

- [ ] **Step 5: Verify package identity**

Compare SHA-256 hashes of the publish source and copied output, and assert the destination product version starts with `0.3.3`.

- [ ] **Step 6: Commit the release metadata**

```powershell
git add src/Lantern.App/Lantern.App.csproj docs/superpowers/plans/2026-08-01-red-watcher-carbon-crimson.md
git commit -m "chore: package LANtern Control 0.3.3"
```
