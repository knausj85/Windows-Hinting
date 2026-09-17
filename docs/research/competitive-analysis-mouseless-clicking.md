# Competitive analysis: mouseless clicking / hint-overlay tools vs. Windows-Hinting

Where does **Windows-Hinting** sit in the landscape of keyboard/voice-driven "click without a
mouse" tools on Windows and macOS, feature by feature — and which compelling features is it
missing?

**Date:** 2026-09-13. Every factual claim below is cited inline to a primary source (the tool's
own GitHub repo, official site, or first-party docs). Where I could not verify something from a
primary source, it is flagged **[unverified]** rather than asserted.

---

## Bottom line (TL;DR)

Windows-Hinting's core — *UIA-driven hint labels on the foreground window's clickable elements,
typed to filter, activated with click-type modifiers* — is a solid, well-executed implementation of
the canonical "hint" paradigm. But the field has moved past plain hint labels, and on Windows one
competitor in particular already ships almost everything Windows-Hinting is missing:

1. **Fluent Search (Screen Search + In-Window Search) is the direct Windows competitor, and it is
   ahead on almost every axis.** It puts hint labels over clickable elements *across the whole
   screen* (not just the foreground window), it has a **text-search-to-click** mode, and — critically
   — it uses a **hybrid UIA + image-recognition (+ optional OCR) engine**, so it still labels
   elements when the accessibility tree is empty. ([Fluent Search — Screen Search][fs-screen],
   [In-window search][fs-inwin])
2. **The biggest concrete gaps in Windows-Hinting are: scroll-region actuation, text-search
   selection, a mouse-grid/bisection fallback, an OCR/vision fallback for empty UIA trees, and
   full-screen / multi-monitor hinting.** Every one of these is shipping in at least one competitor
   today (scroll: Homerow/Vimac/Voice Access; text-search: Shortcat/Fluent Search/Homerow; grid:
   Talon/warpd/win-vind/Voice Access/Voice Control; OCR-or-vision fallback: Fluent Search /
   talon-gaze-ocr; full-screen hinting: Fluent Search / win-vind).
3. The repo is **already actively prototyping the single most-cited gap — scroll actuation** —
   under wayfinder map #54 (`prototype/ScrollExposureProbe`), and auto-hint refresh under #44/#45
   (`prototype/AutoHintDetectionProbe`). So two of the top gaps are in flight; the rest (text
   search, grid, OCR/vision, multi-monitor) are not yet on the prototype bench.
4. Windows-Hinting's genuine differentiators worth *keeping*: default activation via **UIA Invoke**
   (more robust than a synthetic coordinate click for elements that support it), first-class
   **taskbar hinting**, a clean **click-type modifier model** with tray feedback, and an explicit
   **Talon/Stream Deck automation posture**. Its architectural bet (pure UIA) is exactly what makes
   the OCR/vision-fallback gap matter — see the gap analysis.

---

## What Windows-Hinting does today (the baseline)

From its README and prototype source:

- **Hint-label clicking of the foreground window.** Global hotkey → labels ("A", "AB", …) on every
  clickable element discovered via UI Automation; type the label to filter to one; `Space`/`Enter`
  activates. Default activation is **UIA Invoke**, not a synthetic click. (README §"How It Works",
  §"Hint Navigation")
- **Taskbar hinting** on a separate hotkey (`Ctrl+Alt+T`). (README §"Global Hotkeys")
- **Click-type modifiers** via `Shift+key`: left / right / double click, mouse-move, Ctrl+click,
  Shift+click; a tray glyph reflects the pending mode. (README §"Click Action Shortcuts",
  §"Tray Icon")
- **System-tray app**, Preferences dialog, configurable hotkeys/keys, logging. (README §"Tray Icon")
- **Automation posture:** explicitly designed to pair with Talon Voice, Stream Deck, etc., with a
  published Talon integration example. (README top + §"Example Talon Integration")
