# Phase 02 — HintEngine in Core

**Depends on:** 01
**Decisions:** [#31](https://github.com/knausj85/Windows-Hinting/issues/31),
[#38](https://github.com/knausj85/Windows-Hinting/issues/38) (derived-conditions refinement)
**Reference implementation:** the prototype on
[`prototype/hint-engine-intent-effect`](https://github.com/knausj85/Windows-Hinting/tree/prototype/hint-engine-intent-effect)
(`prototypes/HintEngine.Prototype/`) — port its shape, don't merge it.

## Goal

Build the production `HintEngine` in `WindowsHinting.Core` with comprehensive unit tests. **The app
is untouched in this phase** — the engine ships dark, verified purely by its tests, so the
cutover (phase 03) rewires against an already-proven core.

## Work

1. **Types** (closed sets of records in Core):
   - `Intent`: covers hotkey/tray toggles for each source (with host-stamped timestamp), key
     events from the hook, `ScanCompleted`/`ScanFailed` (carrying scan token), foreground-changed,
     auto-hide elapsed, `OptionsApplied`, deactivate, and an activation intent carrying an optional
     `ClickAction` (the intent set must express the retired pipe protocol — see
     [#33](https://github.com/knausj85/Windows-Hinting/issues/33)).
   - `Effect` — exactly five one-shots: `BeginScan`, `ActivateElement`, `Notify`, `PlayErrorBeep`,
     `SuppressKey`.
   - `State` as an immutable value: mode (`Inactive`/`Scanning`/`Active`), active source, scan
     token, hints (`{Bounds, Label, ElementRef}` with `ElementRef` opaque), typed prefix, options.
   - `DesiredConditions(state)`: overlay visibility + hints + filter (the display model), input
     capture, foreground watch, auto-hide timer, tray status, tray click action.
2. **Transition core:** pure `Transition(State, Intent) → (State, IReadOnlyList<Effect>)` inside a
   thin stateful `HintEngine.Submit` wrapper. Behavior must replicate today's `HintController` /
   `HintState` / `HintInputHandler` semantics exactly (source-toggle rule: toggle the shown
   source → off; toggle the other → switch; debounce via intent timestamps; prefix filtering;
   error beep on dead-end prefix — read the current code as the behavioral spec).
3. **Guards baked in:** scan-token staleness (stale `ScanCompleted`/`ScanFailed` ignored);
   foreground watch starts at scan completion; no clock reads inside the engine.
4. **Tests** in `WindowsHinting.Core.Tests`: build-engine → submit intents → assert effects and
   `DesiredConditions`, no mocks. Cover at minimum: the five hard flows the prototype exercised
   (toggle during scan, source switch mid-scan, stale scan completion, prefix dead-end, foreground
   change during each mode), auto-hide, activation with explicit `ClickAction`, and
   `SuppressKey` correctness for every key event in every mode.

## Definition of done

- `dotnet test` green on ubuntu CI; engine + tests live entirely in Core/Core.Tests.
- The host project has **zero** references to the new engine (verified by inspection) — the app
  binary behaves identically because nothing consumes the engine yet.
- Smoke test: section **1** only (sanity that the app still behaves — it must, being untouched).

## Out of scope

- Host wiring, adapters, reconciler (phase 03).
- Scan pipeline internals — `BeginScan` is an opaque request at this point (phase 04).
