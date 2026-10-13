# Competitive research: Linux hinting & keyboard-pointer tools

Ticket: #97 (child of wayfinder map #94). Desk research only, as of 2026-10-13: READMEs, man pages, source code, release notes and repo metadata (GitHub API). No tool was installed.

**Question.** What do Linux keyboard-hinting and keyboard-pointer tools offer? Each tool is inventoried against the #94 common taxonomy, with AT-SPI vs screenshot/CV discovery called out. Every feature Windows-Hinting (WH) lacks is flagged, along with whether it overlaps the #94 baseline.

**WH reference point** (README + `origin/main` at `e331d0d`):
- Discovery is UIA only, for the foreground window plus a taskbar hotkey.
- Labels are a fixed `A–Z` bijective base-26 sequence (`Labels.cs`). The alphabet can't be configured.
- You type a label to filter, then Space/Enter commits.
- Actions: Invoke via an activator chain (Invoke → MouseClick → ExpandCollapse/Toggle/SelectionItem/SetFocus pattern activators, per #8), plus `Shift+L/R/D/M/C/S` for left, right, double, move, ctrl-click and shift-click.
- Named-pipe IPC: `TOGGLE`, `TOGGLETASKBAR`, `SELECT <label> [action]`, `DEACTIVATE`.
- Per-window rules (exe/class/title → root strategy).
- Configurable hotkeys, a tray mode icon and a "show rectangles" option. There's no theming, scroll, drag, grid or pointer mode.

---

## 1. Tools covered

| Tool | Kind | Discovery | Display server | License | Maintenance (GitHub API, 2026-10-13) |
|---|---|---|---|---|---|
| **hints** (AlfredoSequeida) | Element hinting | **AT-SPI, then OpenCV fallback** | X11 + Wayland (GNOME, KDE/plasmashell, sway, Hyprland, niri, mango) | GPL-3.0 | **Active.** v0.1.1 on 2026-06-28; 1.4k★ |
| **warpd** (rvaiya) | Keyboard pointer (hint / grid / normal) | **None.** Hints are a fixed lattice of screen points | X11, wlroots Wayland (no GNOME), macOS, Windows alpha | MIT | **Dormant.** Last commit 2023-06-03; v1.3.5 (2022-11), `v1.3.7-windows-alpha` (2023-04); 3.6k★, 108 open issues |
| **keynav** (jordansissel) | Keyboard pointer (cut/move/grid) | None (screen bisection) | X11 only | BSD-style (COPYRIGHT) | **Dormant.** Last commit 2022-08-24; 651★ |
| **wl-kbptr** (moverest) | Keyboard pointer + optional detection | **OpenCV via screencopy (optional)**, or areas from stdin | wlroots Wayland (sway, Hyprland, niri, dwl, labwc, Wayfire); KDE print-only; no Mutter | GPL-3.0-only | **Active.** v0.4.1 on 2025-11-30, pushed 2026-10-01; 773★ |
| **Neru** (y3owk1n) | Hints + grids + pointer, cross-platform | **AT-SPI / UIA / AX, OCR (tesseract / Windows.Media.Ocr / Vision), contour** | macOS (stable), Linux X11 + Wayland (beta), **Windows 10+ (beta)** | MIT | **Very active.** Pushed 2026-10-13; 810★ |
| **mouseless** (jbensmann) | Key remapper + pointer layers | None | Any (evdev/uinput), incl. Wayland | MIT | Active. v0.3.0 on 2025-12-01; 619★ |
| **vimium-everywhere** (phil294) | Element hinting (AHK_X11 script) | AT-SPI | X11 only (Windows path broken) | GPL-3.0 | Low activity. Pushed 2026-05; 221★; self-described "experimental… unstable" |
| **goodnight-mouse** (rpbritton) | Element hinting | AT-SPI | X11 / AT-SPI | GPL-3.0 | Dormant. Pushed 2024-04; 28★ |
| **viMouse** (garywill) | Element hinting | **OpenCV screenshot only** | X11 + Windows ("theoretically cross-platform") | GPL-3.0 | Dormant alpha. 0.2.0 on 2023-11; 92★ |

Found but not inventoried:
- **keyhop** (rsaz, Apache-2.0) is a UIA hinting tool for Windows. Its Linux AT-SPI backend is only "planned". It belongs to the Windows inventory (#95) and is noted in §5.
- **waywarp** (Xuepoo, MIT, 1★) is a wlroots hint-warp tool known only from docs.rs. It's too immature to cite.
- **keyd** and kmonad are remappers with mouse keys. They're outside the taxonomy.

**Sources:**
- hints: [README](https://github.com/AlfredoSequeida/hints), [`constants.py`](https://github.com/AlfredoSequeida/hints/blob/main/hints/constants.py), [`backends/atspi.py`](https://github.com/AlfredoSequeida/hints/blob/main/hints/backends/atspi.py), [`backends/opencv.py`](https://github.com/AlfredoSequeida/hints/blob/main/hints/backends/opencv.py), [`hints.py`](https://github.com/AlfredoSequeida/hints/blob/main/hints/hints.py), [`cli.py`](https://github.com/AlfredoSequeida/hints/blob/main/hints/cli.py), [`window_systems/`](https://github.com/AlfredoSequeida/hints/tree/main/hints/window_systems), [releases 0.1.0 / 0.1.1](https://github.com/AlfredoSequeida/hints/releases)
- warpd: [README](https://github.com/rvaiya/warpd), [man page `warpd.1.md`](https://github.com/rvaiya/warpd/blob/master/warpd.1.md), [`src/hint.c`](https://github.com/rvaiya/warpd/blob/master/src/hint.c), [`src/config.c`](https://github.com/rvaiya/warpd/blob/master/src/config.c)
- keynav: [README](https://github.com/jordansissel/keynav), [`keynav.pod`](https://github.com/jordansissel/keynav/blob/master/keynav.pod)
- Others: [wl-kbptr README](https://github.com/moverest/wl-kbptr), [Neru README](https://github.com/y3owk1n/neru), [Neru platform-support](https://github.com/y3owk1n/neru/blob/main/docs/reference/platform-support.md), [Neru using-neru](https://github.com/y3owk1n/neru/blob/main/docs/guide/using-neru.md), [mouseless README](https://github.com/jbensmann/mouseless), [vimium-everywhere README](https://github.com/phil294/vimium-everywhere), [goodnight-mouse README](https://github.com/rpbritton/goodnight-mouse), [viMouse README](https://github.com/garywill/vimouse), [keyhop README](https://github.com/rsaz/keyhop)

---

## 2. Discovery approaches: AT-SPI vs screenshot/CV

There are three families. The Linux tools show the trade-offs clearly.

1. **Accessibility tree (AT-SPI, the UIA analogue).**
   - **Used by:** hints, vimium-everywhere, goodnight-mouse, Neru.
   - **hints:** queries the focused window with `Atspi.Collection.get_matches` using a role/state `MatchRule`. If the app doesn't implement Collection, it falls back to a slow recursive walk. By default it takes elements that are `SENSITIVE`+`SHOWING` and *not* a container/text role (a deny-list of roles, not an allow-list). ([`constants.py`](https://github.com/AlfredoSequeida/hints/blob/main/hints/constants.py), [`atspi.py`](https://github.com/AlfredoSequeida/hints/blob/main/hints/backends/atspi.py))
   - **Known pain, version and toolkit quirks:** GTK4 and Wayland give only window-relative coordinates, so hints re-bases them on the window-manager extents. Per-app `scale_factor` fixes DPI. GNOME's `mutter-x11-frames` pseudo-app has to be skipped. ([`atspi.py`](https://github.com/AlfredoSequeida/hints/blob/main/hints/backends/atspi.py))
   - **Known pain, setup:** Chromium and Electron need `--force-renderer-accessibility` plus `ACCESSIBILITY_ENABLED=1`. Qt needs `QT_LINUX_ACCESSIBILITY_ALWAYS_ON=1`. Java needs the ATK bridge. ([vimium-everywhere](https://github.com/phil294/vimium-everywhere), [goodnight-mouse](https://github.com/rpbritton/goodnight-mouse))
   - **Known pain, speed:** "AtSpi… simply does not expose powerful enough functions for performant access… 0 and 3s". vimium-everywhere skips lists with more than 1,000 children. ([vimium-everywhere](https://github.com/phil294/vimium-everywhere#speed))
   - **Comparison:** UIA's `FindAll`/`CacheRequest` is the stronger API. WH's problems are timeouts (#7) and population strategies (#20), not fundamental coverage.
2. **Screenshot + computer vision.**
   - **hints `opencv` backend:** screenshot of the focused window → grayscale → Canny edges → dilate → `findContours` → each bounding rectangle becomes a target. It's tunable per app (`kernel_size`, `canny_min/max_val`). ([`opencv.py`](https://github.com/AlfredoSequeida/hints/blob/main/hints/backends/opencv.py))
   - **wl-kbptr:** `floating` mode with `source=detect` needs the `opencv` build feature and `wlr-screencopy`. ([wl-kbptr](https://github.com/moverest/wl-kbptr#auto-detection))
   - **viMouse:** OpenCV only. It admits failures on low-contrast UIs. ([viMouse](https://github.com/garywill/vimouse))
   - **Neru:** a pure-Go `contour` strategy, plus a `vision` OCR strategy (tesseract on Linux, **`Windows.Media.Ocr` on Windows**) that finds words and button shapes in the same frame. ([platform-support note ²](https://github.com/y3owk1n/neru/blob/main/docs/reference/platform-support.md))
3. **Hybrid / fallback chain.**
   - **hints:** `backends.enable = ["atspi","opencv"]` runs each backend in order until one yields targets. ([`hints.py` `gather_hints`](https://github.com/AlfredoSequeida/hints/blob/main/hints/hints.py))
   - **Neru:** lets you choose a strategy per invocation or app (`neru config set hints.strategy contour`) and recommends grid modes "where hints are thin". ([Neru README](https://github.com/y3owk1n/neru))
4. **No discovery (pure geometry).**
   - **warpd:** hint mode draws an `N×N` lattice of two-char labels (`hint_chars` squared) over the screen. It doesn't detect elements. ([`src/hint.c` `generate_fullscreen_hints`](https://github.com/rvaiya/warpd/blob/master/src/hint.c))
   - **Also pure geometry:** keynav, wl-kbptr `tile/bisect/split`, Neru grids, mouseless.
   - **Upside:** these work in canvases, games and remote desktops, where no tree exists.

**Implication for WH:** WH is pure UIA. Every Linux tool with element hints either offers a CV fallback (hints) or pairs hints with a geometric grid fallback (warpd, Neru, wl-kbptr). Neru's Windows build already ships OCR via `Windows.Media.Ocr`, which proves a Windows-native, no-dependency OCR path exists.

---

## 3. Tool × feature matrix (#94 taxonomy)

Legend: ✅ yes · ◐ partial/limited · ❌ no · — not applicable. **WH** column = current `main`.

| Taxonomy | Feature | WH | hints | warpd | keynav | wl-kbptr | Neru (Linux) | vimium-ev. | goodnight-m. | mouseless |
|---|---|---|---|---|---|---|---|---|---|---|
| **Discovery** | Accessibility API | ✅ UIA | ✅ AT-SPI | ❌ | ❌ | ❌ | ✅ AT-SPI | ✅ | ✅ | ❌ |
| | Screenshot CV / contour | ❌ | ✅ OpenCV | ❌ | ❌ | ✅ (opt.) | ✅ | ❌ | ❌ | ❌ |
| | OCR text targets | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ tesseract | ❌ | ❌ | ❌ |
| | Backend fallback chain | ❌ | ✅ | — | — | ❌ | ◐ per-strategy | ❌ | ❌ | — |
| | Externally supplied targets (stdin/CLI) | ❌ | ❌ | ✅ `--query` | ❌ | ✅ stdin `wxh+x+y` | ✅ | ❌ | ❌ | — |
| | Per-app discovery rules (roles/states/scale) | ◐ root strategy | ✅ `application_rules` | — | — | — | ✅ `clickable_roles` + `app_configs` | ◐ exclude list | ❌ | — |
| **Scope** | Focused window | ✅ | ✅ | — | ✅ `windowzoom` | ✅ (active-win helper) | ✅ | ✅ | ✅ | — |
| | Whole screen / monitor | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ (grid) | ❌ | ❌ | — |
| | Monitor-select mode | ❌ | ❌ | ✅ `screen` | ◐ | ❌ | ✅ | ❌ | ❌ | ❌ |
| | Taskbar / panels | ✅ hotkey | ❌ | — | — | — | ❌ (macOS only) | ❌ | ❌ | — |
| **Labels** | Configurable alphabet | ❌ fixed A–Z | ✅ `alphabet` | ✅ `hint_chars` | ✅ (grid) | ✅ | ✅ | ◐ (edit script) | ✅ config | — |
| | Home-row default | ❌ | ✅ `asdfg…` | ❌ a–z | — | — | ✅ | ? | ? | — |
| | Overlap handling | ❌ (#21) | ✅ layers (0.1.1) | — | — | — | ◐ visible-check | ❌ | ❌ | — |
| | Label case option | ❌ | ✅ `hint_upercase` | — | — | — | ? | — | — | — |
| **Selection** | Type label | ✅ | ✅ | ✅ | ✅ grid-nav | ✅ | ✅ | ✅ | ✅ | — |
| | Text search of element names | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ `/` and `--text` | ❌ | ❌ | — |
| | Role filter at invocation | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ `--role button` | ❌ | ❌ | — |
| | Undo step / backspace | ✅ | ? | ✅ `hint_undo(_all)` | ◐ | ✅ (even across modes) | ✅ | ✅ | ? | — |
| | Auto-commit on unique match | ❌ (Space) | ✅ | ✅ | — | ✅ | ◐ (configurable) | ❌ (Space) | ✅ | — |
| **Actions** | Left/right click | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| | Middle click | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ |
| | Double / triple click | ◐ double | ✅ count prefix `2jk` | ◐ | ✅ `doubleclick` | ❌ | ✅ chains | ❌ | ❌ | ? |
| | Hover / move | ✅ `Shift+M` | ✅ Ctrl+label | ✅ (warp) | ✅ `warp` | ✅ | ✅ | ❌ | ❌ | ✅ |
| | Modifier-click | ◐ ctrl, shift | ❌ | ❌ | ✅ drag w/ modifiers | ❌ | ✅ sticky modifiers (any) | ❌ | ✅ "shifted state" alt action | ✅ (remap) |
| | **Drag** | ❌ | ✅ Alt+label | ✅ `v` | ◐ ("sometimes doesn't work") | ✅ `--drag` two picks | ✅ `Shift+I/U` | ❌ | ❌ | ✅ grab |
| | **Scroll** | ❌ | ✅ `--mode scroll` | ✅ `e/r` accel | ✅ buttons 4–7 | ❌ (compositor binds) | ✅ scroll mode | ✅ j/k | ❌ | ✅ |
| | Text select + copy | ❌ | ◐ (drag) | ✅ `v` then `c` copy_and_exit | ❌ | ✅ (drag doc'd for text) | ◐ drag | ❌ | ✅ focus text | ❌ |
| | Semantic invoke (a11y action) | ✅ activator chain | ❌ synthetic click | ❌ | ❌ | ❌ | ❌ (click) | ? | ✅ (links, focus) | ❌ |
| | **History / repeat-last** | ❌ | ❌ | ✅ history mode, `C-o/C-i` | ✅ `record`/`playback` macros | ❌ | ◐ save/restore cursor recipe | ❌ | ❌ | ❌ |
| **Chaining** | Stay in mode after action | ❌ | ❌ | ✅ (drops into normal; `--oneshot` to exit) | ✅ (omit `end`) | ✅ mode chains | ✅ fresh labels after pick | ✅ normal mode | ✅ **live label update** | ✅ layers |
| **Fallback** | **Grid / recursive grid** | ❌ | ❌ | ✅ grid + 2-pass `hint2` | ✅ grid, cut/move | ✅ tile/bisect/split | ✅ grid, recursive, bisect | ❌ | ❌ | ❌ |
| | **Pointer "normal mode"** | ❌ | ✅ hjkl + ramp | ✅ accel/decel | ✅ | ◐ via compositor | ✅ arrow nudge + glide | ❌ | ❌ | ✅ |
| **Integration** | Global hotkey | ✅ | ◐ (DE binding) | ✅ X / ❌ Wayland | ✅ | ◐ (compositor) | ✅ / ◐ Wayland | ✅ | ? | ✅ |
| | CLI | ❌ | ✅ `hints --mode` | ✅ `--hint --oneshot --click --move` | ✅ commands as args | ✅ `-o` options | ✅ every hotkey cmd = CLI | ❌ | ✅ | ✅ |
| | IPC / API | ✅ named pipe | ✅ unix socket + D-Bus | ❌ | ❌ | ❌ | ✅ versioned IPC + JSON schemas + Go client | ❌ | ❌ | ❌ |
| | **Query / event stream** | ❌ | ❌ | ✅ `print` coords | ❌ | ✅ `--print-only` | ✅ `neru query hints`, `neru watch` | ❌ | ❌ | ❌ |
| | Hooks / shell-exec | ❌ | ❌ | ❌ | ✅ `sh` | ❌ | ✅ hooks + `--on-exit` | ❌ | ❌ | ✅ |
| **Config** | Per-app profiles | ◐ window rules (root) | ✅ by app name | ❌ | ❌ | ❌ | ✅ applied on focus change | ◐ exclude | ❌ | ❌ |
| | Theme (colors/font/size/opacity) | ❌ | ✅ RGBA, font, size | ✅ colors, font, size, radius | ◐ | ✅ config | ✅ themes, indicators | ❌ | ✅ | — |
| | Keymap config | ◐ hotkeys + click keys | ✅ | ✅ every key | ✅ | ✅ | ✅ | ◐ | ✅ | ✅ |
| | Diagnostics (`doctor`) | ◐ log viewer | ◐ `-v/-vv`, `--setup` | ◐ `-f` | ❌ | ◐ `--version` shows features | ✅ `neru doctor`, `roles --explain` | ❌ | ❌ | ❌ |
| **Perf** | Warm daemon / caching | ? | ✅ daemon + window cache ("2–3× faster", 0.1.0) | ✅ daemon | ✅ daemonize | ❌ | ✅ "latency is a bug" | ✅ ahead-of-time pre-query | ? | ✅ |
| **Visual/a11y** | DPI/scale correction | ? | ✅ per-app `scale_factor` | ◐ | ❌ | ◐ | ✅ | ? | ◐ Xft.dpi caveat | — |
| | Mode indicator | ✅ tray letter | ❌ | ✅ `indicator` corner | ❌ | ❌ | ✅ indicator + tray | ✅ tray | ❌ | ❌ |

`?` means it isn't documented in the primary sources reviewed.

**Per-tool cell sources:**
- **hints:** [README](https://github.com/AlfredoSequeida/hints), [`constants.py`](https://github.com/AlfredoSequeida/hints/blob/main/hints/constants.py), [`cli.py`](https://github.com/AlfredoSequeida/hints/blob/main/hints/cli.py), and the source tree (`dbus.py`, `ipc_constants.py`, `service.py`)
- **warpd:** [`config.c`](https://github.com/rvaiya/warpd/blob/master/src/config.c), [`warpd.1.md`](https://github.com/rvaiya/warpd/blob/master/warpd.1.md)
- **keynav:** [`keynav.pod`](https://github.com/jordansissel/keynav/blob/master/keynav.pod)
- **wl-kbptr:** [README](https://github.com/moverest/wl-kbptr)
- **Neru:** [README](https://github.com/y3owk1n/neru), [using-neru](https://github.com/y3owk1n/neru/blob/main/docs/guide/using-neru.md), [platform-support](https://github.com/y3owk1n/neru/blob/main/docs/reference/platform-support.md)
- **vimium-everywhere, goodnight-mouse, mouseless:** their READMEs

---

## 4. Gap register: features WH lacks, with baseline overlap

Ranked by the #94 lens: voice/Talon + accessibility users first, keyboard power users second. "Baseline" means the gap overlaps an existing map or issue from the #94 Notes, so it should be dispositioned *fold into X*.

| # | Gap (WH lacks) | Linux evidence | Baseline overlap | Notes |
|---|---|---|---|---|
| G1 | **Grid / recursive-grid / bisect fallback** for surfaces with no tree | warpd grid + `hint2`, keynav, wl-kbptr tile/bisect/split, Neru grid/recursive/bisect | **None** (Talon has its own mouse grid, see #98) | Universal pattern. Every pointer tool has one. Neru's README calls recursive grid "⭐ … Anything, including canvases and games". |
| G2 | **Screenshot CV / contour discovery** as a fallback when UIA is empty | hints `opencv` backend after AT-SPI; wl-kbptr `detect`; Neru `contour`; viMouse | Partial **#20** (population strategies). Otherwise new | A fallback chain (`["atspi","opencv"]`) is the hints default. |
| G3 | **OCR text targets** (click a word) | Neru `vision`: tesseract on Linux, **Windows.Media.Ocr on Windows** | **None** | Strong voice fit: "click Save" without a label. A Windows API precedent exists in a shipping tool. |
| G4 | **Externally supplied targets + query/event output** | warpd `--query` reads `label x y` from stdin; wl-kbptr floating areas from stdin and `--print-only`; Neru supplies targets, plus `neru query hints`, `neru watch` (JSON events), a versioned IPC schema and a Go client | Partial: WH has a named pipe (`SELECT`), but there's no query/export of hints, no event stream and no target injection. Nearest are **#51** (live UIA tool) and **#26** (architecture) | High value for Talon. Lets Talon read hint names/rects and drive or render its own UI. |
| G5 | **Text search / role filter over element names** (`/` search, `--text save`, `--role button`) | Neru | Possibly **#21** (hinting improvements). Otherwise new | Voice-friendly selection by name instead of label. |
| G6 | **Drag** (hint-to-hint or two-pick drag; text selection by drag) | hints Alt+label; warpd `v`; wl-kbptr `--drag` (start = left-center of pick 1, end = right-center of pick 2, "useful for selecting text"); Neru `Shift+I/U`; keynav `drag` | **None** (#25 covers multi-action buttons, not drag) | wl-kbptr's two-pick edge-anchored design is a ready interaction spec. |
| G7 | **Scroll from hint mode** | hints `--mode scroll` (hjkl + ramp-up); warpd `e/r` with accel; Neru scroll mode; vimium-everywhere j/k | **Fold into #54/#62** (scroll-region maps) and #70 | Already covered by the baseline. Linux confirms the demand. |
| G8 | **Pointer "normal mode"** (hjkl / arrow nudge, accel/decel, nudge-before-click) | warpd normal mode; hints hjkl; Neru arrow nudge + `held_repeat` glide; mouseless | **None** | Neru lets you "correct a near miss before you click". |
| G9 | **Stay-in-mode / live label refresh after action** | goodnight-mouse "update and add new labels when the application changes while GM is open"; Neru "fresh labels appear so you can pick again"; warpd drops into normal mode (`--oneshot` opts out); wl-kbptr chained modes | **Fold into #72** (chained hinting) | Linux prior art for #72's always-on-until-Esc design. |
| G10 | **History / repeat-last / macros** | warpd history mode (hints over past targets) + `hist_back/forward` + `--record`; keynav `record`/`playback` to `~/.keynav_macros` | **None** (#70 covers scroll jump only) | Voice "again" / "repeat" fits well. |
| G11 | **Overlapping-hint layers** | hints 0.1.1 "layers feature to allow viewing hints that are overlapping": Space toggles the active layer, inactive layers drawn at 0.7 alpha | **Fold into #21** ("handling overlapping hints") | A concrete design for an open #21 bullet. |
| G12 | **Configurable alphabet / home-row / label case** | hints `alphabet` (home row first) + `hint_upercase`; warpd `hint_chars`; Neru; keyhop presets + exclude-ambiguous | **Fold into #26** (hint-engine phase) | WH is hard-coded `A–Z`. Voice users also benefit from spoken-alphabet-friendly sets. |
| G13 | **Theme: colors, font, size, opacity, corner radius** | hints RGBA/font/size; warpd `hint_bgcolor/fgcolor/font/size/border_radius`; Neru themes | **Fold into #26** (overlay composer, preferences, per-pixel alpha); #21 color-coding | Visual a11y: contrast and size for low-vision users. |
| G14 | **Per-app discovery rules** (role allow/deny, state filters, scale factor, CV thresholds) applied on focus change | hints `application_rules` keyed by app name; Neru `app_configs` keyed by WM_CLASS/app_id/**exe path on Windows** | **Fold into #62** (profile = window rule grown) and **#20** | WH window rules only pick a root strategy. |
| G15 | **Multi-monitor select / whole-screen scope** | warpd `screen` mode; Neru `monitor_select`; keyhop scopes (Windows) | **None** (#81 covers taskbar only) | |
| G16 | **Middle click, triple click, count-prefixed repeat clicks, sticky arbitrary modifiers** | hints `2jk`; warpd/keynav/wl-kbptr/Neru middle; Neru sticky Cmd/Ctrl/Alt/Shift + click chains | Partial: WH has ctrl/shift-click only. **#25** for alternate actions | Low effort, incremental. |
| G17 | **Mode/oneshot CLI** (`--hint --oneshot`, `--click`, `--move x y`) | warpd flags; hints `--mode`; Neru "every hotkey command is also a shell command" | Partial: WH has the named pipe but no CLI. Nearest is **#26** | Makes Stream Deck/script integration easier. |
| G18 | **Diagnostics: `doctor`, role explain, guided setup** | `neru doctor`, `neru roles --explain`; hints `--setup`, `-vv` per-element logging | **Fold into #19** (logger UI) / **#51** | |
| G19 | **Warm-daemon / pre-scan on focus change** | hints 0.1.0 daemon + `window:activate` cache ("2–3× performance increase"); vimium-everywhere ahead-of-time query; keyhop 500 ms tree cache | **Fold into #26** (scan pipeline) / #7 | |
| G20 | **Alternate "shifted" semantic action** (e.g. close tab) | goodnight-mouse "hold shift… alternative actions, such as closing a tab" | **Fold into #25** | |

Not gaps (WH is equal or ahead):
- **Semantic invoke.** WH's UIA activator chain beats the synthetic clicks used by hints, warpd and Neru.
- **Taskbar hinting.** No Linux tool hints panels. Neru does menu bar and Dock only on macOS.
- **Tray mode indicator.**
- **Named-pipe IPC.** Only hints and Neru have an equivalent.

---

## 5. Cross-ticket notes (for synthesis, #100)

- **Neru is a direct Windows competitor**, not just a Linux one.
  - **Status:** Windows 10+ is "Beta… every mode works as on a stable platform" with "no known gaps".
  - **Windows stack:** UIA hints, `Windows.Media.Ocr`, contour, grids and per-app config by exe path.
  - **Context:** MIT licensed, very active (pushed 2026-10-13), with a voice/status-bar integration story (`neru watch`, Go client).
  - **Follow-up:** #95 should inventory it. It's arguably the closest feature superset of WH in the competitive set. ([platform-support](https://github.com/y3owk1n/neru/blob/main/docs/reference/platform-support.md))
- **keyhop** is Windows UIA hinting (Rust, Apache-2.0, v0.4.0). Its Windows-side features belong in #95:
  - A window picker
  - Chromium a11y wake via `WM_GETOBJECT`
  - Shortest-first variable-length labels
  - Badge→element leader lines
  - `active_monitor` / `all_windows` scopes
  ([README](https://github.com/rsaz/keyhop))
- **warpd has a Windows alpha** (`v1.3.7-windows-alpha`, 2023-04), but the project has been dormant since 2023.
- **Wayland-era constraints:** Wayland forbids global hotkeys and pointer warping without compositor protocols, so tools fall back to compositor binds, `/dev/uinput`, or `wlr-*` protocols. WH has none of these constraints, so no Wayland-specific feature translates. Only the interaction ideas do.

## 6. Open questions / uncertainty

- `?` cells weren't documented in the sources reviewed. vimium-everywhere and goodnight-mouse in particular were read from READMEs only.
- The hints README doesn't name AT-SPI. The backend list comes from source (`hints/backends/`).
- Neru's claims are vendor docs and weren't verified hands-on. Neru-on-Windows parity is the claim most likely to matter for dispositions. #94 "Not yet specified" allows a hands-on check if a disposition hinges on it.
