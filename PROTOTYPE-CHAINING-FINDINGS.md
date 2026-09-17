# PROTOTYPE #73 — chained hinting (throwaway)

**Branch:** `prototype/chained-hinting` (do NOT merge — throwaway spike per Wayfinder map #72).
**Question:** Can chaining work *mechanically* on legacy `main`? After an activation, re-scan the foreground window and re-show hints, looping until an exit — proven end-to-end on Settings.

## What was changed

All changes in `Windows-Hinting/HintController.cs`, tagged `PROTOTYPE #73`:

- **`OnSelectionCommitted`** — after activating the element, if the round was a foreground-window round, arm a fixed-delay timer (`ChainReScanDelayMs = 400`) instead of just staying deactivated.
- **`OnChainReScanTick`** (new) — fixed-delay settle: marshals to the UI thread, then `Activate(ForegroundWindow)` + `ScanForHints()` (reusing the existing hotkey scan path).
- **`GetForegroundPid`** (new) — `PInvoke.GetWindowThreadProcessId` for the "foreground left the app" exit.
- Fields: `ChainingEnabled` (const on), `ChainReScanDelayMs`, `_chainTimer`, `_chainGeneration` (stale-tick guard), `_chainOriginPid`.

**Loop:** `activate → wait 400ms → re-scan foreground → re-show → (repeat)`.

**Exit conditions wired:**
- `Esc` — the commit's `Deactivate()` leaves us Inactive and only a *commit* arms the re-scan, so a plain Esc ends the loop. (Known gap: Esc *during* the 400 ms window can't be seen — the keyboard hook is stopped in that gap.)
- **Foreground leaves the app** — origin-PID captured at commit, compared on the tick.
- **Zero scannable elements** — handled inside `ScanForHints()` (it deactivates).

**Design choices worth noting:**
- Reuses the exact hotkey re-toggle path (`Activate` + `ScanForHints`) rather than a bespoke re-scan → maximum reuse, minimum new surface.
- Stale-tick guard = a generation counter bumped on every commit; the tick captures its generation and no-ops if superseded.
- Chaining is gated to `HintSource.ForegroundWindow` (taskbar hints don't chain).

## Live-run results (Settings, 2026-09-17)

- **Chaining works mechanically — YES.** Click a nav item → hints re-appear on the new pane → repeat. The loop (activate → re-scan → re-show → repeat) and the re-scan target (same foreground HWND for in-app clicks) behave as designed.
- **NOT robust: some elements are not hinted** on a chained re-scan. Signature of the **fixed delay racing the pane's settle** — 400 ms sometimes fires before the new pane has finished materializing its UIA children, so the scan catches a *partial* tree. This is the predicted failure of a blind time-based settle.
- No focus-theft / target-collapse / wrong-window re-scan reported. Stale-tick guard held (no thrash from fast commits).

## Verdict

**Mechanical GO.** Chaining is viable on legacy `main` via the re-toggle path (`Activate` + `ScanForHints`). The fixed-delay settle is a working stand-in but **not robust** — the settle **must be event-driven**, which is exactly Prototype B's scope.

## Constraints handed to B / C

- **Fixed-delay is a backstop, not the mechanism.** Keep it only as a hard-timeout fallback (~800 ms) behind the event-driven gate.
- **B's settle signal = UIA `StructureChanged` readiness gate** (per #44's #48): subscribe on the foreground root after activation, re-scan when `ChildAdded`/bulk-add fires, debounced to a short quiet window; fall back to the timeout if silent.
- Harness to build on: `OnChainReScanTick` + `_chainGeneration` guard + `GetForegroundPid` exit are the reusable pieces; B replaces the `_chainTimer.Change(...)` arming with an event subscription.