- **Active R&D on the prototype bench:** scroll-region exposure/actuation via UIA
  (`prototype/ScrollExposureProbe`, wayfinder map #54 — censuses ScrollPattern / ScrollItemPattern /
  RangeValue / ScrollBar signals and *ground-truth tests* whether a UIA scroll actually moves
  content) and auto-hint detection / redraw-refresh (`prototype/AutoHintDetectionProbe`, maps
  #44/#45).

Two structural facts that shape the gap analysis:

- **Scope is the foreground window, not the whole screen / all monitors.** Hints are drawn for the
  active window (plus the taskbar on its own hotkey). Several competitors label *everything on
  screen across monitors*.
- **Detection is pure UIA.** When an app exposes nothing useful to UI Automation (canvas apps,
  some Electron/Chromium surfaces, games, custom-drawn UI), there is no hint to type. Competitors
  that add OCR or image-recognition fallbacks keep working where a pure-UIA tool goes blank.

---

## Feature matrix — Windows & cross-platform tools

Legend: **✓** = yes (primary-source confirmed) · **▲** = partial / with caveat · **✗** = no /
not a feature · **~** = in prototype/R&D, not shipped · **[?]** = unverified.

| Feature | **Windows-Hinting** | **Fluent Search** | **win-vind** | **Vimium / Vimium C** | **Talon (+community)** | **MS Voice Access** | **warpd** | **Vimium Everywhere (AHK)** |
|---|---|---|---|---|---|---|---|---|
| Hint-label clicking | ✓ (foreground window) | ✓ Screen Search, whole screen ([src][fs-screen]) | ✓ EasyClick ([src][wv]) | ✓ but **browser-only** ([src][vim]) | ▲ via community add-ons; not core | ✗ (uses numbers/grid instead) | ✓ hint mode ([src][warpd]) | ✓ system-wide ([src][vime]) |
| Mouse grid / bisection fallback | ✗ | ▲ [?] (Screen Search is hint-based) | ✓ GridMove ([src][wv]) | ✗ | ✓ recursive 3×3 mouse grid ([src][talonwiki]) | ✓ "Show grid" + drill-down ([src][va-cmd]) | ✓ grid mode ([src][warpd]) | ✗ |
| **Scroll-region actuation** | ~ prototyping (map #54) | ▲ [?] | ▲ Vim scroll keys | ✓ page scroll (browser) ([src][vim]) | ✓ scroll + gaze/OCR scroll ([src][gazeocr]) | ✓ scroll/continuous scroll ([src][va-cmd]) | ✗ (pointer tool) | ✗ |
| **Text-search selection** (type visible label) | ✗ | ✓ In-Window Search via UIA ([src][fs-inwin]) | ✗ | ▲ find-in-page only | ▲ via gaze-OCR words ([src][gazeocr]) | ▲ "Click <name>" ([src][va-cmd]) | ✗ | ✗ |
| Continuous / auto hint refresh | ~ prototyping (maps #44/#45) | [?] | [?] | ✓ (re-run f) | n/a | n/a | ✗ | [?] |
| Multi-monitor / full-screen scope | ▲ foreground window + taskbar | ✓ whole screen ([src][fs-screen]) | ✓ OS-wide | ✗ (per-tab) | ✓ grid per display ([src][va-multi]) | ✓ everywhere / per-display ([src][va-multi]) | ▲ X11 xinerama | ✓ any window |
| Right / double / modifier clicks | ✓ (Shift+key modes) | ✓ (number-key prefix) ([src][fs-screen]) | ✓ (Vim/click cmds) | ▲ new-tab open | ✓ | ✓ | ✓ (m/,/.) ([src][warpd]) | ▲ |
| Works in any app vs browser-only | ✓ any app (UIA) | ✓ any app (UIA+vision) | ✓ any app | ✗ browser-only | ✓ any app | ✓ any app | ✓ any app | ✓ any app |
| Driven by keyboard / voice / both | keyboard (Talon-friendly) | keyboard | keyboard | keyboard | **voice** (+eye/noise) | **voice** | keyboard | keyboard |
| Detection: a11y-API vs OCR/vision | **UIA only** | **UIA + image-recognition + optional OCR** ([src][fs-screen]) | UIA ([src][wv]) | DOM | a11y + **OCR** (gaze-ocr) ([src][gazeocr]) | UIA + numbers/grid | none (grid/pointer) | UIA (AHK) |
| Drag support | ▲ mouse-move mode (no 2-point drag) | [?] | ✓ (Vim) | ✗ | ✓ `drag` ([src][talonwiki]) | ✓ mark→drag ([src][va-cmd]) | ✓ drag key ([src][warpd]) | ✗ |
| "Click & keep going" / chaining | ▲ re-trigger hotkey | [?] | ✓ modal (stays in mode) | ✓ (stays in hint mode option) | ✓ modal | ✓ modal | ✓ modal | ▲ |
| Configurability | ✓ prefs, hotkeys, keys | ✓ extensive | ✓ Vim config files ([src][wv]) | ✓ | ✓ Python scripts | ▲ fixed commands | ✓ config file | ▲ |
| First-party OS integration | ✗ (3rd-party) | ✗ | ✗ | ✗ | ✗ | ✓ **built into Windows 11** ([src][va-cmd]) | ✗ | ✗ |

*Note on NVDA:* NVDA (the screen reader) offers **object navigation** and a **"route mouse to
navigator object"** command that can reach and click elements the keyboard can't tab to
([NVDA User Guide][nvda]) — it's the accessibility-framing baseline, not a hint tool, so it is
discussed in text rather than scored as a column.

## Feature matrix — macOS tools

| Feature | **Homerow** | **Vimac** (OSS, superseded by Homerow) | **Shortcat** | **macOS Voice Control** (first-party) |
|---|---|---|---|---|
| Hint-label clicking | ✓ ⌘⇧Space ([src][hr]) | ✓ hint mode ([src][vimac]) | ✗ (search paradigm) | ✗ (numbers/grid instead) |
| Mouse grid / bisection | ✗ | ✗ (grid mode was planned) | ✗ | ✓ "Show grid" + drill-down ([src][vc]) |
| **Scroll-region actuation** | ✓ Scroll mode ⇧⌘J, HJKL, switch areas ([src][hr]) | ✓ scroll mode, HJKL ([src][vimac]) | ✗ | ✓ scroll up/down ([src][vc]) |
| **Text-search selection** | ✓ Search mode ⌘⇧/ ([src][hr]) | ✗ | ✓ fuzzy search of labels ([src][sc]) | ▲ "Click <name>" via Show names ([src][vc]) |
| Right / double / modifier clicks | ✓ (Return/Shift-Return/⌘-Return) ([src][hr]) | ✓ click/dbl/right ([src][vimac]) | ✓ click/right/double/modifier ([src][sc]) | ✓ |
| Detection: a11y-API vs OCR/vision | macOS Accessibility API | macOS Accessibility API | macOS Accessibility API ([src][sc]) | macOS Accessibility API |
| Driven by | keyboard | keyboard | keyboard | **voice** |
| Multi-monitor | [?] (FAQ implies yes) | [?] | [?] | ✓ |
| First-party OS integration | ✗ | ✗ | ✗ | ✓ built into macOS |

---

## Compelling features missing from Windows-Hinting (ranked)

Ranked by likely value to the Talon/voice + accessibility audience. Each is sourced to a real
competitor that ships it, with a one-line difficulty read given Windows-Hinting's pure-UIA base and
a note on whether the repo is already exploring it.

### 1. Scroll-region actuation (scroll a chosen region by keyboard/voice)
- **Who has it:** Homerow ("Scroll mode", ⇧⌘J, HJKL, `Tab`/number to switch scroll areas)
  ([Homerow README][hr]); Vimac (scroll mode, red border marks the active area) ([Vimac][vimac]);
  Voice Access (scroll + "start scrolling") ([command list][va-cmd]); macOS Voice Control
  ([commands][vc]).
- **Why it matters:** it's the single most common "I can hint the button but I still need the mouse
  to *read the list*" gap; it's table-stakes on macOS.
- **Difficulty (UIA):** *Medium, and empirically the crux.* The repo's own `ScrollExposureProbe`
  exists precisely because ScrollPattern exposure is inconsistent and often a **false negative**;
  some surfaces only actuate via a ScrollBar's RangeValue and some (canvas/virtual/terminal) expose
  nothing — so a robust implementation needs the ScrollPattern → RangeValue → (last resort) synthetic
  wheel fallback ladder the probe is mapping.
- **Repo status:** **~ actively prototyping** — wayfinder map #54, `prototype/ScrollExposureProbe`.

### 2. Text / label search selection (type the visible text instead of a hint code)
- **Who has it:** Shortcat ("Type what you want to click. 'ok' for an OK button", fuzzy + synonym
  search) ([Shortcat][sc]); Homerow Search mode (⌘⇧/) ([Homerow][hr]); **Fluent Search In-Window
  Search on Windows** — type text, it finds tabs/buttons/links/menu items via UIA and Enter invokes
  ([Fluent Search][fs-inwin]).
- **Why it matters:** for elements with a stable accessible Name, "type what you see" is faster and
  more memorable than a hint code, and it's the paradigm Windows currently only gets from a
  third-party launcher. It reuses the exact UIA Name data Windows-Hinting already reads.
- **Difficulty (UIA):** *Low–Medium.* The element enumeration + Name reading is already in place;
  this is mostly a matching UI (fuzzy filter) over the same candidate set, plus disambiguation when
  several elements share a name.
- **Repo status:** not on the bench.

### 3. Mouse grid / bisection fallback for un-hintable spots
- **Who has it:** Talon (recursive 3×3 mouse grid) ([Talon wiki][talonwiki]); warpd (grid mode)
  ([warpd][warpd]); win-vind (GridMove) ([win-vind][wv]); Voice Access ("Show grid" → say a number
  → drill down) ([command list][va-cmd]); macOS Voice Control ("Show grid", repeat number to
  drill down) ([commands][vc]).
- **Why it matters:** it's the universal escape hatch — when *no* accessible element exists (canvas,
  game, custom UI, a precise pixel), a grid still lets you reach any point. Every serious voice/
  keyboard mouse tool ships one; Windows-Hinting has no fallback at all when UIA is empty.
- **Difficulty (UIA):** *Low.* It's pure overlay geometry + synthetic cursor move/click — no UIA
  dependency, which is exactly why it complements the UIA weakness.
- **Repo status:** not on the bench.

### 4. OCR / image-recognition fallback when the UIA tree is empty
- **Who has it:** **Fluent Search Screen Search** runs a hybrid — *image recognition* (works with any
  app) **plus** UIA, with optional OCR, and an Auto mode that picks the engine ([Fluent Search
  Screen Search][fs-screen]); Talon's `talon-gaze-ocr` clicks any on-screen text via OS OCR
  ([gaze-ocr][gazeocr]).
- **Why it matters:** this is the direct answer to Windows-Hinting's core architectural risk — a
  pure-UIA tool simply shows no hints on canvas/Chromium/game surfaces. A competitor on the *same OS*
  already solved it with vision.
- **Difficulty (UIA):** *High.* Adds a whole second detection engine (screenshot capture + OCR/vision
  + coordinate clicking) and a merge/dedupe strategy with the UIA candidates. Biggest lift on this
  list, but the highest ceiling.
- **Repo status:** not on the bench. (Note: the mouse-grid fallback in #3 is the cheap partial
  substitute for this.)

### 5. Full-screen / multi-monitor hinting (not just the foreground window)
- **Who has it:** Fluent Search Screen Search labels *the whole screen* ([Fluent Search][fs-screen]);
  Voice Access grids/numbers work "everywhere" and per-display ([multi-display][va-multi]); win-vind
  is OS-wide ([win-vind][wv]).
- **Why it matters:** users frequently want to click something in a *background* window, a second
  monitor, or a floating palette without focusing it first.
- **Difficulty (UIA):** *Medium.* Enumerating all top-level windows across monitors and merging their
  UIA subtrees (with correct per-monitor DPI transforms) is more work than one foreground window, but
  the probe code already inspects arbitrary windows via `ElementFromHandle`/`EnumWindows`.
- **Repo status:** not directly on the bench (the scroll probe *inspects* arbitrary windows but the
  product hints only the foreground window).

### 6. Drag-and-drop between two hints
- **Who has it:** macOS Voice Control ("Drag <n> to <m>") ([commands][vc]); Voice Access (mark →
  drag) ([command list][va-cmd]); warpd (drag key) ([warpd][warpd]); Talon (`drag`) ([Talon
  wiki][talonwiki]).
- **Why it matters:** reordering lists, moving files, resizing — currently impossible mouseless in
  Windows-Hinting (its "mouse-move" mode only positions the cursor; there's no press-hold-release
  across two picked points).
- **Difficulty (UIA):** *Low–Medium.* Pick hint A → pick hint B → synthesize
  button-down at A, move, button-up at B (bounding rectangles are already known). Some targets also
  expose UIA drag/drop patterns, but synthetic mouse drag is the general path.
- **Repo status:** not on the bench.

### 7. Hints that follow / refresh as the UI changes (and in-place re-hinting/chaining)
- **Who has it:** conceptually every modal tool that stays active (warpd/win-vind/Voice Access remain
  in their mode); Vimium re-hints on demand. Windows-Hinting's own `AutoHintDetectionProbe` is
  chasing exactly the "detect redraw → refresh hints" signal.
- **Why it matters:** open a menu / expand a tree / load new content and the hints go stale; the user
  must re-trigger the hotkey. Auto-refresh (or auto re-hint after activating a menu) is a big
  smoothness win, especially for voice where re-triggering is costly.
- **Difficulty (UIA):** *Medium.* Needs UIA StructureChanged/LayoutInvalidated event handling with
  debounce; the probe's #49 finding (virtualized lists under-report StructureChanged) shows the
  subtlety.
- **Repo status:** **~ prototyping** — `prototype/AutoHintDetectionProbe` (maps #44/#45/#49).

### 8. High-DPI / mixed-DPI correctness across monitors
- **Who addresses it:** first-party tools (Voice Access, Voice Control) handle multi-DPI natively;
  Voice Access documents per-display grids ([multi-display][va-multi]).
- **Why it matters:** hint rectangles must land on the right pixel on a mixed-DPI multi-monitor setup;
  it's a correctness prerequisite for feature #5, not a headline feature.
- **Difficulty (UIA):** *Medium.* Per-monitor-v2 DPI awareness + correct coordinate transforms.
- **Repo status:** not explicitly tracked (single-foreground-window scope defers the hardest cases).

---

## Notes, caveats, and what I could not verify

- **Fluent Search is the most important competitor to study hands-on.** Its Screen Search is a
  near-superset of Windows-Hinting's hint mode (whole-screen labels, click-type via number prefix,
  and — decisively — a UIA + image-recognition + OCR hybrid), and its In-Window Search is the
  Windows analog to Shortcat. All confirmed from its official docs
  ([Screen Search][fs-screen], [In-Window search][fs-inwin]); repo: `adirh3/Fluent-Search`.
- **win-vind is archived (per its GitHub, archived ~March 2026) [unverified date]** but remains the
  clearest example of EasyClick (UIA hints) + GridMove (grid) unified under whole-OS Vim modality —
  a different philosophy (modal OS control) vs. Windows-Hinting's focused hint overlay
  ([win-vind][wv]).
- **Homerow/Vimac multi-monitor and drag:** their READMEs/sites don't spell these out; Homerow's FAQ
  merely *asks* "Does it work with multiple monitors?" without exposing the answer in fetched content
  — **[unverified]**.
- **Fluent Search scroll actuation and auto-refresh:** not confirmed from primary docs — marked
  **[?]** in the matrix.
- **Talon "hint-label clicking" is not a core built-in.** Talon core ships the *mouse grid* and
  eye/zoom mouse; letter-hint clicking comes from community/add-on packages and OCR
  (`talon-gaze-ocr`), so it's scored **▲**. Talon's headline strengths are voice + eye tracking +
  noise ([talonvoice.com][talon]).
- **Vimium/Vimium C are browser-only** — included as the canonical hint-UX benchmark, not as a
  same-scope competitor ([Vimium][vim], [Vimium C][vimc]).
- **AutoHotkey option exists:** `vimium-everywhere` brings UIA-based system-wide hints via AHK but is
  self-described as experimental/unstable ([vimium-everywhere][vime]).
- I did **not** install or run any competitor; all claims are from documentation/source. Anything I
  couldn't pin to a primary source is flagged inline.

---

## Sources

- Windows-Hinting README: `C:\Users\knausj\git\Windows-Hinting\README.md`; scroll probe:
  `prototype\ScrollExposureProbe\Program.cs`; auto-hint probe: `prototype\AutoHintDetectionProbe\Program.cs`
- Fluent Search — Screen Search (hint labels; UIA + image recognition + OCR): https://fluentsearch.net/docs/Screen%20search/General
- Fluent Search — In-window search (text-search-to-click via UIA): https://fluentsearch.net/docs/Search%20apps/In-window%20search
- Fluent Search repo: https://github.com/adirh3/Fluent-Search
- win-vind (EasyClick, GridMove, UIA): https://github.com/pit-ray/win-vind
- Vimium: https://github.com/philc/vimium
- Vimium C: https://github.com/gdh1995/vimium-c
- Talon Voice (voice, eye tracking, noise; cross-platform): https://talonvoice.com/
- Talon community — mouse grid & mouse commands (Basic Usage): https://talon.wiki/Basic%20Usage/basic_usage/
- talon-gaze-ocr (click on-screen text via OCR): https://github.com/wolfmanstout/talon-gaze-ocr — and https://handsfreecoding.org/2022/11/27/gaze-ocr-talon-support-and-10-new-features/
- Microsoft Voice Access — command list (Show numbers, Show grid, drag, scroll): https://support.microsoft.com/en-us/accessibility/windows/voice-access/voice-access-command-list
- Microsoft Voice Access — multi-display (grid per display): https://support.microsoft.com/en-us/topic/use-voice-access-on-a-multi-display-setup-6aaa5483-1a38-40a9-8a40-9c6aa97974ce
- warpd (hint / grid / normal modes, drag, cross-platform): https://github.com/rvaiya/warpd
- keynav (grid bisection, X11): https://github.com/jordansissel/keynav
- vimium-everywhere (AHK, system-wide UIA hints): https://github.com/phil294/vimium-everywhere
- NVDA User Guide (object navigation, route mouse to navigator object): https://download.nvaccess.org/releases/2026.1/documentation/userGuide.html
- Homerow (click / scroll / search modes, shortcuts): https://homerow.app and https://github.com/nchudleigh/homerow/blob/main/README.md
- Vimac (hint mode + scroll mode; superseded by Homerow): https://github.com/dexterleng/vimac
- Shortcat (fuzzy text search of UI labels; click/right/double/modifier): https://www.shortcat.app
- Apple macOS Voice Control commands (Show numbers / Show grid / drill-down / drag / scroll): https://support.apple.com/guide/mac-help/use-voice-control-commands-mh40719/mac

[fs-screen]: https://fluentsearch.net/docs/Screen%20search/General
[fs-inwin]: https://fluentsearch.net/docs/Search%20apps/In-window%20search
[wv]: https://github.com/pit-ray/win-vind
[vim]: https://github.com/philc/vimium
[vimc]: https://github.com/gdh1995/vimium-c
[talon]: https://talonvoice.com/
[talonwiki]: https://talon.wiki/Basic%20Usage/basic_usage/
[gazeocr]: https://github.com/wolfmanstout/talon-gaze-ocr
[va-cmd]: https://support.microsoft.com/en-us/accessibility/windows/voice-access/voice-access-command-list
[va-multi]: https://support.microsoft.com/en-us/topic/use-voice-access-on-a-multi-display-setup-6aaa5483-1a38-40a9-8a40-9c6aa97974ce
[warpd]: https://github.com/rvaiya/warpd
[vime]: https://github.com/phil294/vimium-everywhere
[nvda]: https://download.nvaccess.org/releases/2026.1/documentation/userGuide.html
[hr]: https://github.com/nchudleigh/homerow/blob/main/README.md
[vimac]: https://github.com/dexterleng/vimac
[sc]: https://www.shortcat.app
[vc]: https://support.apple.com/guide/mac-help/use-voice-control-commands-mh40719/mac
