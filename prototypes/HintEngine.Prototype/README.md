# HintEngine prototype — THROWAWAY

**Question under test** ([#38](https://github.com/knausj85/Windows-Hinting/issues/38)): does the
intent/effect model from [#31](https://github.com/knausj85/Windows-Hinting/issues/31) —
`HintEngine.Submit(Intent) → Effects`, pure and synchronous — hold up ergonomically on the
trickiest real flows of today's `HintController`?

Verify by reacting to it: (a) transitions stay readable, (b) no intent handler blocks or awaits,
(c) the `BeginInvoke` deadlock-avoidance flows are expressible as post-and-return, (d) the effect
set feels like an interface, not a command bus.

## Run

```
dotnet run --project prototypes\HintEngine.Prototype                    # interactive TUI
dotnet run --project prototypes\HintEngine.Prototype -- --demo          # scripted trace
dotnet run --project prototypes\HintEngine.Prototype -- --demo --derived
```

## Two host styles under comparison

`[m]` toggles the host between them live (also `--derived` for the demo):

- **EFFECTS mode** (the model as decided in #31): the host executes every effect in the
  returned batch. Explicit, but deactivation re-sends the same 7 idempotent commands each time.
- **DERIVED mode** (alternative under evaluation): the host executes only the *one-shot* effects
  (`BeginScan`, `ActivateElement`, `Notify`, `PlayErrorBeep`, `SuppressKey`) and reconciles the
  *continuous* conditions — overlay, hook, fg-watch, timer, tray — against the pure
  `HintEngine.DesiredConditions(state)`. The log then shows only actual deltas
  (`~ hook: off -> on`), and "nothing changed" transitions reconcile to nothing.

Writing `DesiredConditions` caught a real drift bug in the hand-written effects path: the
source-switch batch forgot `SetTrayClickAction(Default)` (see the comment in `HintEngine.OnToggle`).
That's the trade-off in miniature: derived can't forget a condition; effects can't hide *when*
something happened.

`HintEngine.cs` is the portable part (pure — no I/O, no threading, no clock). `Program.cs` is a
throwaway TUI playing the host: fake clock, fake adapters, and manual control over everything
asynchronous — *you* decide when a scan completes, a timer fires, or the foreground window
changes, so you can interleave them with toggles.

## The five flows from the ticket

1. **Toggle while taskbar hints are active (source switch)** — `t` `c` (taskbar hints show), then
   `h`: overlay hides, a new foreground scan starts, input capture stays on. `c` completes it.
   *Staleness:* press `h` then `t` *before* completing — two scans in flight; `c` completes the
   older one and the engine ignores it by token; `c` again lands the live one.
2. **Hotkey pass-through** — with hints active, press `h`: the log shows `KeyPressed(Ctrl+Alt+H)`
   produced **no** `SuppressKey`, then the registered-hotkey message arrives as `ToggleHints`.
3. **Scan timeout** — `h` then `x`: full teardown plus a `Notify` toast.
4. **Selection commit → deferred activation** — `h` `c`, type `j` `k` (filter narrows), `Enter`:
   teardown effects come first, `ActivateElement` is *posted* (pending count goes up), and `g`
   pumps the message loop — activation runs after the hook callback returned.
5. **Foreground change → auto-hide** — `h` `c` then `f`: deactivates. Contrast `t` `c` then `f`:
   taskbar hints stay (watch is off for taskbar source).

Also try: `h` twice quickly (debounce eats the second), `w` after hints are gone (stale auto-hide
timer fire is ignored), `Shift+R` while active (pending click action → tray), a wrong letter like
`m` (beep, consumed), `Esc` with and without a filter.
