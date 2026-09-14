# ScrollSelectProbe — FINDINGS (wayfinder #61, map #54)

**Question (#61):** does a *select-a-scroll-region* capability — detect scrollable regions,
overlay numbered hints, pick one, and actuate it — work across both well-behaved and
questionable apps? This throwaway spike answers the #60 go/no-go **empirically**.

**Verdict: GO.** Select→actuate works across the matrix; the questionable apps (no UIA
scroll signal) are carried by the injected-wheel fallback. Operator confirmed live.

## What it does

Global hotkey (**Ctrl+Alt+S**) enters "scroll mode" over the focused window: detect
scrollable regions (reusing the #59 census heuristic), draw a numbered, per-region-colored
rect on each (topmost click-through overlay), and drive scroll from the keyboard.
**Default target = the active window** (injected wheel to the focused control — works even
with zero UIA signal). Type a digit to switch the target to a specific UIA region.
Keys: `0`=window · `1-9`=region · `H/J`=up/down · `K/L`=left/right · `PgUp/PgDn`=page · `Esc`=exit.
`--target <hwnd>` enters directly (no hotkey). Build with VS MSBuild; run the exe.

**Actuation paths:** ScrollPattern (`Scroll`/`SetScrollPercent`) for content regions;
`ScrollBar`→`RangeValuePattern.SetValue` where only a scrollbar is exposed; injected wheel
(`SendMessage WM_MOUSEWHEEL/HWHEEL` at the region/window) as the universal fallback.

## Eval (operator-driven, live, 2026-09-14)

| App | Detection | Actuation | Verdict |
|---|---|---|---|
| **Visual Studio** | editor V + H scrollbars (RangeValue) | `RangeValue.SetValue`, both axes | ✅ live-confirmed (incl. horizontal) |
| **GitHub Desktop** | commits + changes lists (ScrollPattern) | `ScrollPattern.Scroll` | ✅ |
| **File Explorer** | nav **Tree** + file **List** (ScrollPattern) | `ScrollPattern.Scroll` | ✅ (dead sibling scrollbar suppressed) |
| **Settings** | content **Pane** (ScrollPattern) | `ScrollPattern.Scroll` | ✅ |
| **Gmail / ChatGPT** | message/conversation group (ScrollPattern) | `ScrollPattern.Scroll` | ✅ |
| **VS Code (Monaco)** | 0 regions (no UIA signal) | **default-window injected wheel** | ✅ editor scrolls via fallback |
| **Slack (PWA)** | 0 regions (Blink virtual list) | **default-window injected wheel** | ✅ message list scrolls via fallback |

**The load-bearing result:** well-behaved apps get precise UIA region-select (both
mechanisms, both axes); the questionable apps that expose *nothing* are fully carried by
the default-window injected wheel. Nothing scrolls worse than today → additive, no
regression → **GO**.

## Detection refinements discovered during the eval

- **Container-type gate beats a raw viewSize gate.** An early `viewSize < 60` filter (to
  reject barely-overflowing junk) wrongly dropped legit but lightly-overflowing panes —
  e.g. VS **Solution Explorer** / Explorer's nav **Tree** (viewSize ~68–96%). Fix: accept a
  `ScrollPattern` region when it's a **content-container control type** (List/Tree/DataGrid/
  Table/Document/Pane/Group/Custom/Window) that reports scrollable, regardless of overflow
  %, and reject non-containers (Button/row/Edit) by type. Still gate on on-screen + ≥60px.
- **Suppress redundant/detached sibling scrollbars.** When a content `ScrollPattern` region
  exists, its sibling `ScrollBar` is dropped (it overlaps the pane) — many are *detached*
  (SetValue moves the value but not the content, e.g. Explorer nav scrollbar, the bar beside
  the VS terminal). Scrollbars with **no** content region beside them are **kept** (VS
  editor case — the scrollbar is the only handle).
- **Warm-up retry.** Electron/Chromium builds its a11y tree lazily after a UIA client
  attaches (#59); a cold first query returns 0 regions, so detection retries a few times.
- **Z-order.** The overlay must be shown *off* the WM_HOTKEY handler (deferred to a timer)
  and forced topmost via `SetWindowPos(NOACTIVATE)`, or it lands behind the active window.

## Deferred to a follow-up "overlay/detection UX" pass (NOT go/no-go blockers)

- **Color-coding / overlay legibility** — distinct per-region colors read poorly when rects
  are thin/overlapping; needs a dedicated design pass (badge placement, contrast, which
  panes to hint).
- **Un-hinted panes** — a pane that exposes *only* a detached scrollbar (no ScrollPattern)
  can't be driven by UIA or the window-fallback; a **wheel-at-cursor** fallback would cover it.
- **Minor region dedup** — the same element occasionally appears twice (child-descent /
  breadcrumb overlap).
- **Scroll-mode re-entry** — a second Ctrl+Alt+S in one process was flaky before the z-order
  fix; verify it holds, else tear down/rebuild the hook+overlay more defensively.
- Intermittent `COMException 0x80040201` on Chromium `ScrollPattern.Scroll` — retry-able.

Throwaway prototype — not part of `Windows-Hinting.sln`, never merges.
