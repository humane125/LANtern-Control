# LANtern Control frontend redesign

## Approved direction

LANtern Control will use the approved compact dark operations-console design in
[`assets/lantern-control-frontend-concept.png`](assets/lantern-control-frontend-concept.png).
The redesign preserves the existing navy and teal identity while replacing the
spreadsheet-like screen with clearer network status, live traffic context, and
device controls.

Three directions were considered: a minimal table-only utility, a card-heavy
consumer dashboard, and a compact operations console. The operations console
is selected because it keeps frequent controls visible, supports dense network
data without looking cramped, and scales naturally to future views.

## Layout and navigation

The window remains a native WPF desktop application with a minimum usable size
of 1100 by 700 and a preferred size near 1440 by 900.

- A 232-pixel left rail contains the LANtern mark, Overview, Devices, Activity,
  and Settings destinations, plus the local-processing notice.
- Navigation remains within the single window. Overview focuses the whole
  dashboard, Devices focuses the device table, Activity focuses the live chart,
  and Settings focuses the adapter/control strip. No empty placeholder pages
  are introduced.
- The main header contains the page title and a text-plus-dot status pill.
- The adapter strip shows adapter name, local IPv4 address, and gateway. When
  idle it presents Start control as the sole primary action. When active it
  presents Refresh devices and a spatially separated Stop and restore action.
- The content area uses an 8-pixel spacing system, 12-pixel card radii, and
  one-pixel low-contrast borders. The footer reports the latest operation and
  controller uptime.

## Overview metrics

Four compact cards show only real values available from the controller:

1. connected device count;
2. aggregate current download rate;
3. aggregate current upload rate;
4. active device-rule count.

The concept image's packet-loss card will become Active limits because LANtern
does not currently measure packet loss. The UI must not invent telemetry. Rate
figures use tabular numerals and the same byte-rate formatting as device rows.

## Live traffic chart

The chart displays aggregate download and upload samples collected from the
existing one-second device refresh. It retains the most recent two minutes in
memory and does not add network probes or persist telemetry to disk.

- Download is teal and upload is blue, with a visible legend and units.
- Time spacing reflects actual sample timestamps.
- Hovering or keyboard-focusing a sample reveals time, total download/upload,
  and the highest-traffic device for that sample.
- Empty, idle, and unavailable states use explanatory text instead of a blank
  graph.
- The chart uses a lightweight custom WPF drawing control and redraws only when
  a sample changes.

## Device controls

The device table remains ranked by current total traffic and uses real observed
devices only.

- Device identity combines icon, display name, IP, and MAC without allowing the
  MAC to collide with the device name.
- Download and upload rates remain separate and use tabular numerals.
- Limit inputs include a visible `KB/s` suffix and helper tooltip stating that
  zero means unlimited.
- Internet access uses a labelled toggle rather than an unlabeled checkbox.
- The gateway is visibly Protected and its controls are disabled. The local PC
  is identified in the adapter strip rather than inserted as a fake traffic row.
- Sorting by current traffic remains automatic, but keyboard focus is preserved
  while values update.

## Visual system

- Background: `#07111F`.
- Base surface: `#0C1A2A`; raised surface: `#112438`.
- Border: `#213A50`.
- Primary accent: `#39D6C0`; upload accent: `#5B8DEF`.
- Success: `#59E391`; destructive: `#FF6F7D`.
- Primary text: `#F3F7FB`; secondary text: `#98AEC2`.
- Typography remains Segoe UI for a native Windows feel. Titles use 26 pixels,
  body text 14 to 16 pixels, and metadata 12 pixels minimum.
- Icons are consistent outline paths. Emoji, glow effects, glassmorphism, heavy
  gradients, and decorative animation are excluded.
- Interactive controls provide visible hover, pressed, disabled, and keyboard
  focus states without moving surrounding layout.

## Data flow and state

The networking engine remains unchanged by the visual redesign. `MainWindow`
continues sampling `DeviceRegistry` once per second. View-model numeric rate
properties feed both the summary cards and a bounded chart-history collection.
Existing commands continue to own Start, Refresh, Stop and restore, limit, and
pause behavior.

During long operations, related controls are disabled and the status pill shows
Working. Failures keep the existing network state visible, show the cause in the
footer, and offer the relevant retry action. Stop and restore remains available
whenever control is active and stays visually distinct from ordinary actions.

## Accessibility and resizing

- All text and functional icons meet WCAG AA contrast against their surfaces.
- Every control is keyboard reachable in visual order and has a visible focus
  indicator.
- Icon-only affordances receive accessible names and tooltips.
- Status is expressed with text and icon as well as color.
- At narrower supported widths, metric cards wrap to two columns, the chart
  stays full width, and the device table gains horizontal scrolling rather than
  truncating controls.
- Motion is limited to 150-to-200-millisecond color or opacity transitions and
  does not block interaction.

## Verification

Implementation is complete only when:

- existing networking and restoration tests still pass;
- new tests cover aggregate metrics, bounded chart history, protected-device
  controls, and byte-rate formatting;
- the Release build succeeds with warnings treated as errors;
- the window is inspected at 1100 by 700, 1280 by 760, and 1440 by 900;
- Start, Refresh, applying limits, Pause internet, and Stop and restore are
  exercised without changing their established behavior;
- the rendered UI is compared with the approved concept for hierarchy, spacing,
  color, and control placement.

