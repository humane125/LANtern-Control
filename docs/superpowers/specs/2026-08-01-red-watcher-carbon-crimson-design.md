# Red Watcher and Carbon Crimson Design

## Goal

Give LANtern Control a distinctive dark identity without making the interface visually loud. The approved Red Watcher logo becomes the application mark, and the approved Carbon Crimson palette replaces the existing blue/teal theme while preserving the current layout and usability.

## Logo

- Use a geometric eye silhouette with a near-black body, crimson outline, dark-red iris, and uninterrupted black pupil.
- Do not include an X, white catchlight, white pupil detail, text inside the mark, or large bloom effects.
- Keep the mark legible at 16, 24, 32, 48, and 256 pixels.
- Maintain one vector source of truth and derive the Windows `.ico` asset from it.
- Use the mark for the executable icon, window icon, taskbar icon, and sidebar brand block.
- Keep the existing `LANtern CONTROL` wordmark beside the sidebar mark; the standalone executable icon uses only the eye.

## Carbon Crimson palette

| Role | Color | Use |
| --- | --- | --- |
| Window background | `#08090B` | Main canvas |
| Sidebar | `#060708` | Navigation rail |
| Surface | `#101114` | Cards and tables |
| Raised surface | `#171317` | Buttons, menus, hover layers |
| Input background | `#0C0B0D` | Text and limit inputs |
| Border | `#2B2226` | Subtle structure |
| Primary text | `#F4EEF0` | Main labels and values |
| Secondary text | `#A89095` | Metadata and helper text |
| Crimson accent | `#D72C43` | Active navigation, focus, selected states, primary chart |
| Crimson dark | `#261014` | Accent backgrounds and selected fills |
| Upload accent | `#9B6670` | Secondary chart series |
| Success | `#68C08A` | Online and healthy state only |
| Danger | `#F06473` | Destructive action only |

Red must remain an accent. Large backgrounds stay neutral near-black; no full red panels, saturated red table rows, or persistent bright glow.

## Interface application

- Replace teal branding in the sidebar, headings, active navigation, focus rings, toggles, and download chart with crimson.
- Use muted rose for upload traffic so both chart series remain distinguishable.
- Recolor hard-coded blue surfaces in headers, rows, chart backing, and status strips to neutral black or dark wine tones.
- Keep online indicators muted green because they communicate health rather than branding.
- Keep destructive actions visually distinct with the lighter danger red and restrained dark background.
- Preserve the existing spacing, typography, controls, table structure, one-second chart sampling, and one-hour history behavior.

## Accessibility and restraint

- Maintain high text contrast against every surface.
- Use crimson borders or small fills for focus and selection instead of large bright blocks.
- Avoid glow on body text, table rows, form fields, and ordinary buttons.
- The logo may use a soft localized red shadow at large display sizes; small icons remain crisp and flat.
- Disabled states retain their current opacity behavior.

## Verification

- Load the complete WPF resource dictionary in tests and instantiate the main window without XAML errors.
- Verify the new logo appears in the window/sidebar and the project references a valid `.ico` application icon.
- Run the full core and app test suites.
- Publish a new standalone x64 executable and confirm its file version and SHA-256 copy match.
- Visually inspect the dashboard at normal size and confirm the red is limited to accents rather than dominant surfaces.
