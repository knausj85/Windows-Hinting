# ScrollExposureProbe — FINDINGS (wayfinder #59, map #54)

**Question (map #54):** how reliably do real apps expose scrollability to UI
Automation? Working hypothesis: *ScrollPattern exposure is inconsistent and often
false-negative.* This is the census that answers it, plus the objective ground-truth
of whether a UIA-driven scroll actually moves the content.

**Method.** A headless probe (`Program.cs`) that, per window, `FindAll(Subtree)`s every
element bearing a scroll signal (ScrollPattern / ScrollItemPattern / RangeValuePattern /
ScrollBar control type), dumps every signal, and **attempts a real UIA scroll** on
**both axes** (`Scroll`/`SetScrollPercent`, async settle-read) — recording whether the
percent actually changed, then restoring. It also tests the **alternative actuation
path** (`RangeValuePattern.SetValue` on a `ScrollBar`) and **descends into child-HWND UIA
fragments**. Driven over already-open windows by handle/process — no focus needed.

Modes: `--hwnd` / `--proc` / `--uiaproc` (UIA desktop-root walk, for CoreWindows like
Start) census; `--holdall` actuates *every* scroller (both mechanisms, both axes) in
sequence for visible confirmation; `--holdrange` / `--reach` are targeted diagnostics.

Raw evidence logs: `census-2026-09-13.log` (round 1, 6), `-round2.log` (+5),
`-terminals.log`, `-edge-breadth.log`, `-ide-slack.log`, `-childfrag.log`.

## The census (Win11, 2026-09-13, ~17 real surfaces)

| Surface | Framework | Scroll path | Verdict |
|---|---|---|---|
| File Explorer file list | Win32 / DirectUI | ScrollPattern | ✅ |
| Start menu — All apps | XAML CoreWindow | ScrollPattern | ✅ |
| Settings | WinUI / XAML | ScrollPattern | ✅ |
| Notepad (with content) | WinUI / XAML | ScrollPattern | ✅ |
| **Visual Studio — editor** | WPF | **ScrollBar → RangeValue** (both V & H) | ✅ (confirmed visually) |
| **Visual Studio — Solution Explorer** | WPF | ScrollPattern | ✅ |
| Command Prompt (conhost) | classic console | ScrollPattern | ✅ |
| **Windows Terminal** (standalone) | XAML terminal | **ScrollBar → RangeValue** | ✅ (confirmed visually) |
| Edge — plain page (MDN, Wikipedia) | Chromium | ScrollPattern (document root) | ✅ |
| Gmail (tab **and** PWA) | Chromium SPA | ScrollPattern (inner CSS-overflow) | ✅ |
| ChatGPT | Chromium SPA | ScrollPattern (conversation group) | ✅ |
| GitHub Desktop | Electron | ScrollPattern (commits + changes lists) | ✅ |
| **VS Code — Monaco editor** | Electron | — none — | ❌ |
| **Slack — PWA *and* native** | Chromium SPA / Electron | — none — | ❌ |
| **VS integrated terminal** (`HwndTerminal`) | Win32 hosted | — none — | ❌ |

**Bottom line: the large majority of surfaces have a usable, actuable scroll path via one
of two mechanisms. The failures are a single coherent class (below).**

## What the census establishes

1. **Hypothesis CONFIRMED, but the failure set is narrow and predictable.** A usable path
   (ScrollPattern *or* ScrollBar+RangeValue) exists across native Win32/WinUI/XAML/WPF,
   both console hosts *except the embedded one*, ordinary web pages, and many Electron/web
   *list* panes. The **only failures are custom-rendered text surfaces that expose no
   scroll signal at all**: **VS Code's Monaco editor, Slack's message list (PWA and
   native alike), and the embedded `HwndTerminal`** (VS's Developer PowerShell).

2. **Two actuation mechanisms, not one.**
   - **ScrollPattern** (`Scroll`/`SetScrollPercent`) — the primary path; native shell,
     WPF panes (VS Solution Explorer), web content regions.
   - **ScrollBar + `RangeValuePattern.SetValue`** — a first-class fallback where no
     ScrollPattern container exists. **Visually confirmed** driving both **standalone
     Windows Terminal** (buffer scrolled) and the **Visual Studio editor** (code scrolled,
     vertical *and* horizontal). The naive `*Scrollable` flag alone is nearly useless.

3. **Same widget class, opposite outcomes — the *engine/host* decides, not the category.**
   - "Code editor": **Visual Studio's WPF editor is actuable** (RangeValue, both axes);
     **VS Code's Monaco is not** (Blink virtual scroller, no path).
   - "Terminal": **standalone Windows Terminal is actuable** (sibling XAML ScrollBar
     RangeValue); the **embedded `HwndTerminal` is not** (exposes nothing); **classic
     conhost** exposes ScrollPattern directly (contradicting research #57).
   - "Chat/SPA": **Gmail and ChatGPT expose their scrollers**; **Slack does not** — same
     Chromium engine, different app-level virtualization.
   - "Virtualized list": **XAML virtualized lists (Start AppsList, Explorer) DO expose
     ScrollPattern**; only **Blink/canvas** virtualizers (Monaco, Slack) don't.

4. **No UIA fragmentation problem (a hypothesis we tested and rejected).** Hosted child
   controls *could* root a separate UIA fragment unreachable from the top window; the
   probe descends into child HWNDs to cover that. But in every app tested — VS (incl. the
   `HwndTerminal`), Slack, Gmail — the content is **stitched into one fragment, reachable
   top-down** (proven with `--reach`: the VS terminal is 2 parent-steps from the devenv
   root). So the failures are genuine *no-signal* cases, not "we looked in the wrong
   sub-tree." Child-descent is retained as defensive coverage (some WebView2/island
   configs do fragment) but was **not** the missing piece here.

5. **Exposure is non-deterministic / warm-up-dependent.** Across identical back-to-back
   captures the ★ count moved (VS Code 1→0, Gmail 1→2, GitHub Desktop 0→1→2 as the window
   was resized / accessibility warmed up). Electron/Chromium builds its a11y tree lazily
   *after* a UIA client attaches. **A real capability must re-scan + settle, not query
   once.**

6. **The usable signal is a needle in a haystack.** Gmail: 1 usable region among 12
   ScrollPattern candidates + ~1,700 weak `ScrollItem`-only nodes. Selection needs the
   multi-factor heuristic below, not the raw flag.

## The selection heuristic that works

A real, targetable **content region** is the conjunction of:

- exposes `ScrollPattern` with a non-`-1` percent on some axis (**vertical or
  horizontal** — horizontal-only scrollers like wide editors are real, e.g. VS's
  `WpfEditorUIHorizontalScrollbar`, range 0..282);
- **on-screen** (rect intersects the window);
- **substantial size** ≥ 60×60 px (rejects recurring 2×2 micro-scrollers);
- **`ViewSize < ~60%`** on the moved axis (rejects ~98% buttons, ~80% virtual rows);
- **proportional actuation** (a small increment moves it a small amount, not a `0→100`
  slam).

Validated against a deliberately multi-scrollbar surface (ChatGPT, 7 candidates): it kept
the 2 real regions (main + `Conversation`), rejected the document root, the 2×2 `Text`
micro-scrollers, and the barely-overflowing horizontal groups.

**Fallback (no ScrollPattern container):** a `ScrollBar` control-type element whose
`RangeValuePattern` has `max>min` → actuate with `RangeValue.SetValue`. This covers
standalone Windows Terminal and the Visual Studio editor. Caveat: a *detached* scrollbar
can exist (VS draws a WPF `scrollbar` beside the `HwndTerminal` whose RangeValue changes
but scrolls nothing) — so the target scrollbar must be the one actually bound to the
visible content.

## Implication for the go/no-go (#60)

- **UIA scroll is viable for the large majority of surfaces** — native shell, WPF IDEs,
  ordinary web pages, list panes, most SPAs, both standalone terminals — via **two
  actuation paths** (ScrollPattern + ScrollBar/RangeValue), the multi-factor selection
  heuristic, and re-scan/settle.
- **It structurally misses one class: custom-rendered text surfaces that expose no scroll
  signal** — VS Code's Monaco editor, Slack's message list (PWA + native), and the
  embedded `HwndTerminal`. These have neither a ScrollPattern content region nor a
  scrollbar with RangeValue, and it is *not* a fragmentation/tooling gap.
- Whether that gap is acceptable is the #60 decision. If not, a non-UIA fallback (injected
  wheel at a rect) is a *separate effort* (ruled out of scope for map #54).

## Reproduce

```powershell
& $msbuild -restore prototype\ScrollExposureProbe\ScrollExposureProbe.csproj
$exe = '.\prototype\ScrollExposureProbe\bin\Debug\net10.0-windows\ScrollExposureProbe.exe'
& $exe --hwnd 0x1234            # census one window (auto-descends into child fragments)
& $exe --proc devenv           # census a process's windows (title-agnostic, >=200px)
& $exe --uiaproc StartMenuExperienceHost   # UIA desktop-root walk (CoreWindows: Start etc.)
& $exe --holdall 0x1234 4000   # actuate EVERY scroller (both mechanisms/axes) in sequence
& $exe --reach 0xTOP 0xCHILD   # diagnostic: is a child element in the same UIA fragment?
```

Throwaway prototype — not part of `Windows-Hinting.sln`, never merges. Branch
`prototype/scroll-exposure-probe`.
