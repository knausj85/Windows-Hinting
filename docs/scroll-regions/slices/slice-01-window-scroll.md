# Slice 01 — Scroll mode with the window target

**Depends on:** — (merge `feature/hint-dismissal-parity` first, or bring `HintDismissalWatcher`
in with this slice)
**Design:** [design.md](../design.md) §1, §2, §4.3–4.4, §5, §6, §7, §8
**Absorbed later by:** modernize phases 02/03 (mode, intents, `DesiredConditions`), 06 (overlay)

## Goal

`Ctrl+Alt+S` puts the foreground window into scroll mode and `H J K L` scroll it, with taps and
momentum holds, a hidden pointer, intelligent dismissal, and the `[Scroll]` title for Talon. No
region detection yet: window `0` is the only target.

## Work

1. **Test project.** If slice 03 hasn't added it yet, add `Windows-Hinting.Tests` (xUnit v3,
   `net10.0-windows`, `InternalsVisibleTo`) for the pure helpers. Modernize phase 01 later moves
   these tests to `WindowsHinting.Core.Tests`.
2. **Mode.** `HintMode.Scrolling` in `HintStateManager`, with the target window captured at entry.
   A `ScrollController` owns enter/exit and the target, alongside `HintController`. Entering
   supersedes any click mode. The click workflow is untouched.
3. **Hotkey.** `ScrollHotkey` in `HintOverlayOptions` (default `Ctrl+Alt+S`), registered through
   `HotkeyWindow`, shown on the Preferences Hotkeys page. Pressing it while scrolling exits.
4. **Input.** A scroll branch in `HintInputHandler` (or a sibling handler the hook routes to while
   scrolling): the keymap, the 130 ms self-timed hold (fresh press → tick; still down at 130 ms →
   hold start; key-up → hold end), OS auto-repeat ignored, full key capture, `H J K L` key-ups
   swallowed. The hook only maps keys and returns. Keep the hold timing in a pure, unit-tested
   class (input: key events with timestamps; output: tick / hold-start / hold-end).
5. **Actuator (window target only).** `IScrollActuator`/`ScrollActuator`, DI-registered:
   - tap = one aimed wheel notch at the window centre;
   - hold = the velocity loop on its own ticker thread (constants in design §4.3);
   - aiming = park the hidden cursor at the window centre, 40 ms debounce, stay parked, restore on
     exit;
   - a worker thread for taps, one in flight, latest wins.
6. **Cursor hide.** `SetSystemCursor` over the full `OCR_*` set with a transparent cursor on entry;
   `SPI_SETCURSORS` on every exit path, `ProcessExit` and unhandled exceptions; the crash marker
   checked at startup (design §6.3).
7. **Overlay.** `OverlayForm`/`OverlayManager`: a scroll state that draws the `0` badge and the
   inset active outline around the target window, one accent colour. Title
   `Windows Hinting Overlay [Scroll]` on every per-screen overlay while scrolling; back to the base
   title on exit.
8. **Dismissal.** Start `HintDismissalWatcher` on entry with the target window: real click (passes
   through), foreground change, target move/resize → exit. Stop it on exit.

## Definition of done

- Builds clean with VS 18 MSBuild; the new unit tests pass.
- Manual, on bare metal: Notepad, Edge, File Explorer, and VS Code each scroll by tap (one notch)
  and by hold (glides, stops after release) with the user's mouse resting elsewhere on screen.
- `Esc`, `Ctrl+Alt+S`, a click (which still lands), Alt+Tab, and moving the window each exit the
  mode; the pointer reappears where it was.
- Kill the process from Task Manager while scrolling, restart: the pointer is restored.
- The overlay window title reads `Windows Hinting Overlay [Scroll]` while scrolling (check with
  Talon's `ui.active_window()` or Spy++) and never contains `[Active]`.
- Click hinting: hotkey → labels → type → activation still works, including from inside a scroll
  session (the click hotkey supersedes scroll mode).

## Out of scope

- Region detection and digit switching (slice 02).
- Window rules (slices 03–04).
- Home/End, PageUp/PageDown (#70).
