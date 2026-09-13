# ScrollExposureProbe — FINDINGS (wayfinder #59, map #54)

**Question (map #54):** how reliably do real apps expose scrollability to UI
Automation? Working hypothesis: *ScrollPattern exposure is inconsistent and often
false-negative.* This is the census that answers it, plus the objective ground-truth
of whether a UIA-driven scroll actually moves the content.

**Method.** Built a headless probe (`Program.cs`) that, per window, `FindAll(Subtree)`s
every element bearing a scroll signal (ScrollPattern / ScrollItemPattern /
RangeValuePattern / ScrollBar control type), dumps every signal, then **attempts a real
UIA scroll** (`Scroll(SmallIncrement)`, `SetScrollPercent` fallback, async settle-read),
records whether `VerticalScrollPercent` actually changed, and **restores** it. Driven
over already-open windows by handle/process — no focus/hotkey needed (`ElementFromHandle`
inspects any window; `--uiaproc` walks the UIA desktop root to reach CoreWindow surfaces
like the Start menu). Raw evidence:
[`census-2026-09-13.log`](census-2026-09-13.log) (round 1, 6 targets) and
[`census-2026-09-13-round2.log`](census-2026-09-13-round2.log) (round 2, +5 targets).

## The census (Win11, 2026-09-13, 11 real surfaces)

| App / surface | Framework | ★ real actuable scroller | Primary content region exposed + actuable? |
|---|---|---|---|
| File Explorer file list | Win32 / DirectUI | ✅ 1–2 | ✅ `List "Items View"` |
| Start menu — All apps | XAML CoreWindow | ✅ 1 | ✅ `List "AppsList"` (viewSize 16%) |
| Settings | WinUI / XAML | ✅ 1 | ✅ content `Pane` |
| Notepad (with content) | WinUI / XAML | ✅ 1 | ✅ `Document "Text editor"` |
| Command Prompt | classic conhost | ✅ 1 | ✅ `Window "Console Window"` |
| Edge — plain web page | Chromium | ✅ 1 | ✅ `Document`/RootWebArea itself scrolls |
| Gmail | Chromium web (SPA) | ✅ 1 | ✅ inner `Group ":3"` message list |
| GitHub Desktop | Electron | ✅ 2 | ✅ `List "Commits"` **and** changed-files `DataGrid` |
| **VSCode** | Electron | ❌ 0 | ❌ Monaco editor not exposed (only sidebar rows/dead nodes) |
| **Slack PWA** | Chromium web (SPA) | ❌ 0 | ❌ message list is a JS virtual scroller — no ScrollPattern |
| **Windows Terminal** | XAML terminal | ❌ 0 | ❌ text area is `Document`/`Custom`, TextPattern only |
| _Notepad (empty)_ | WinUI / XAML | (0) | _n/a — nothing to scroll; ✅ once it has content_ |
| _Slack native_ | Electron | — | _N/A — not installed on this machine_ |

**Bottom line: 8 of 11 surfaces expose ≥1 usable, actuable scroll region; 3 fail.**

## What the census establishes

1. **Hypothesis CONFIRMED but the failure set is narrow and specific.** ScrollPattern
   reliably exposes *and* actuates the primary content region across native Win32/WinUI/
   XAML (Explorer, Start, Settings, Notepad), the classic console, ordinary web pages,
   and many Electron/web *list* panes (Gmail mail list, GitHub Desktop commits+changes).
   It fails on exactly three: **VSCode's Monaco editor, Slack's message list, and the
   Windows Terminal text buffer** — all bespoke, custom-virtualized text/chat/editor
   surfaces.

2. **Virtualization ≠ no-exposure — it depends on the engine.** XAML virtualized lists
   (Start's `AppsList`, Explorer's file list) expose a real `ScrollViewer`/ScrollPattern
   that actuates. Blink/canvas virtual scrollers (Monaco, Slack) expose none. So "the
   list is virtualized" does NOT predict failure; "it's a bespoke web/canvas virtualizer"
   does.

3. **Classic conhost exposes ScrollPattern; Windows Terminal does not.** The classic
   Command Prompt console window (`ConsoleWindowClass`) exposes `ScrollPattern` on its
   Console Window and actuates it — **contradicting research #57's claim that conhost
   implements no ScrollPattern.** Windows Terminal (XAML) genuinely exposes nothing on
   the text area (only a stock ScrollBar with degenerate RangeValue). If a target's
   console is hosted in Windows Terminal (now the Win11 default), scroll is a
   false-negative; in classic conhost it is not.

4. **The document root is the scroller only for plain pages.** On a plain web page the
   `Document`/RootWebArea itself is scrollable+actuable. In SPAs (Gmail, Slack) the root
   reports NoScroll (`vPct=-1`) and scroll lives on inner CSS-overflow elements — exposed
   in Gmail, virtual/absent in Slack. (Matches research #57/#58.)

5. **Exposure is non-deterministic / warm-up-dependent.** Across identical back-to-back
   captures the ★ count moved (VSCode 1→0, Gmail 1→2, GitHub Desktop 0→1→2 as the window
   was resized and accessibility warmed up). Electron/Chromium builds its a11y tree lazily
   *after* a UIA client attaches. **A real capability must re-scan + settle, not query
   once.**

6. **The usable signal is a needle in a haystack.** Gmail surfaced 1 usable region among
   12 ScrollPattern candidates + ~1,700 weak `ScrollItem`-only nodes. The raw
   `*Scrollable` flag is nearly useless as a target selector (Gmail claims 11 scrollable;
   ~1 is real).

## The selection heuristic that works

Neither the `*Scrollable` flag nor "a UIA-scroll moved it" is sufficient alone — Chromium
exposes swarms of tiny/virtual/offscreen scrollers that trivially "move" `0→100`. A real,
targetable content region is the conjunction of:

- exposes `ScrollPattern` with `VerticalScrollPercent != -1` (not the NoScroll sentinel);
- **on-screen** (rect intersects the window rect);
- **substantial size** ≥ 60×60 px (rejects the recurring 2×2 micro-scrollers);
- **`ViewSize < ~60%`** — content genuinely overflows (rejects ~98% buttons, ~80% rows);
- **proportional actuation** — a small increment moves it a *small* amount, not a
  `0→100` slam.

Every true region cleared this (viewSize 0.5 / 8.1 / 10.8 / 12.5 / 16.1 / 35.7 / 48.8%);
every artifact — buttons, tree-rows, 2×2 groups, offscreen nodes — was rejected.

## Implication for the go/no-go (#60)

- **UIA scroll is viable for a large, well-defined subset** — native shell UI, ordinary
  lists, plain web pages, and many Electron list panes — but only via the multi-factor
  heuristic above plus re-scan/settle, NOT the naive `*Scrollable` flag.
- **It structurally misses one class:** bespoke virtualized text surfaces — **code
  editors (Monaco), chat virtual-lists (Slack), and terminal buffers hosted in Windows
  Terminal.** Whether that gap is acceptable is the #60 decision. If not, a non-UIA
  fallback (injected wheel at a rect) is a *separate effort* (ruled out of scope for #54).
- Net: the earlier "≈50/50" read was pessimistic — with the full 11-surface census it's
  **~3/4 exposed**, and the misses are a coherent, predictable category rather than random.

## Reproduce

Build with VS MSBuild (COMReference needs full framework), then drive it:

```powershell
& $msbuild -restore prototype\ScrollExposureProbe\ScrollExposureProbe.csproj
$exe = '.\prototype\ScrollExposureProbe\bin\Debug\net10.0-windows\ScrollExposureProbe.exe'
& $exe --hwnd 0x1234            # census one window
& $exe --proc explorer          # census a process's HWND windows (title-agnostic, >=200px)
& $exe --uiaproc StartMenuExperienceHost   # census via UIA desktop-root walk (CoreWindows: Start etc.)
& $exe                          # interactive: Ctrl+Alt+S / capfg / capproc / capuia / caphwnd
```

For the transient Start menu: open it (+ click "All apps"), then `--uiaproc
StartMenuExperienceHost` — run from a non-foreground shell so the probe doesn't steal
focus and close it.

Throwaway prototype — not part of `Windows-Hinting.sln`, never merges. Branch
`prototype/scroll-exposure-probe`.
