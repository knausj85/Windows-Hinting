# Competitive research: macOS OS-wide keyboard hinting tools

Ticket: #96 (child of wayfinder map #94). Desk research only, done 2026-10-13. No tool was installed. Every claim cites a primary source: the vendor site or docs, a README, a changelog, or the issue tracker. When a claim is inferred rather than stated, the text says so.

**Windows-Hinting (WH) reference point:** `main` @ `e331d0d` README, plus a code check for things the README does not mention:
- hint labels use a fixed `A–Z` alphabet (`Windows-Hinting/Labels.cs`)
- scope is the foreground window, plus a separate taskbar hotkey
- you select by typing the label, then commit with Space or Enter
- `Shift+key` sets the click type: left, right, double, move, Ctrl-click or Shift-click
- the tray icon shows the current click mode
- a named pipe `WindowsHinting_Pipe` takes `TOGGLE`, `TOGGLETASKBAR`, `SELECT <label> [LEFT|RIGHT|DOUBLE|MOVE|CTRL|SHIFT]` and `DEACTIVATE` (`Services/NamedPipeService.cs`)

## Tools covered

| Tool | Status (as of 2026-10-13) | License / price | Primary sources |
|---|---|---|---|
| **Homerow** | Active, closed source. v1.5.3, 2026-03-19. Issue tracker is active (#227 filed 2026-09-09). | Proprietary. $49.99 one-time, free trial with a reminder every 50 activations. macOS 13+. | [homerow.app](https://www.homerow.app/), [changelog](https://www.homerow.app/changelog), [README / issue tracker](https://github.com/nchudleigh/homerow) |
| **Shortcat** | Semi-active, closed-source beta. v0.12.2, 2025-07-03. It went quiet from v0.11.4 (2023-01) to v0.12.0 (2025-04). v0.12.1 says "Bump beta expiry to 2030". | Proprietary EULA, free beta download. macOS 13+. | [shortcat.app](https://shortcat.app/), [docs](https://shortcat.app/docs/), [changelog](https://shortcat.app/changelog) |
| **Vimac** | **Superseded.** README: "Vimac is now Homerow". Last push 2022-12-31. | GPL-3.0 (source available, 3.7k stars) | [README](https://github.com/nchudleigh/vimac) |
| **Scoot** | Low activity, maintained. v1.3 (2026-03-02) is a multi-display fix; the release before it, v1.2, was 2022-12. README lists macOS 11/12. | BSD-3-Clause (open source) | [README](https://github.com/mjrusso/scoot), [v1.3 release](https://github.com/mjrusso/scoot/releases/tag/v1.3) |
| **Wooshy** | Active. Build 51 released 2026-10-08. Closed source, public docs/issue repo. | Proprietary. "Subscribe for a coffee a month"; without a subscription it "takes some random naps". macOS 14.6+. | [wooshy.app](https://wooshy.app/), [docs](https://docs.wooshy.app/), [Wooshy.blahblah repo](https://github.com/godbout/Wooshy.docs) |
| **Neru** *(found)* | **Very active.** v1.58.0, 2026-10-12. Pushed 2026-10-13. 810 stars. **Cross-platform: macOS Stable, Windows 10+ Beta, Linux Beta.** | MIT (open source) | [README](https://github.com/y3owk1n/neru), [docs/](https://github.com/y3owk1n/neru/tree/main/docs) (`using-neru.md`, `platform-support.md`, `integrations.md`, `reference/cli.md`, `reference/configuration.md`) |
| **Superkey** (Seek & Click) *(found)* | Active, v1.66 | Proprietary, 20-day trial, 3 devices. macOS 12+. | [superkey.app](https://superkey.app/) |
| Mouseless *(found, reference tier iv)* | Active, cross-platform (macOS/Windows/Linux) | Proprietary, trial then lifetime or subscription | [mouseless.click](https://mouseless.click/) |

Mouseless is keyboard mouse control with a grid and free movement, and no element hints. It sits in the map's reference tier (iv), so it is noted but not scored. The Wooshy README also names "Superkey" among competing tools ([Wooshy.docs README](https://github.com/godbout/Wooshy.docs)).

> **Flag for #95 (Windows) and synthesis:** Neru ships a **Windows 10+ Beta** that it says has "no known gaps" against macOS. It reads UIA, does OCR through `Windows.Media.Ocr`, and draws overlays with Direct2D ([platform-support.md](https://github.com/y3owk1n/neru/blob/main/docs/reference/platform-support.md)). That makes it a **direct Windows competitor**, MIT-licensed and readable. Its listed Windows weakness: it "misses elements an app exposes only in UI Automation's raw view", and it can't act on elevated windows unless Neru itself runs elevated.

## Tool × feature matrix (common taxonomy, #94)

Legend: ✅ documented · ⚠️ partial or alpha · ❌ absent or not documented · — not applicable. "WH" = Windows-Hinting `main`.

### Target discovery

| | Homerow | Shortcat | Vimac | Scoot | Wooshy | Neru | Superkey | **WH** |
|---|---|---|---|---|---|---|---|---|
| AX / UIA tree | ✅ | ✅ AX ([docs](https://shortcat.app/docs/)) | ✅ | ✅ AX ([README](https://github.com/mjrusso/scoot)) | ✅ AX metadata ("labels, titles, values, tooltips, placeholders, types") ([search](https://docs.wooshy.app/usage/search)) | ✅ `axtree` | ✅ AX "for compatible applications" | ✅ UIA |
| OCR / CV fallback | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ `vision` (Apple Vision; Windows.Media.Ocr) and `contour` (shape detection) | ✅ OCR is the primary method ("Seek uses OCR") | ❌ |
| Per-app detection strategy | ⚠️ browser "Fast/Exhaustive modes" (changelog 1.5.0) | ❌ | ❌ | ❌ | ❌ | ✅ `strategy` overridable per app | ❌ | ❌ |
| External targets injected by another tool | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ `neru hints --targets -` (JSON on stdin) | ❌ | ❌ |
| Chromium/Electron AX enablement | ✅ "instant navigation tree loading" (1.5.0) | ✅ Electron listed | ? | ? | ? | ✅ "enhanced Electron/Chromium accessibility" | ? | ⚠️ (baseline: #20, #26) |
| Visibility hit-test / skip disabled | ? | ✅ "Visible elements given a scoring boost" (0.11.1) | ? | ? | ? | ✅ `visible_check_enabled`; disabled elements get no label | ? | ? |

### Scope

| | Homerow | Shortcat | Vimac | Scoot | Wooshy | Neru | Superkey | **WH** |
|---|---|---|---|---|---|---|---|---|
| Focused window | ✅ | ✅ | ✅ "frontmost window" | ✅ | ✅ | ✅ | ✅ "anywhere on the screen" | ✅ |
| Menu bar / status items | ✅ (issue #195; "menu bar status detection" 1.5.0) | ✅ menus and menu items, with their keyboard shortcuts shown (0.10.0) | ? | ❌ | ✅ "the left menu bar and the right status bar" | ✅ `include_menubar_hints` (macOS only) | ✅ (screen OCR) | ⚠️ taskbar hotkey only. Tray/menu work is baseline #78/#79/#81 |
| Dock / taskbar | ? | ✅ "Application Dock Item" role | ? | ❌ | ✅ "the Dock" | ✅ `include_dock_hints`, Notification Center, Stage Manager, PiP | ✅ | ✅ Ctrl+Alt+T (baseline #81) |
| Window switcher / Mission Control | ✅ Mission Control labels (1.5.0) | ✅ window search by title; minimized windows indexed (0.10.0) | ❌ | ❌ | ? | ✅ `detect_mission_control` (macOS only) | ❌ | ❌ |
| Browser tabs as targets | ? | ✅ "Tabs enumerated in WebViews"; `tab` keyword (0.10.3, 0.11.0) | ? | ? | ? | ? | ? | ❌ |
| Multi-monitor | ⚠️ labels on one display. Open requests #227 and #216 ask for all displays. | ? | ? | ✅ grid spans "all connected screens" | ? | ✅ "monitor select" mode; hints draw on the monitor under the cursor | ? | ⚠️ foreground window only |

### Label scheme

| | Homerow | Shortcat | Vimac | Scoot | Wooshy | Neru | Superkey | **WH** |
|---|---|---|---|---|---|---|---|---|
| Configurable alphabet | ✅ "Label Characters" setting (issues #72, #41) | ❌ | ? | ? | — (search-first, no labels) | ✅ `hint_characters` (default `asdfghjkl`) | — | ❌ fixed `A–Z` |
| Home-row default | ✅ (by name) | ? | ✅ | ✅ avy-style "char-based decision tree" | — | ✅ | — | ❌ |
| Label-length strategy | ? | 1–2 characters | ? | ✅ avy-style | — | ✅ `label_direction` normal/reverse (how many single-character labels are kept) | — | ❌ |
| Label placement and theme | ✅ Dark/Light/Original themes (1.0.0) | ✅ black-on-yellow, grey border (0.10.4) | ? | ✅ fonts, colours, opacity; live contrast ±, `⌘=` / `⌘-` | — | ✅ `font_size`; `placement` top/center/bottom with a connector arrow; theming | — | ❌ (preferences cover hotkeys only) |
| Label delay | ❌ | ✅ "Show Shortcuts Speed": immediately up to 5 s, or after input | ❌ | ❌ | — | ❌ | — | ❌ |

### Selection

| | Homerow | Shortcat | Vimac | Scoot | Wooshy | Neru | Superkey | **WH** |
|---|---|---|---|---|---|---|---|---|
| Type the label | ✅ | ✅ (and the label can be appended to a query, 0.10.2) | ✅ | ✅ | — | ✅ | — | ✅ |
| **Type element text (search)** | ✅ search mode is the default; `Shift/` | ✅ **fuzzy, plus synonyms** ("delete/remove/clear/destroy") and emoji | ❌ | ❌ | ✅ **fuzzy, word parts in any order** | ✅ `/` search inside hints | ✅ OCR text match | ❌ |
| Role / type filter in query | ? | ✅ `btn`, `radio`, `check`, `tab`, `Link`… | ❌ | ❌ | ✅ "Role Search" ("log but") | ✅ `--role` | ❌ | ❌ |
| Result list navigation | ✅ Tab / arrows | ✅ arrows, Tab, `Ctrl+N` / `Ctrl+P`, `Cmd+1–9` to act on the top 10 | ❌ | ❌ | ✅ Tab, arrows, ctrl-n/p, first/last/half-page, kindaVim | ? | ? | ❌ |
| Show all targets | ✅ | ✅ | ✅ | ✅ | ✅ `*` | ✅ | ? | ✅ |
| Inspector: which terms reach an element | ❌ | ❌ | ❌ | ❌ | ✅ "The Inspector" (`⌘H` / `⌘I`) | ✅ `neru roles --explain` | ❌ | ❌ (baseline #51) |

### Actions

| | Homerow | Shortcat | Vimac | Scoot | Wooshy | Neru | Superkey | **WH** |
|---|---|---|---|---|---|---|---|---|
| Left click / invoke | ✅ Return | ✅ Enter | ✅ | ✅ | ✅ | ✅ `Shift+L` | ✅ | ✅ UIA invoke |
| Right click | ✅ `Shift-Return` | ✅ `Ctrl+Enter` | ✅ | ✅ | ✅ `ctrl return` | ✅ `Shift+R` | ? | ✅ |
| Double click | ? | ✅ Enter twice | ✅ | ✅ | ✅ | ✅ chained actions; **triple** click too | ? | ✅ |
| Middle click | ❌ | ❌ | ❌ | ✅ `[` | ❌ | ✅ `Shift+M` | ❌ | ❌ |
| Modifier clicks | ⚠️ Cmd only | ✅ any modifier with Enter | ❌ | ✅ any combination is passed through | ✅ Cmd, Shift, Option | ✅ **sticky modifiers** (tap Cmd/Ctrl/Alt/Shift to hold) | ? | ⚠️ Ctrl and Shift only; no Alt or Win, no combinations |
| Hover / move cursor | ❌ (open #187) | ✅ tap Ctrl | ❌ | ✅ | ✅ `shift command return` | ✅ (every mode moves the cursor) | ? | ✅ Shift+M |
| **Drag & drop** | ❌ | ❌ | ❌ | ✅ `=` holds, Return releases | ❌ | ✅ `Shift+I` holds / `Shift+U` releases; right and middle drag too | ❌ | ❌ |
| Arrow-key nudge after selection | ❌ (open #225) | ⚠️ alpha: Space+HJKL sends arrows to the app | ❌ | ✅ partial and full steps, edges, center | ❌ | ✅ arrows nudge; held-key glide with acceleration | ❌ | ❌ |
| **Scroll mode** | ✅ `⇧⌘J`: HJKL, dash, d/u, gg/G, Tab or 0-9 to pick a scroll area | ⚠️ alpha: Space+Cmd+HJKL | ✅ red border, HJKL | ✅ Shift+arrows at the cursor | ❌ | ✅ j/k, u/d, gg/G, h/l, smooth | ❌ | ❌ (baseline #54/#62) |
| **Copy element text / metadata** | ❌ | ❌ | ❌ | ❌ | ✅ `⌘C` copies text; `⌘⌥C` etc. copy URL or state | ❌ | ❌ | ❌ |
| Insert emoji | ❌ | ✅ `:` emoji mode | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Auto-click on unique label | ✅ toggleable "automatic clicks" (issue #199, #50) | — | ✅ | ⚠️ moves the cursor; Return clicks | — | ✅ opt-in `hints --action left_click` | — | ❌ always needs Space or Enter |
| Pre-select the action before labels show | ✅ by turning auto-click off, then picking the key (#199) | — | — | — | — | ✅ `--action` flag on the hotkey binding | — | ✅ Shift+key mode (tray shows it) |

### Chaining / persistence

| | Homerow | Shortcat | Vimac | Scoot | Wooshy | Neru | Superkey | **WH** |
|---|---|---|---|---|---|---|---|---|
| Re-hint after an action | ✅ "click chain" (1.4.0); off for input fields (1.5.0); refresh-label bugs open (#218, #188) | ⚠️ alpha **Continuous Mode**: 500 ms re-index; **auto-exits on text-field focus or when a menu appears**; `Cmd+R` re-index | ❌ | ❌ | ❌ | ✅ `--repeat`; "fresh labels appear so you can pick again" | ❌ | ❌ (baseline #72) |
| Toggle chain from a hotkey | ❌ (open #165, #179) | ⚠️ preference | — | — | — | ✅ binding flag | — | — |

### Fallback modes

| | Homerow | Shortcat | Vimac | Scoot | Wooshy | Neru | Superkey | **WH** |
|---|---|---|---|---|---|---|---|---|
| Grid | ❌ (open #134) | ❌ | ❌ | ✅ grid over all screens; cell size ± | ❌ | ✅ grid, **recursive 3×3 grid**, **bisect** | ❌ | ❌ |
| Free cursor / mouse mode | ❌ | ❌ | ❌ | ✅ freestyle with Emacs/vi/system bindings | ⚠️ clicks at the current mouse position when the query is empty | ✅ | ❌ | ❌ |

### Integration

| | Homerow | Shortcat | Vimac | Scoot | Wooshy | Neru | Superkey | **WH** |
|---|---|---|---|---|---|---|---|---|
| CLI | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ full `neru` CLI | ❌ | ❌ |
| IPC / API | ❌ (URL scheme requested in #184) | ❌ | ❌ | ❌ | ✅ URL scheme `wooshy://openTheInput?with=<query>` | ✅ JSON IPC socket, schemas, Go client | ❌ | ✅ named pipe (toggle / select / deactivate) |
| **Query labels/elements from outside** | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ `neru query hints --json`: label, title, role, rect | ❌ | ❌ |
| **State events to other tools** | ❌ | ❌ | ❌ | ❌ | ✅ distributed notifications `WooshyInputDidAppear` / `Disappear`; Karabiner variable `WsInputStatus` | ✅ `neru watch` event stream; `[hooks]`; `--on-exit` passes `NERU_X` / `NERU_Y` | ❌ | ⚠️ tray icon only |
| Voice / Talon | ❌ | ⚠️ VoiceOver announces the selected result (0.11.0) | ❌ | ❌ | ❌ | ✅ documented "Voice control" recipe (query → feed label) | ❌ | ✅ Talon example (README) |
| Activation variants | ✅ modifier-only shortcuts; Caps Lock → Hyper; hotkey toggles | ✅ `Cmd+Shift+Space` | ✅ hold Space | ✅ per-mode hotkeys | ✅ Karabiner | ✅ per-mode bindings, custom modes | ✅ Hyperkey | ✅ configurable hotkeys |

### Config

| | Homerow | Shortcat | Vimac | Scoot | Wooshy | Neru | Superkey | **WH** |
|---|---|---|---|---|---|---|---|---|
| Per-app profiles | ⚠️ ignored apps only (1.3+). Per-app shortcuts requested (#78), custom scroll areas per app requested (#160). | ❌ | ❌ | ❌ | ⚠️ through Karabiner conditions | ✅ **per-app overrides** for strategy, label direction, visibility check… | ❌ | ❌ (baseline #62 profile layer) |
| Remappable in-mode keys | ⚠️ | ❌ | ? | ✅ system, Emacs or vi keymaps | ✅ through Karabiner | ✅ every binding | ? | ⚠️ only the Shift+key action keys |
| Config as file / reload | ❌ GUI | ❌ GUI | ❌ | ❌ GUI | ❌ | ✅ one TOML file, JSON schema, `neru config reload` / `set` | ❌ | ❌ GUI |

### Visual / a11y, performance

| | Homerow | Shortcat | Vimac | Scoot | Wooshy | Neru | Superkey | **WH** |
|---|---|---|---|---|---|---|---|---|
| Contrast / size control | ⚠️ 3 themes | ⚠️ fixed, improved legibility | ? | ✅ live contrast ±; font, colour, opacity | — | ✅ font size, placement, theme | — | ❌ (baseline #26 overlay composer) |
| Sound feedback | ✅ click and warning sounds (1.5.0) | ❌ | ❌ | ❌ | ❌ | ⚠️ alerts and notifications | ❌ | ❌ |
| Screen-reader output | ❌ | ✅ VoiceOver | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Hide overlay from screen share | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ (macOS only) | ❌ | ❌ |
| Performance claims | "instant navigation tree loading" for Chromium | depends on the "target application returning Accessibility API calls quickly" | — | — | — | `max_depth`; grid prewarm | OCR: weak on small text | — |

## Gaps: features WH lacks

`Baseline` means the gap overlaps an existing map or issue in the #94 Notes (disposition hint: *fold into X*). `New` means nothing in the baseline covers it. Rank order follows the #94 lens: voice/Talon and accessibility users first, keyboard power users second.

| # | Gap | Seen in | Baseline overlap | AX-specific vs UIA-translatable |
|---|---|---|---|---|
| 1 | **Text-search selection**: fuzzy match on element name, value, tooltip or placeholder, with synonyms and a role filter (`btn`, `link`). Typing what you see instead of a label. For voice users, "click Save" with no label read-back. | Shortcat, Wooshy, Homerow, Neru, Superkey | Partial: #21 "hinting improvements" is generic. Otherwise **New**. | Translates. UIA `Name`, `HelpText`, `LocalizedControlType`, `ValueValue` and `AutomationId` map onto AX title, help, role and value. Synonyms and fuzzy matching are pure app logic. |
| 2 | **External query of labels/elements** (`query hints --json`: label, title, role, rect) **and a state event stream** (`watch`, hooks, distributed notifications). Lets Talon run "click <name>" or build its own lists without OCR. | Neru (query + watch), Wooshy (notifications, URL scheme) | Partial: WH's named pipe can SELECT a label but can't READ labels or emit events. Not in any baseline map. **New.** (Neighbors: #26 phase "logging/updates"; Talon research branches `research/talon-rpc` / `talon-direct-channel` exist but are not in the #94 baseline list.) | Translates directly: IPC-level work, no OS-specific dependency. |
| 3 | **Grid / recursive-grid / bisect fallback** for canvases, games, RDP, and apps with thin UIA trees | Neru, Scoot (Homerow #134 requested) | **New** (no map) | Translates fully (screen-coordinate based). Neru already ships it on Windows. |
| 4 | **OCR / CV hint fallback**: label text and shapes when the tree is empty | Neru (`vision` / `contour`, including Windows.Media.Ocr), Superkey | Partial: #20 "UIA population strategies" covers tree strategies, not OCR. Likely **New** (#94 already notes it as a possible cross-cutting pattern). | Translates: `Windows.Media.Ocr` exists on Win10+. Neru's Windows build uses it but needs an OCR language pack and gets no per-word confidence. |
| 5 | **Chained / continuous hinting**: re-hint after each action, with auto-exit on text-field focus or menu open | Homerow (click chain), Shortcat (Continuous Mode), Neru (`--repeat`) | **Baseline #72** (fold). Prior art: Shortcat's exit-on-text-field and exit-on-menu rules plus a 500 ms re-index; Homerow disables chaining on input fields. | Translates. UIA focus-changed and MenuOpened events replace the AX notifications. |
| 6 | **Configurable alphabet / home-row labels / label-length strategy** | Homerow, Neru, Scoot | Partial: #21. **New** as a specific item. | Translates (pure labeling logic; WH `Labels.cs` is hard-coded A–Z). |
| 7 | **Drag & drop** (hold the button at one target, release at another) and **arrow-key nudge** of the cursor after a selection | Neru, Scoot (Homerow #225 asks for HJKL in click mode) | **New**. #25 "multi-action buttons" is a neighbor, but it covers multiple actions, not drag. | Translates (SendInput mouse down/up; UIA has no drag pattern, so use synthetic input). |
| 8 | **Scroll mode**: HJKL, half-page, gg/G, choosing a scroll area by number or Tab | Homerow, Vimac, Neru, Scoot, Shortcat (alpha) | **Baseline #54 / #62** (fold). Homerow's numbered scroll-area picker matches the #54 select-a-region verdict. | Translates (UIA ScrollPattern plus injected-wheel fallback, per #54). |
| 9 | **Per-app profiles** (detection strategy, ignored apps, label options) | Neru (full), Homerow (ignored apps) | **Baseline #62** (profile layer); "ignored apps" is a small slice of it | Translates. Neru's `bundle_id` becomes the process or AUMID on Windows (`platform-support.md`, app identity). |
| 10 | **Multi-monitor / all-windows scope, monitor select** | Neru (monitor select), Scoot (grid over all screens); Homerow users request it (#216, #227) | Partial: #81 is the taskbar only. **New** for all windows and all monitors. | Translates. UIA can enumerate top-level windows per monitor; overlays need per-monitor DPI (the #81 caveat). |
| 11 | **Window switcher / Mission-Control-style labels** (label every top-level window; search windows by title; minimized windows too) | Homerow (Mission Control), Shortcat (window search) | **New** (the #81 taskbar overlay is adjacent) | Mission Control is macOS-only. The equivalent is a hinted Alt+Tab or Task View, or a UIA top-level window list. Translates conceptually. |
| 12 | **Menu-bar / status-area hints in the same pass** | Wooshy, Shortcat, Neru, Homerow | **Baseline #78 / #79** (menu-bar spin-off), **#81** (tray) | AX menu bar ≈ Win32 menu bars and the UIA MenuBar control type. The tray is Shell_TrayWnd UIA (#81). |
| 13 | **Copy element text / metadata** (copy a link URL, a checkbox state) | Wooshy | **New** | Translates: UIA `Name`, ValuePattern, LegacyIAccessible `Value`. |
| 14 | **Full modifier set and sticky modifiers** (Alt-click, Win-click, combinations; tap a modifier to hold it), **middle click**, **triple click** | Neru, Scoot, Wooshy, Shortcat | Partial: #25 multi-action buttons. Mostly **New**. | Translates (SendInput). |
| 15 | **Auto-commit on a unique label** (toggleable) | Homerow, Vimac, Neru (opt-in) | Partial: #21. **New**. | Translates (pure UX). |
| 16 | **Label theming / contrast / size / placement** | Scoot (live contrast keys), Neru, Homerow | **Baseline #26** (overlay composer, per-pixel alpha, preferences) | Translates (overlay drawing). |
| 17 | **Inspector: why an element is or isn't hinted, and which terms reach it** | Wooshy Inspector, Neru `roles --explain` | **Baseline #51** (live-UIA inspection tool) | Translates. |
| 18 | **Config as file with schema, reload, and `config set` from the CLI** | Neru | Partial: #26 phase "preferences" | Translates. |
| 19 | **External target injection** (another tool supplies rects; WH labels and actuates them) | Neru `--targets` | **New** (pairs with gap 2) | Translates. |
| 20 | Sound feedback, screen-reader announcement of the selection, hiding the overlay from screen share | Homerow, Shortcat, Neru | **New** (small items) | Sounds and the screen-reader announcement translate (UIA Notification event). Screen-share hide is macOS-only per Neru, though Windows has `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`. Neru's claim that "Windows has no counterpart" looks wrong and needs checking. |
| 21 | Browser tabs as targets, emoji insertion, label delay | Shortcat | **New** (low priority) | Translates (UIA TabItem). Emoji is out of WH's core scope. |

## What depends on macOS AX specifics

- **macOS system surfaces**: Dock, Notification Center, Stage Manager, PiP, Mission Control (Neru's macOS-only words; Homerow 1.5.0). Rough Windows analogues are the taskbar (#81), Action Center/notifications, Task View/Alt+Tab, and Snap layouts. Each needs its own UIA work; nothing carries over directly.
- **Electron/Chromium AX enablement**: on macOS, tools set `AXManualAccessibility` / `AXEnhancedUserInterface` (Neru's "enhanced Electron/Chromium accessibility"). On Windows, Chromium turns its UIA/MSAA tree on when it detects an AT client. Neru's note says Windows still "misses elements an app exposes only in UI Automation's raw view", which suggests WH's raw-view and population strategies (#20) could be an advantage.
- **Distributed notifications, URL schemes, Karabiner variables** (Wooshy) are macOS IPC idioms. On Windows the equivalents are named pipes (which WH already has), a custom URI protocol handler, or Window messages.
- **Secure-input detection and cursor hiding** are macOS-only per Neru. Windows has its own boundary: **UIPI/elevation** (input can't be sent to admin windows from a non-elevated process). WH faces the same limit.
- Everything else (search, labels, grid, OCR, drag, scroll, chaining, IPC query, profiles, theming) is OS-neutral logic over the element tree or SendInput, and translates to UIA. Neru's Windows Beta is evidence that this works in practice.

## Notes for synthesis (#100)

- **Neru belongs in the Windows inventory too** (#95). It is the only tool found that covers nearly every taxonomy row, and it already runs on Windows under MIT. Its CLI and IPC model (`query hints --json`, `action feed`, `watch`, `--targets`) is a ready template for gap 2 and the Talon story.
- Search-first (Shortcat, Wooshy) and label-first (Homerow, Vimac, Scoot) have converged: Homerow, Shortcat and Neru all support both. WH is label-only.
- Chaining prior art for #72: Shortcat auto-exits on text-field focus or when a menu opens, re-indexes after 500 ms, and refreshes with `Cmd+R`. Homerow skips chaining on input fields, and its open bugs are about stale labels (#218) and needing a delay (#188). Both match the risks #72 already identified.
- Maintenance risk: Vimac is dead (it became Homerow). Shortcat had a two-year gap and is still in "beta". Scoot is a slow hobby project.
