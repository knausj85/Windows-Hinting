# Competitive research: Windows OS-wide hinting tools

Ticket: #95 (child of wayfinder map #94). Desk research only, done 2026-10-13. Nothing was installed.
Each claim cites the primary source that owns it: official docs, README, config reference, GitHub API metadata, or the issue tracker.

## Tools covered

| Tool | What it is | Maintenance (as of 2026-10-13) | License | Sources |
|---|---|---|---|---|
| **Hunt-and-Peck** (zsims/hunt-and-peck) | The original Windows "vimium for apps". UIA, Invoke-pattern only | **Stale.** Last release 1.7 on 2023-02-16, last push 2023-04-01. README says it is "sporadically maintained, please consider one of the various forks". 814 stars, 33 open issues | **None declared.** The repo has no LICENSE file and issue #55 "Add license" is still open | [README](https://github.com/zsims/hunt-and-peck), GitHub API, [issues](https://github.com/zsims/hunt-and-peck/issues) |
| ↳ forks: Hunt-and-Peck-Enhanced (W-gerber), Fettsackmitch, jiangyun-fun | Small active forks | W-gerber was pushed 2026-10-12 (2 stars). The others are low activity | None declared | [Enhanced README](https://github.com/W-gerber/Hunt-and-Peck-Enhanced) |
| **Fluent Search** (adirh3) | A launcher with three hint/search surfaces: **Screen Search** (hint labels), **In-window search** (search the UIA elements of the focused app) and **OCR screen search** | **Very active.** v1.5.0.3 shipped 2026-10-10 and nightly builds are offered. 2.2k stars on the tracker repo | **Freeware, closed source.** The GitHub repo is an issue tracker only and declares no license. The site offers donations; no paid tier was found | [Screen search: General](https://fluentsearch.net/docs/Screen%20search/General), [Advanced](https://fluentsearch.net/docs/Screen%20search/Advanced%20usage), [Customization](https://fluentsearch.net/docs/Screen%20search/Customization), [OCR](https://fluentsearch.net/docs/Screen%20search/OCR), [In-window search](https://fluentsearch.net/docs/Search%20apps/In-window%20search), [repo](https://github.com/adirh3/Fluent-Search) |
| **Neru** (y3owk1n/neru) | Cross-platform "hints, grids and vim keys for your whole desktop" (Go) | **Very active.** v1.58.0 shipped 2026-10-12, 810 stars. The README rates Windows 10+ as "Beta, daily" (macOS is Stable) | MIT | [README](https://github.com/y3owk1n/neru), [using-neru](https://github.com/y3owk1n/neru/blob/main/docs/guide/using-neru.md), [configuring](https://github.com/y3owk1n/neru/blob/main/docs/guide/configuring.md), [integrations](https://github.com/y3owk1n/neru/blob/main/docs/guide/integrations.md) |
| **mousemaster** (petoncle) | Keyboard mouse control and key remapper. Hint types: `grid`, `ui-accessibility` (UIA), `ui-vision` (CV) and `position-history` | **Very active.** Pushed 2026-10-13, release "94" on 2026-09-04, 906 stars. Supports Windows and macOS | None declared (the API reports no LICENSE) | [README](https://github.com/petoncle/mousemaster), [configuration-reference](https://github.com/petoncle/mousemaster/blob/main/docs/configuration-reference.md) |
| **keyhop** (rsaz/keyhop) | Young Rust UIA hinter with a window picker | **Active but small.** v0.5.0 on 2026-05-22, last push 2026-06-22, 12 stars. Windows only; Linux and macOS are on the roadmap | Apache-2.0 / MIT | [docs.rs crate page](https://docs.rs/crate/keyhop/latest), [repo](https://github.com/rsaz/keyhop) |
| **win-vind** (pit-ray) | Vim bindings for Windows, with `<easyclick>` UIA hints and `<gridmove>` | **Archived.** Last release v5.13.2 on 2024-07-06. 2.3k stars | MIT | [functions cheat sheet](https://pit-ray.github.io/win-vind/cheat_sheet/functions/) |
| keynavish (lesderid) | Windows port of keynav: recursive-bisect cursor control, **no element hints** | Active (pushed 2026-09-24) | GPL-2.0 | [repo](https://github.com/lesderid/keynavish) (reference only, fallback mode) |
| **KeyNavigator** | **Not found.** Neither web search nor GitHub search turned up a Windows desktop hinter by this name. The only matches were jQuery plugins and a browser extension called "Keyboard Navigator". It may be a misremembered name, possibly for keyhop or keynavish. The map should drop or re-identify it | — | — | — |

**Context signal:** PowerToys issue [#48942 "Hints-style Keyboard Navigation"](https://github.com/microsoft/PowerToys/issues/48942) was opened 2026-06-27 and is still `Needs-Triage`. It cites Fluent Search as the existing implementation. Microsoft has not responded. If PowerToys ever ships hinting it would be a first-party competitor; for now it is only a signal of demand.

## Windows-Hinting baseline (what it has today, per README + `main`)

- **Discovery:** UIA. Per-window `WindowRules` set the root strategy (`Configuration/WindowRule.cs`).
- **Scope:** the foreground window, plus the taskbar on a separate hotkey.
- **Labels:** fixed A–Z alphabet in bijective base-26 (`Labels.cs`). The alphabet cannot be configured. Hint position and overlap threshold can be set.
- **Selection:** type the label, then Space/Enter.
- **Actions:**
  - UIA activator chain: Invoke, Toggle, SelectionItem, ExpandCollapse, SetFocus, then a mouse fallback.
  - Shift-toggled click modes: left, right, double, mouse-move, ctrl-click, shift-click.
- **Integration:** a named pipe that accepts `TOGGLE`, `TOGGLETASKBAR`, `SELECT <label> [LEFT|RIGHT|DOUBLE|MOVE|CTRL|SHIFT]` and `DEACTIVATE`. It is inbound only. There is a Talon example.
- **Configuration and lifecycle:**
  - Preferences dialog with recordable hotkeys and click-action keys.
  - 15 s auto-hide timeout and a scan timeout.
  - NetSparkle updates and a log viewer.

## Tool × feature matrix (common taxonomy from #94)

Legend:
- **Y** = documented
- **P** = partial or limited
- **—** = not documented
- **WH** = Windows-Hinting

| Taxonomy / feature | WH | Hunt-and-Peck | Fluent Search | Neru (Win beta) | mousemaster | keyhop | win-vind (archived) |
|---|---|---|---|---|---|---|---|
| **Discovery: UIA** | Y | Y (Invoke-pattern only) | Y ("In-Window Content" engine) | Y (accessibility tree) | Y (`ui-accessibility`) | Y | Y (`<easyclick>`) |
| **Discovery: OCR / CV** | — | — | Y: Image Recognition engine, **Auto** engine choice, **Tab** toggles engine; Windows OCR text search | Y: on-device OCR + "pure-Go contour scan" | Y: `ui-vision` with `vision-density` 1–8 | — | — |
| Background pre-scan / cache | — | — | Y: "Process in background … labels appear almost instantly" | — | — (async scan, stays responsive) | Y: element-tree caching (v0.4.0) | — |
| **Scope: focused window** | Y | Y | Y | Y | Y (`active-window` incl. popups past the window edge) | Y | Y |
| **Scope: all windows / whole screen** | — | — | Y ("Entire screen" hotkey) | — (monitor under cursor) | Y (`active-screen`; occluded elements culled) | Y (active monitor / all windows) | Y (`<easyclick_all>`) |
| **Scope: multi-monitor** | — | — | Y (L/R Shift switches screen) | Y (draws on cursor monitor; **monitor-select mode**) | Y (`all-screens`) | Y | — |
| Scope: taskbar / tray | Y (hotkey) | Y (`Ctrl+;` tray) | Y (taskbar, tray **and notifications**) | — | via screen scope | via screen scope | via `_all` |
| Scope: menus / popups | P | — | via screen scope | — | Y (popups included explicitly) | — | — |
| Scope: window picker | — | — | — | — | — | **Y** (`Ctrl+Alt+Space` window selection) | — |
| **Labels: configurable alphabet** | — (fixed A–Z) | — | Y (default `ASDWJKLIFHQERNOUMCXVZGBTYP`) | Y (`hints.hint_characters`) | Y (selection keys) | Y (presets: home row, alpha, alnum, numbers, custom; ambiguous-char exclusion) | — |
| Labels: home-row default | — | — | P | Y | Y | Y (`asdfghjkl`) | — |
| Labels: length strategy | bijective base-26 | — | — | — | — | Y (shortest-first vs fixed-length) | — |
| Labels: element cap | — | — | — | — | — | Y (default 300) | — |
| Labels: skip disabled elements | ? | Y (Invoke only) | — | Y (no labels on disabled) | — | — | — |
| **Selection: type label** | Y | Y | Y | Y | Y | Y | Y |
| **Selection: type text / search by name** | — | — (requested, HAP #53) | **Y**: In-window search (UIA names, tab-first ranking, preview-on-select); OCR text search | **Y** (`/` then text) | — | — | — |
| Selection: nudge labels | — | — | Y (arrow keys move labels) | — | — | — | — |
| **Action: left / right / double** | Y | invoke only (requested HAP #16/#23) | Y (number prefix: 2 = double, 4 = right) | Y (+ **middle, triple**) | Y (+ middle) | Y (Ctrl = double, Shift = right) | Y (+ middle) |
| Action: hover / move | Y (Move) | — | Y (5 = move) | Y | Y (`mouse-follows-selected-hint`) | — | Y (easyclick alone) |
| Action: focus without activating | P (SetFocus in chain only) | — (requested HAP #32) | — | — | — | — | — |
| Action: modifier clicks | Y (Ctrl/Shift) | — | — | Y (**sticky modifiers**: tap Ctrl/Alt/Shift/Win, it stays held) | Y (combos) | P (in progress) | — |
| **Action: drag** | — | — | — | **Y** (Shift+I hold, move, Shift+U release) | Y | — | — |
| **Action: scroll** | — | — (requested HAP #52) | — | **Y** (scroll mode, vim keys) | Y | Y (via UIA patterns) | — |
| **Action: text-select** | — | — | **Y** (3 = select/highlight text at element) | — | — | — | — |
| Action: keep cursor after click | ? | — | Y (option) | — | Y | — | — |
| Action: repeat-last / position history | — | — | — | — | **Y** (`position-history` hints = jump to saved positions) | — | — |
| **Chaining / persistence** | — (one-shot) | — | — | P (sticky mode; macros) | Y (modal engine; stays in mode) | — | P (modal) |
| **Fallback: grid** | — | — | — (CV engine instead) | **Y** (recursive grid, grid, bisect) | **Y** (grid, recursive) | — | Y (`<gridmove>`) |
| Fallback: keyboard mouse mode | — | — | — | Y | Y (core feature) | — | Y |
| **Integration: inbound command** | Y (named pipe) | P (`/hint`, `/tray` CLI flags) | — | Y (CLI + versioned JSON IPC + Go client) | — | P (lifecycle CLI flags) | — |
| **Integration: outbound state / labels for voice** | — | — | — | **Y** (`neru query hints --json` "to enable voice-driven element selection"; `neru watch --event mode_enter,mode_exit`; `neru hints --targets` for custom targets) | — | — | — |
| Integration: Talon / voice | Y (example) | — | — | Y (via query API) | — | — | — |
| **Config: per-app profiles** | P (WindowRules: root strategy only) | — | — | Y (`app_configs` by exe path, applied on focus change) | — | — | — |
| Config: file + hot reload | P (settings JSON) | — | GUI | TOML, manual reload | `.properties`, **auto-reload on save** | GUI + TOML | `.vindrc` |
| Config: rebindable hotkeys | Y | — (HAP #19/#31) | Y | Y | Y | Y | Y |
| **Visual: colors / size / font** | P (hint position, fade animation) | P (font size) | Y (size 1–40, borders, 4 positions, bg color) | Y (themes) | Y (box/font/opacity/radius/padding) | Y (colors, opacity, Consolas) | Y |
| Visual: high contrast / DPI | ? | known bugs (HAP #35 HC invisible, #30 HiDPI) | — | — | — | Y (smart badge positioning) | — |
| Performance claims | scan timeout + metrics | known slow (HAP #1, #60) | background pre-scan claim | "any added latency as a bug" | async scan | caching | — |
| Privacy claim | — | — | — | "OCR and element detection run on-device. No telemetry" | — | — | — |
| Platform | Win11 | Win (not Modern/UWP; Win11 issue #42) | Win10/11 x64 + ARM64 | Win10+ (beta), macOS, Linux | Win, macOS | Win (x64) | Win |

## Gaps Windows-Hinting lacks, with baseline overlap

Gaps are ranked with the map's lens: voice/Talon and accessibility first, keyboard power users second. "Baseline" names the existing map or issue a gap would fold into. "None" means no existing repo work covers it.

| # | Gap | Who has it | Baseline overlap |
|---|---|---|---|
| 1 | **Outbound state/label API for voice**: query the current hints (label, name, role, rect) as JSON and subscribe to mode events. WH's pipe is command-in only, so Talon cannot "say the button name" | Neru (`query hints --json`, `watch`) | **None.** #26 (modernize) touches the IPC seam but does not cover this feature |
| 2 | **Search/filter by element text (name)** as well as by label. This is a natural fit for voice ("click Save") | Fluent (In-window search, OCR search), Neru (`/`); requested in HAP #53 | Partial #21 (hinting improvements); otherwise **none** |
| 3 | **OCR / computer-vision discovery fallback** for apps with no UIA (canvas, Electron gaps, remote desktop, games) | Fluent (Image Recognition + Auto engine + OCR), Neru (OCR + contour), mousemaster (`ui-vision`) | Partial #20 (UIA population strategies is UIA-only); **cross-cutting family, candidate for its own grilling** |
| 4 | **Grid / recursive-grid fallback** for areas with no target | Neru, mousemaster, win-vind, keynavish | **None** |
| 5 | **Multi-monitor and all-windows scope**: hint every visible window or a chosen monitor, with occlusion culling and monitor-select | Fluent, Neru, mousemaster, keyhop, win-vind | Partial #26 (scan pipeline); #81 is taskbar-only. Mostly **none** |
| 6 | **Configurable label alphabet / home-row / length strategy / element cap** | Fluent, Neru, mousemaster, keyhop | **#26** (hint engine phase) |
| 7 | **Scroll action/mode** | Neru, mousemaster, keyhop; requested in HAP #52 | **#54 + #62** (scroll regions) |
| 8 | **Drag** (hold, move, release), **middle/triple click** and **sticky modifiers** | Neru, mousemaster (drag/middle) | **None** (#25 multi-action buttons is adjacent only) |
| 9 | **Text-select action** at an element | Fluent (`3`) | **None.** Text selection is a likely cross-cutting family |
| 10 | **Background pre-scan / element caching** so hints appear instantly | Fluent, keyhop, HAP-Enhanced | **#26** (scan pipeline); #81 is event-driven reconcile for the taskbar; #20 |
| 11 | **Label appearance customization**: colors, size, font, borders, plus **arrow-key label nudging** | Fluent, mousemaster, keyhop, HAP-Enhanced | **#26** (overlay composer, preferences) for appearance; **none** for nudging |
| 12 | **Window picker hints**: label top-level windows to switch to | keyhop | **None** |
| 13 | **Position history / repeat-last**: hints at saved cursor positions | mousemaster | **None** (#70 focus paging/absolute jump is adjacent) |
| 14 | **Richer per-app profiles** (exe-path matched, any setting) | Neru `app_configs` | **#62** (profile = grown window rule) |
| 15 | **Popups included in window scope** (menus/dropdowns that extend past the window) | mousemaster `active-window` | **#44 / #72** (+ #78/#79) |
| 16 | **Notifications / tray coverage** on the taskbar surface | Fluent | **#81** |
| 17 | **Config hot-reload on save** | mousemaster | **#26** (preferences) |
| 18 | **Focus without activating** as an explicit action | requested HAP #32 | Partial #8 (activator order) |
| 19 | **High-contrast / HiDPI correctness** as a stated feature | (HAP has these as known bugs; keyhop claims smart positioning) | **#26** (per-pixel alpha); #81 DPI caveat |
| 20 | **CLI one-shot flags** (`/hint`, `--quit`) | HAP, keyhop, Neru | Partial #15 (portable); the named pipe already covers most of it, so priority is low |

Things Windows-Hinting already does that most rivals do not:
- **Ctrl-click and Shift-click modes.** Only mousemaster/Neru combos come close.
- **A UIA pattern activator chain** (Toggle/SelectionItem/ExpandCollapse before mouse). Rivals invoke or synthesize clicks.
- **A dedicated taskbar hotkey.** HAP and Fluent also have one.
- **A named-pipe SELECT with a click type**, plus a Talon example.

Fluent Search is the closest feature-complete Windows rival, but it is closed source and lives inside a launcher. Neru is the closest open-source and voice-oriented rival, but Windows is still beta.

## Caveats

- Everything above comes from documentation. Behaviour, especially Neru's Windows beta and Fluent's Image Recognition quality, was not verified by hand. Fluent is the candidate for the map's "hands-on verification" item if a disposition depends on it.
- No changelog or tracker was found for "Tab hints" in Fluent Search. The documented tab feature is **In-window search**, which ranks browser tabs first among the focused app's UIA elements.
- Some cells marked "?" for WH (skip-disabled, keep-cursor, HC/DPI) are not stated in the README and were not traced through code.
