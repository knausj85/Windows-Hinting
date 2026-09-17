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

## Live-run results (FILL IN after driving Settings)

- [ ] Does chaining work at all on Settings? (click nav item → hints re-appear on new pane → repeat)
- [ ] Does `Esc` exit cleanly?
- [ ] Does the fixed-delay flash/thrash? What `N` felt right (too short = mid-animation empty/partial scan; too long = laggy)?
- [ ] Overlay/focus: did the re-show ever steal focus or collapse the target?
- [ ] Stale hints: any flash of the pre-click state before the new scan painted?
- [ ] Idempotency: any misbehavior from a fast second commit (stale-tick guard holding)?
- [ ] Re-scan target: was it always the same foreground HWND for an in-app click? (expected yes for Settings)

## Constraints handed to B / C

- (fill in: recommended settle N, whether fixed-delay is viable as a floor, what event-driven signal B should reach for, any focus/overlay gotchas)
