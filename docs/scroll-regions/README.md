# Scroll regions and window rules: implementation spec

This directory is the hand-off spec for two linked features:

1. **Select-a-scroll-region.** A hotkey enters scroll mode. The active window is target `0` and
   scrolls at once. Each detected scrollable region gets a number badge `1`–`9`; a digit switches
   the target, and `H J K L` scroll it. Talon drives it with the same keys.
2. **Window rules, grown.** Today's `WindowRule` (matcher + root strategy) becomes the shared
   per-app customization layer for scroll detection, scroll actuation and click hinting.

It was assembled from the decisions of the wayfinder map
[select-a-scroll-region — implementation spec (#62)](https://github.com/knausj85/Windows-Hinting/issues/62),
which followed the GO from
[keyboard-selectable scroll regions via UIA — viability (#54)](https://github.com/knausj85/Windows-Hinting/issues/54).
Every design choice traces back to a closed decision ticket linked in context. Domain terms are in
[`CONTEXT.md`](../../CONTEXT.md) at the repo root.

**The inviolable behavior:** the click-hint workflow (hotkey → labels → type a label → element
activates) keeps working exactly as before. Scroll is additive; window-rule changes keep today's
root-strategy behavior.

## Documents

- [`design.md`](design.md) — the scroll capability: engine fit, detector, actuator, mode and
  input, overlay, the Talon surface, and the map onto today's `main` seams.
- [`window-rules.md`](window-rules.md) — the window-rule layer: matcher, app definitions,
  sections, precedence, storage, diagnostics, and its `main` bridge.
- [`builtin-rules.json`](builtin-rules.json) — the draft built-in rules (Slack, Teams, VS Code,
  Gmail, Explorer, Chromium windows), in the final schema.
- [`slices/`](slices/) — the build, one file per session-sized slice.

## Build base: `main`, through the bridge

The design is written in the **modernize** vocabulary (intents, `HintEngine` modes, `HintSource`
and actuator ports, the overlay display model, `ScanPolicy`). `modernize` has no phase code yet,
so **the slices build on today's `main`**, landing each modernize concept on the `main` seam the
bridge tables name. When the modernize phases later reach these seams, they port the scroll code
the same way they port the click code; each slice names the phase that absorbs it.

Slices branch off `main` and PR into `main`. Build with the VS 18 MSBuild (the app has COM
references, so `dotnet build` fails on it).

## Slice order

| Slice | Title | Depends on | Ships |
|---|---|---|---|
| [01](slices/slice-01-window-scroll.md) | Scroll mode with the window target | — | Ctrl+Alt+S scrolls the active window: taps, holds, dismissal, Talon title |
| [02](slices/slice-02-region-detection.md) | Generic region detection and switching | 01 | Numbered regions, digit switching, region actuation |
| [03](slices/slice-03-window-rule-engine.md) | Window-rule engine | — | Matcher, app definitions, sections, precedence, storage on `main` |
| [04](slices/slice-04-rule-driven-scroll.md) | Rule-driven scroll + built-in rules | 02, 03 | `expectedRegions`, `axisPolicy`, scoped roots, the built-ins |
| [05](slices/slice-05-rule-diagnostics-and-click.md) | Diagnostics + click section | 03 | Copy window identity, explain effective policy, `click` section |

01 → 02 gives a usable feature early. 03 runs in parallel with 01–02 because it touches only the
rule layer. 04 joins them. 05 needs only 03.

## How to execute a slice

1. Read this README, the relevant design document, and the slice file. Read a decision ticket
   only for rationale.
2. Check **Depends on**; all listed slices must be merged.
3. Satisfy the slice's **Definition of done**, including the manual checks it names.
4. Keep the slice's **Out of scope** list out of the diff.

## Reference prototypes

All throwaway; port behavior, not code structure.

| Asset | Where |
|---|---|
| `ScrollSelectProbe` — the capability end to end (detector, actuator, velocity hold, cursor hide, dismissal, Slack/VS Code/Teams pane queries) | [`prototype/slack-scroll-panes`](https://github.com/knausj85/Windows-Hinting/tree/prototype/slack-scroll-panes/prototype/ScrollSelectProbe) at `a6619d7` (its `FINDINGS.md`) |
| `RuleProbe` — headless UIA dump used to write the built-in rules | [`prototype/window-rule-probe`](https://github.com/knausj85/Windows-Hinting/tree/prototype/window-rule-probe/prototype/RuleProbe) at `745a346` (its `FINDINGS.md`) |
| Research: smooth-scroll mimicry, cursor-free injection, cursor hide | `docs/research/{smooth-scroll-mimicry,cursor-free-scroll-injection,cursor-hide-lock-scroll-mode}.md` on `prototype/slack-scroll-panes` |
| Research: URL read, URL syntax, PWA identity, Talon context lessons | `docs/research/` on `research/web-page-url-matching`, `research/url-match-syntax`, `research/pwa-identity`, `research/talon-context-lessons` |
| `ReadingOrder.Sort` — spatial reading-order helper | `Windows-Hinting/Services/ReadingOrder.cs` on `fix/taskbar-hint-reading-order` (`fdec648`, not yet on `main`) |
| `HintDismissalWatcher` — click / foreground / move-resize dismissal | `Windows-Hinting/NativeInterop/HintDismissalWatcher.cs` on `feature/hint-dismissal-parity` (`a324706`, not yet on `main`) |

## Decision record

| Ticket | Decision |
|---|---|
| [A #63](https://github.com/knausj85/Windows-Hinting/issues/63) | Scroll fits modernize additively: `ScrollRegionSource`, `Scrolling` mode, `ScrollElement` effect, `IScrollActuator` port, semantic commands |
| [B #64](https://github.com/knausj85/Windows-Hinting/issues/64) | Detector = COM harvest → pure Gate/Suppress/Dedup/Cap stages; actuator ladder, `ScrollOutcome`, profile knobs declared |
| [C #65](https://github.com/knausj85/Windows-Hinting/issues/65) | Badge-only overlay, number is identity, reading-order numbering, full key capture, intelligent dismissal, hidden cursor, v1 keymap |
| [D #66](https://github.com/knausj85/Windows-Hinting/issues/66) | Talon surface = `Windows Hinting Overlay [Scroll]` title + keystrokes; hold intents |
| [E #67](https://github.com/knausj85/Windows-Hinting/issues/67) | Per-app profile = window rule grown; sections; most specific wins per section (3 amendments) |
| [H #88](https://github.com/knausj85/Windows-Hinting/issues/88) | `url` matcher read from the web Document's UIA Value |
| [J #90](https://github.com/knausj85/Windows-Hinting/issues/90) | `url` syntax: plain host/path pattern or `/regex/`; URL tie-break |
| [K #91](https://github.com/knausj85/Windows-Hinting/issues/91) | `appId` (AppUserModelID) matcher |
| [L #92](https://github.com/knausj85/Windows-Hinting/issues/92) | App definitions, `not` exclusions, one string syntax, no tags, two diagnostics |
| [I #89](https://github.com/knausj85/Windows-Hinting/issues/89) | Built-in rule contents; drop `FrameworkId`; `className` token; `name` selector; `region: parent` |
| [F #68](https://github.com/knausj85/Windows-Hinting/issues/68) | This spec. Settled here: v1 ships the prototype's actuation model (taps try UIA first); app definitions live inside rule files; diagnostics are tray items acting on the last active window; build in slices on `main`; vim `HJKL` layout |
| [G #69](https://github.com/knausj85/Windows-Hinting/issues/69) | **Still open** (not a blocker): bare-metal cursor-independence test, touch-pan lane, VS-editor gap, acceleration curve. See [design.md § Open items](design.md#open-items-owned-by-g-69) |

Out of scope for this spec: Home/End and PageUp/PageDown (parked in
[#70](https://github.com/knausj85/Windows-Hinting/issues/70)); a rules editor UI; Talon-side Python;
moving the physical pointer to reach an un-hinted pane.
