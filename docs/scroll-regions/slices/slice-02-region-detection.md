# Slice 02 — Generic region detection and switching

**Depends on:** 01 (merge `fix/taskbar-hint-reading-order` first for `ReadingOrder.Sort`, or bring
it in with this slice)
**Design:** [design.md](../design.md) §3.1–3.3, §4.1–4.4, §6
**Absorbed later by:** modernize phase 04 (`ScrollRegionSource`, stages as `IScanStage`s)

## Goal

Scroll mode finds the scrollable regions of the target window with the generic UIA query, numbers
them `1`–`9` in reading order, and lets digits switch between them. Region taps use UIA first; holds
and fallbacks use the aimed wheel.

## Work

1. **Harvest.** `ScrollRegionScanner` beside `UIAutomationService`: root from today's root
   strategy, the `ScrollPattern OR ScrollBar` query on the root and on child-HWND fragments, raw
   facts stamped, cap 24, the empty-result warm-up retry (3 × 300 ms). Scan on entry; entry with
   zero regions still works.
2. **Pure stages** as static helpers over plain data, unit-tested: Gate, Suppress, Dedup, Number
   (design §3.2). Parameters come from a `ScrollDetectionPolicy` object holding the defaults
   (slice 04 fills it from window rules). Include table tests built from the prototype's real
   cases: Monaco `Edit` v=9 accepted; a 23 px GitHub Desktop row with h-overflow rejected; a VS Code
   tab strip accepted; a Solution Explorer tree at v=96 accepted; a button at v=98 rejected;
   Terminal's `0..0` scrollbar accepted; a bar beside a pane suppressed; a lone bar kept.
3. **Mode.** Regions + active index in the scroll state; `0`–`9` switch; a number with no region is
   ignored; switching clears any hold.
4. **Actuator, regions.** The tap ladder (design §4.2) with `MovedSince`, the rung memo, the
   boundary guard, `axisPolicy` (default `RespectUia`), bar aiming beside the bar, skip-park when
   the cursor is already inside a pane, and `TargetLost` → active back to `0`. Holds on a region
   park at the region's aim point.
5. **Overlay.** Badges for every region (top-left inner corner, inset, clamped), the inset outline
   on the active one, collision nudge + leader tick in the renderer.

## Definition of done

- Unit tests for every stage pass.
- Manual, bare metal: each of these shows the expected numbered regions, and taps + holds scroll
  the selected one with the mouse resting elsewhere: File Explorer (list), Settings (UWP pane),
  VS Solution Explorer, Beyond Compare (detached scrollbar), Windows Terminal, VS Code editor,
  GitHub Desktop (history rows are **not** regions).
- At the top of a list, pressing up does nothing (no parent pane scrolls).
- A region that disappears mid-session falls back to `0` without an error.
- Run the same matrix in the VM and note which rows follow the physical mouse (expected for
  wheel-based targets). Post the results on G
  [#69](https://github.com/knausj85/Windows-Hinting/issues/69).

## Out of scope

- Rule-driven regions (Slack, Teams, VS Code sidebar, Gmail folder list) and `TrustWheel`
  built-ins: slice 04.
- Touch-pan injection (G #69).
