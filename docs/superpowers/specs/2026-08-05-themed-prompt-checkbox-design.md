# Themed Wi-Fi Prompt Checkbox Design

## Goal

Replace the native white "Don't ask again" checkbox in the Wi-Fi Safe Mode prompt with a deliberate LANtern-themed preference control while preserving its existing behavior.

## Visual design

The preference appears as a compact rounded security chip beneath the Safe Mode explanation. The chip uses the existing near-black and burgundy palette, a subtle border, and comfortable internal spacing.

- Unchecked: dark rounded square with a muted burgundy border.
- Checked: red-tinted square with a crisp light checkmark and restrained red glow.
- Hover: chip background and border brighten slightly.
- Keyboard focus: a visible red focus treatment remains accessible.
- Copy: primary label "Don't ask again" with secondary label "Remember this choice on Wi-Fi".

The full chip is clickable, not only the square or label. Its dimensions must fit the existing dialog without changing the button placement or overall window size.

## Behavior

The control continues to bind to `DontAskAgainCheckBox.IsChecked`; no settings, prompt policy, or persistence behavior changes. It must support mouse and keyboard interaction and expose normal checkbox semantics to accessibility tools.

## Cross-platform implementation

Create equivalent local checkbox styles in:

- `src/Lantern.App/SafeModePromptWindow.xaml` for WPF.
- `src/Lantern.Linux/SafeModePromptWindow.axaml` for Avalonia.

The two templates should have the same layout, colors, labels, and state feedback, using framework-native template triggers/selectors where necessary.

## Validation

- Build both Windows and Linux UI projects with warnings treated as errors.
- Run the existing Windows and Linux test projects.
- Render or launch the Windows prompt and visually verify unchecked, hover, focus, and checked states.
- Confirm Ignore and Turn on Safe Mode still read the same checkbox value.
