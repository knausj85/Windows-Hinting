# Phase 03 — Host cutover to the intent funnel

**Depends on:** 02
**Decisions:** [#31](https://github.com/knausj85/Windows-Hinting/issues/31),
[#38](https://github.com/knausj85/Windows-Hinting/issues/38)

## Goal

Replace `HintController` and its collaborators with the intent funnel: adapters post intents, the
engine decides, the host executes effects and reconciles `DesiredConditions`. This is the largest
phase and the point where the legacy control flow dies. Behavior must be preserved except the two
accepted deltas ratified in #38 (foreground watch starts at scan completion, ~100 ms; `Selecting`
mode gone — unobservable, since shipped code never entered it).

## Work

1. **The funnel.** A small host component that serializes every intent onto the UI thread, stamps
   toggle intents with a timestamp, calls `HintEngine.Submit`, executes the returned one-shot
   effects, and reconciles `DesiredConditions` deltas after each submit.
2. **Inbound adapters** (each maps events → intents; no decision logic):
   - Hotkey (`HotkeyWindow` / `KeyboardHookService`) → toggle/key intents. The hook adapter reads
     `SuppressKey` synchronously from the returned batch — never `BeginInvoke` from hook context.
     Key-repeat suppression stays in the hook adapter as input hygiene.
   - Tray (`TrayIconManager`) → toggle intents; tray status/click-action are now *outputs*
     reconciled from `DesiredConditions`.
   - Foreground watch (`ForegroundWindowHookService`) → foreground-changed intents; the watcher is
     started/stopped by reconciliation (a desired condition), not by ad-hoc calls.
   - Timers (auto-hide) → elapsed intents; the timer itself is a reconciled condition.
   - Scan completion → `ScanCompleted`/`ScanFailed` intents carrying the scan token.
3. **Outbound adapters** (execute effects):
   - `BeginScan` → kicks the existing scan path (`UIAutomationService` etc.) off-thread; completion
     posts an intent. `ScanCompleted` carries hint items whose `ElementRef` is an opaque handle; a
     ref-table in `WindowsHinting.Interop.Uia` maps refs to live COM elements and owns their
     lifetime/release.
   - `ActivateElement` → posts to the message loop, resolves the `ElementRef`, runs the activator
     chain with the intent's `ClickAction`.
   - `Notify` / `PlayErrorBeep` → tray notification / sound.
   - Overlay: `OverlayManager`/`OverlayForm` become a renderer of the display-model slice of
     `DesiredConditions` (show/hide/hints/filter). Move `CurrentOpacity`/`TargetOpacity` out of the
     shared element model into the overlay code.
4. **Delete the legacy control flow:** `HintController`, `HintState`, `HintInputHandler` (their
   semantics now live in the engine), plus any event spaghetti they held together. Update DI in
   `ServiceCollectionExtensions.cs` / `Program.cs`.
5. **Preferences dialog, updater, startup registration stay in the composition root** — they do not
   route through the engine (until phase 07 introduces `OptionsApplied` for preferences).

## Definition of done

- Full CI green (Core tests + Debug build).
- **Full smoke test** (`smoke-test.md` sections 1–6) passes, on two monitors.
- `HintController.cs`, `HintState.cs`, `HintInputHandler.cs` no longer exist.
- Grep-check: no `BeginInvoke`/`Invoke` calls inside keyboard-hook callback paths.
- Known accepted deltas only (foreground-watch start timing); anything else that differs is a bug.

## Out of scope

- Scan pipeline restructure (phase 04) — the existing scan internals are called as-is behind
  `BeginScan`.
- Overlay composer/presenter split (phase 06) — the overlay may keep its current painting.
- Logging changes (phase 09).
