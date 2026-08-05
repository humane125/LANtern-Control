# Themed Wi-Fi Prompt Checkbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the native prompt checkbox with a matching LANtern security-chip control on Windows and Linux without changing its behavior.

**Architecture:** Keep `DontAskAgainCheckBox` as the framework-native accessible checkbox and replace only its visual template. Define local prompt-window styles so the main application theme is unaffected, with equivalent WPF triggers and Avalonia selectors for checked, hover, pressed, and focus states.

**Tech Stack:** .NET 8, WPF XAML, Avalonia XAML, xUnit, WiX 5 release packaging.

## Global Constraints

- Preserve `DontAskAgainCheckBox.IsChecked` and all existing persistence behavior.
- The entire rounded chip must be clickable and keyboard accessible.
- Use the existing near-black, burgundy, red, cream, and muted-pink palette.
- Keep the existing dialog size and action-button placement.
- Windows and Linux must use the same copy: "Don't ask again" and "Remember this choice on Wi-Fi".

---

### Task 1: Windows security-chip checkbox

**Files:**
- Modify: `src/Lantern.App/SafeModePromptWindow.xaml`
- Modify: `tests/Lantern.App.Tests/DeviceViewModelTests.cs`

**Interfaces:**
- Consumes: WPF `CheckBox.IsChecked`, `IsMouseOver`, `IsKeyboardFocused`, and `IsPressed` template state.
- Produces: the existing named `DontAskAgainCheckBox` with a local `PromptPreferenceCheckBoxStyle`.

- [ ] **Step 1: Add a failing XAML contract test**

Read `SafeModePromptWindow.xaml` and assert it contains `PromptPreferenceCheckBoxStyle`, `Remember this choice on Wi-Fi`, a template trigger for `IsChecked`, and the existing `x:Name="DontAskAgainCheckBox"`.

- [ ] **Step 2: Run the focused Windows test**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj -c Release --filter FullyQualifiedName~SafeModePrompt`

Expected: FAIL because the custom style and secondary copy do not exist.

- [ ] **Step 3: Implement the local WPF template**

Add a `Window.Resources` style whose template is a rounded outer `Border`, a 22-pixel rounded selection box, a checkmark `Path`, and two text rows. Use triggers to show the checkmark and red glow when checked and to brighten the border on hover/focus. Apply it to `DontAskAgainCheckBox` while retaining the control name.

- [ ] **Step 4: Run the Windows tests**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj -c Release`

Expected: all tests pass with zero build warnings or errors.

- [ ] **Step 5: Commit**

```powershell
git add src/Lantern.App/SafeModePromptWindow.xaml tests/Lantern.App.Tests/DeviceViewModelTests.cs
git commit -m "feat: theme wifi prompt preference"
```

### Task 2: Linux parity and release validation

**Files:**
- Modify: `src/Lantern.Linux/SafeModePromptWindow.axaml`
- Modify: `tests/Lantern.Linux.Tests/LinuxMainWindowTests.cs`

**Interfaces:**
- Consumes: Avalonia `CheckBox:checked`, `:pointerover`, `:pressed`, and `:focus-visible` selectors.
- Produces: the existing named `DontAskAgainCheckBox` using the local `prompt-preference` class.

- [ ] **Step 1: Add a failing Avalonia XAML contract test**

Read `SafeModePromptWindow.axaml` and assert it contains `CheckBox.prompt-preference`, `Remember this choice on Wi-Fi`, checked/pointer-over selectors, and `x:Name="DontAskAgainCheckBox"`.

- [ ] **Step 2: Run the focused Linux test**

Run: `dotnet test tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj -c Release --filter FullyQualifiedName~SafeModePrompt`

Expected: FAIL because the themed Avalonia template does not exist.

- [ ] **Step 3: Implement the equivalent Avalonia template**

Add local styles/templates matching the WPF dimensions, colors, labels, checkmark, and state feedback. Keep the native checkbox as the templated parent and apply `Classes="prompt-preference"`.

- [ ] **Step 4: Run the full solution and package**

```powershell
dotnet test LanternControl.slnx -c Release
.\scripts\publish.ps1 -Configuration Release
```

Expected: 272 or more tests pass, both UI projects build, and `outputs/LANtern-Control-Setup-v0.1.3.msi` is regenerated.

- [ ] **Step 5: Visually verify the prompt**

Launch the Windows prompt in the application, confirm the unchecked, hover, keyboard-focus, and checked appearances, and confirm both action buttons read the selected preference.

- [ ] **Step 6: Commit**

```powershell
git add src/Lantern.Linux/SafeModePromptWindow.axaml tests/Lantern.Linux.Tests/LinuxMainWindowTests.cs
git commit -m "feat: match themed prompt on linux"
```
