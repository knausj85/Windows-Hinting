# ScrollExposureProbe — FINDINGS (wayfinder #59, map #54)

**Question (map #54):** how reliably do real apps expose scrollability to UI
Automation? Working hypothesis: *ScrollPattern exposure is inconsistent and often
false-negative.* This is the census that answers it, plus the objective ground-truth
of whether a UIA-driven scroll actually moves the content.

**Method.** Built a headless probe (`Program.cs`) that, per window, `FindAll(Subtree)`s
every element bearing a scroll signal (ScrollPattern / ScrollItemPattern /
RangeValuePattern / ScrollBar control type), dumps every signal, and then **attempts a
real UIA scroll** (`Scroll(SmallIncrement)`, `SetScrollPercent` fallback), recording
whether `VerticalScrollPercent` actually changed, and **restores** it. Driven
automatically over already-open windows by handle/process (`--hwnd` / `--proc`) — no
focus or hotkey needed, because `ElementFromHandle` inspects any window. Raw evidence:
[`census-2026-09-13.log`](census-2026-09-13.log).

## The census (Win11, 2026-09-13)

| App | Framework | ScrollPattern (claim scrollable) | ★ real content scrollers | Primary region exposed + actuable? |
|---|---|---|---|---|
| File Explorer | Win32 / DirectUI | 2–3 (1–2) | **1–2** | ✅ `List "Items View"` (file list) |
| GitHub Desktop | Electron / Chromium | 8–14 (7–13) | **2** | ✅ `List "Commits"` **and** changed-files `DataGrid` |
| Gmail | Chromium web (Edge) | 12 (11) | **1** | ✅ `Group ":3"` 815×823 (message list) |
| VSCode | Electron / Chromium | 32 (31) | **0** | ❌ Monaco editor not exposed; only sidebar tree-rows + dead nodes |
| Slack PWA | Chromium web (Edge) | 6 (5) | **0** | ❌ message list is a JS virtual scroller — no ScrollPattern |
| Windows Terminal | XAML | 1 (0) | **0** | ❌ text area is a `Document`/`Custom` with no scroll signal |
| Slack native | Electron | — | — | N/A — not installed on this machine |

**"★ real content scrollers"** = elements passing the selection heuristic below. The raw
`*Scrollable` flag count is in parentheses to show how misleading it is (Gmail claims
11 scrollable; exactly 1 is a usable region).

## What the census establishes

1. **Hypothesis CONFIRMED, with nuance.** ScrollPattern reliably exposes *and* actuates
   the primary content region in **3 of 6** targets (File Explorer, GitHub Desktop,
   Gmail) and **fails entirely** in **3 of 6** (VSCode editor, Slack PWA message list,
   Windows Terminal). It is genuinely inconsistent — even two Chromium *web* apps
   disagree (Gmail exposes its list; Slack does not), and two Electron apps disagree
   (GitHub Desktop exposes both lists; VSCode's editor exposes nothing).

2. **The document root is never the scroller.** On every Chromium/Electron surface the
   top `Document`/`RootWebArea` reports `vScrollable=False`, `vPct=-1` (NoScroll
   sentinel). Scroll lives on inner Blink CSS-overflow elements — when it's exposed at
   all. (Matches research #57/#58: Chromium implements IScrollProvider only for real
   CSS-overflow, not on the document.)

3. **Virtual scrollers are the false-negative.** VSCode's Monaco editor, Slack's message
   list, and the Windows Terminal buffer are all custom/virtualized scrollers that
   expose **no** usable ScrollPattern container. Terminal is the starkest: the scrollable
   text area carries only `TextPattern`; the only ScrollPattern element is a
   non-scrollable tab strip, and the vertical `ScrollBar` is a degenerate stock XAML one
   (`RangeValue` 0/0/0).

4. **Exposure is non-deterministic / warm-up-dependent.** Across identical back-to-back
   captures the ★ count moved (VSCode 1→0, Gmail 1→2, File Explorer 1→2, GitHub Desktop
   0→1→2 as the window was resized and accessibility warmed up). Electron/Chromium builds
   its a11y tree lazily *after* a UIA client attaches; a single snapshot under-reports.
   **A real capability must re-scan + settle, not query once.**

5. **The usable signal is a needle in a haystack.** Gmail surfaced 1 usable region among
   12 ScrollPattern candidates + ~1,700 weak `ScrollItem`-only nodes. `ScrollItemPattern`
   is near-useless as a scroll signal (XAML/Blink put it on almost every element).

## The selection heuristic that works

Neither the `*Scrollable` flag nor "a UIA-scroll moved it" is sufficient alone — Chromium
exposes swarms of tiny/virtual/offscreen scrollers that trivially "move" `0→100`. A real,
targetable content region is the conjunction of:

- **exposes `ScrollPattern`** with `VerticalScrollPercent != -1` (not the NoScroll sentinel);
- **on-screen** — element rect intersects the window rect;
- **substantial size** — ≥ 60×60 px (rejects the recurring 2×2 micro-scrollers);
- **`ViewSize < ~60%`** — content genuinely overflows the viewport (rejects
  barely-overflowing buttons at ~98% and virtual list-rows at ~80%);
- **proportional actuation** — a small increment moves it a *small* amount, not a
  `0→100` slam (rejects micro/virtual scrollers whose entire range is one increment).

Every true region cleared this (viewSize 0.5 / 8.1 / 10.8 / 12.5 / 35.7 / 48.8%); every
artifact — toolbar buttons, tree-rows, 2×2 groups, offscreen measurement nodes — was
rejected by it.

## Implication for the go/no-go (#60)

- **UIA scroll is viable for a meaningful subset**, but NOT via the naive `*Scrollable`
  flag — only through the multi-factor heuristic above, plus re-scan/settle for the
  lazy a11y tree.
- **A pure-UIA capability will miss ~half of real targets** as tested here — notably
  code editors (Monaco), Slack, and terminals. Whether that subset is enough is the #60
  decision; if not, the non-UIA fallback (injected wheel at a rect) is a *separate
  effort* (ruled out of scope for map #54).
- Practically: the reliable wins are **native Win32 lists and *some* Electron/web list
  panes** (file lists, commit lists, mail lists). The reliable losses are **text/code
  editors, chat message lists, and terminals** — exactly the virtualized surfaces.

## Reproduce

Build with VS MSBuild (COMReference needs full framework), then drive it:

```powershell
& $msbuild -restore prototype\ScrollExposureProbe\ScrollExposureProbe.csproj
$exe = '.\prototype\ScrollExposureProbe\bin\Debug\net10.0-windows\ScrollExposureProbe.exe'
& $exe --proc explorer          # census by process (no focus needed)
& $exe --hwnd 0x1234            # census one window
& $exe                          # interactive: Ctrl+Alt+S / capproc / caphwnd / capfg
```

Throwaway prototype — not part of `Windows-Hinting.sln`, never merges. Branch
`prototype/scroll-exposure-probe`.
