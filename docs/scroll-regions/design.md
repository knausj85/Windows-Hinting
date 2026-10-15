# Scroll capability: design

How select-a-scroll-region works, in the modernize vocabulary, with a map onto today's `main` seams
at the end. Window rules (which this design reads through the **effective policy**) are specified
in [`window-rules.md`](window-rules.md).

## 1. What the user sees

1. Press **`Ctrl+Alt+S`**. Scroll mode starts on the foreground window (the **target window**).
   Any click hints on screen go away; scroll mode replaces click mode.
2. The target window is **target `0`** and is live at once. Each scrollable region the detector
   finds gets a numbered badge `1`–`9` in its top-left corner, numbered in reading order.
3. **`H J K L`** scroll the active target left, down, up and right (vim layout). A **tap**
   scrolls one step; **holding** the key flings with momentum and glides to a stop on release.
4. A **digit** switches the active target. The active target gets a subtle outline; badges stay up
   for the whole mode so you can switch again.
5. The pointer is hidden for the whole mode and put back where it was on exit.
6. The mode ends on **`Esc`**, `Ctrl+Alt+S` again, a real mouse click (which still reaches the
   app), the foreground window changing, or the target window moving or resizing.

Talon drives the same flow: it sees the overlay title `Windows Hinting Overlay [Scroll]` and sends
the same keys.

## 2. Engine fit (modernize)

Decided in [A #63](https://github.com/knausj85/Windows-Hinting/issues/63); scroll adds to the
engine and reshapes nothing.

### Flow

```
Inactive ──EnterScrollMode──▶ Scanning(source = ScrollRegions) ──ScanCompleted──▶ Scrolling
Scrolling: SwitchScrollTarget(n) · ScrollTick · ScrollHoldStart/End · ScrollTargetLost  (stay)
Scrolling ──ExitScrollMode──▶ Inactive
```

Scroll reuses the scan machinery but skips the click `Active` mode: target `0` is live on entry, so
there is no "wait for one pick" step. Picking a region is a target switch inside `Scrolling`.
`Scrolling` is orthogonal to the click source toggle (ForegroundWindow / Taskbar): its own hotkey,
not a third toggle source. Entering it from a click mode supersedes that mode.

`ScanFailed` or a scan that finds nothing still enters `Scrolling`, with target `0` as the only
target.

### Mode state

`Scrolling { TargetWindow: WindowHandle, Regions: ScrollRegion[≤9], Active: 0..9, ScanToken }`.
`TargetWindow` is the HWND captured at entry. The actuator never re-reads the foreground window,
because the mode ends when the foreground changes.

### Intents (new)

| Intent | Carries | Source |
|---|---|---|
| `EnterScrollMode` | host timestamp, target window | hotkey adapter |
| `ExitScrollMode` | reason (`Key`, `Toggle`, `Click`, `Foreground`, `TargetMoved`) | input adapter, dismissal watcher |
| `SwitchScrollTarget` | `n ∈ 0..9` | input adapter |
| `ScrollTick` | axis, direction, amount | input adapter (one per fresh press) |
| `ScrollHoldStart` | axis, direction | input adapter (key still down after the hold threshold) |
| `ScrollHoldEnd` | axis | input adapter (key up after a hold started) |
| `ScrollTargetLost` | scan token | effect executor (actuator outcome) |

`ScanCompleted`/`ScanFailed` are reused and carry the regions and scan token. A `SwitchScrollTarget`
to a number with no region is ignored (no beep). `ScrollTargetLost` sets `Active = 0`
([C #65](https://github.com/knausj85/Windows-Hinting/issues/65)).

### Commands, not mechanisms

The engine speaks only **scroll commands**: `axis ∈ {Vertical, Horizontal}` × `direction ∈ {−1, +1}`
× `amount ∈ {Line, Page, ToStart, ToEnd}`. How a command becomes motion (UIA increment, wheel, …) is
an **actuation mechanism** that only `IScrollActuator` knows. v1 binds only `Line`; `Page`,
`ToStart` and `ToEnd` stay in the vocabulary but nothing emits them and the actuator need not
implement them until [#70](https://github.com/knausj85/Windows-Hinting/issues/70).

### Effects and conditions

- **New one-shot effect `ScrollElement(target, command)`** (the sixth). Emitted for each
  `ScrollTick` while `Scrolling`. `target` is a `ScrollTargetSpec` (below).
- **Holds are a derived condition, not effects.** `DesiredConditions` gains
  `ScrollDrive: { Target, Vertical: −1|0|+1, Horizontal: −1|0|+1 }`. `ScrollHoldStart` sets the
  axis, `ScrollHoldEnd` clears it, and `SwitchScrollTarget`/exit clear both. The host reconciles it
  by calling the actuator's `SetDrive`. The velocity, friction and ticker live entirely in the
  actuator, so nothing continuous enters the engine (the invariant G
  [#69](https://github.com/knausj85/Windows-Hinting/issues/69) asked to keep).
- `DesiredConditions` also gains the scroll overlay model (§6), the `Scrolling` input-capture
  profile (§5), the cursor-hidden flag (§6.3), and the dismissal watch (§7).

`ScrollTargetSpec = { Window: WindowHandle, Region?: { ElementRef?, Bounds, Caps } }` — a region
without an `ElementRef` is a wheel-only region found by a rule (§3.4). Window `0` is synthesized by
the engine; the source never yields it.

### Ports

- **`ScrollRegionSource`** (a `HintSource`, in Interop.Uia): §3.
- **`IScrollActuator`** (in Interop.Uia, beside `IElementActivator`/`ElementActivatorChain`): §4.
  `Scroll(target, command, policy) → ScrollOutcome ∈ {Scrolled, NoOp, TargetLost}` and
  `SetDrive(target, vertical, horizontal, policy)`. The effect executor maps `TargetLost` to the
  `ScrollTargetLost` intent.

## 3. Detection: `ScrollRegionSource`

Decided in [B #64](https://github.com/knausj85/Windows-Hinting/issues/64), with the gate changes
proven in G [#69](https://github.com/knausj85/Windows-Hinting/issues/69) and the rule hooks from
E [#67](https://github.com/knausj85/Windows-Hinting/issues/67) / I
[#89](https://github.com/knausj85/Windows-Hinting/issues/89). A COM **harvest** collects raw facts;
pure stages decide. Tunables are `scrollDetection` fields of the effective policy (§3.5).

### 3.1 Harvest (COM, Interop.Uia)

1. **Root.** The `root` section's strategy picks the scan root, exactly as for click hints (so
   Explorer scans only its active tab — inactive tabs produce phantom regions).
2. **Generic query.** `FindAll(Subtree, IsScrollPatternAvailable OR ControlType == ScrollBar)` on
   the root, then on each visible child-HWND fragment (`EnumChildWindows`, ≥100×100 px, a separate
   UIA fragment from the root, not nested in one already taken) — the hosted-island case from the
   census.
3. **Rule query.** Each `expectedRegions` selector (§3.4) becomes part of the request, so elements
   the generic query never returns are fetched.
4. Stamp raw facts on every candidate: bounds, control type, class name, vertically/horizontally
   scrollable, vertical/horizontal view size, has `ScrollPattern`, and which query found it.
   Cap the harvest at 24 candidates.
5. **Warm-up retry.** Chromium/Electron builds its accessibility tree only after a UIA client
   attaches, so a cold query can return nothing. If the harvest is empty, retry
   `warmUp.retries` times with `warmUp.delayMs` between (defaults 3 × 300 ms). Apps that answer at
   once pay nothing.

### 3.2 Pure stages (Core), in order

| Stage | Rule |
|---|---|
| **Gate** | *Pattern candidate:* on-screen, long side ≥ `minSizePx` (60), and overflows on an axis (`scrollable && viewSize < viewSizeThreshold` (99.5)). Horizontal overflow counts only if the element is ≥ 60 px tall or is a `Tab` (a one-row element reporting horizontal overflow is truncated text). Accept if the control type is in `containerTypes`, **or** it overflows strongly on the vertical axis (`viewSize < 50`; catches the Monaco editor, an `Edit`). *ScrollBar candidate:* on-screen with long side ≥ 40 px; orientation = taller-than-wide is vertical. **Don't** use the RangeValue range (Windows Terminal reports `0..0` with real scrollback). *Rule candidate:* accepted if on-screen and ≥ 60×60 after clipping to the window; skips the gate. |
| **Suppress** | Drop a ScrollBar that touches (8 px margin) any pane region: it is that pane's own bar. Keep a lone bar (the VS editor case: the bar is the only handle). Skipped when `suppressDetachedScrollbars` is false. |
| **Dedup** | Collapse candidates that are the same element (`CompareElements`) or whose bounds match within a few pixels. A rule candidate that also exposes a `ScrollPattern` merges into the pattern candidate (keeps the pattern). |
| **Number** | Sort survivors into **reading order** (rows by top, then left; reuse `ReadingOrder.Sort`), keep the first **9**, number them `1`–`9`. |

Default `containerTypes` = List, Tree, DataGrid, Table, Document, Pane, Group, Custom, Window, Tab.

### 3.3 What crosses into Core

A `ScrollRegion` is `{ Number, Bounds, ElementRef?, Caps }`, with
`ScrollCaps { Vertical, Horizontal, Kind ∈ Pattern | Bar | None }`. `Bar` = a lone ScrollBar (its
axis is the orientation); `None` = a rule region with no UIA scroll handle. COM objects never enter
Core and are never cached across a scan; the actuator re-resolves them from the `ElementRef`.

### 3.4 Rule regions (`expectedRegions`)

Selectors from the effective policy name panes the generic query misses or misjudges (Slack, Teams,
VS Code sidebar, Gmail folder list). Selector fields and `region: parent` are defined in
[`window-rules.md` § Selectors](window-rules.md#33-selectors). A matched element becomes a candidate of
kind `None` (or `Pattern` if it has one), clipped to the window. With `region: parent`, the
candidate is the element's parent, one per parent.

### 3.5 `scrollDetection` fields

`containerTypes {add, remove}` · `minSizePx` · `viewSizeThreshold` · `suppressDetachedScrollbars` ·
`warmUp {retries, delayMs}` · `expectedRegions [selector]`. Defaults reproduce the values above.

## 4. Actuation: `IScrollActuator`

v1 ships the model validated in the prototype (`3b97d18` → `a6619d7`), decided in this spec (F
[#68](https://github.com/knausj85/Windows-Hinting/issues/68)) from B's ladder plus G's proven
amendments. G's open questions are in [Open items](#open-items-owned-by-g-69).

### 4.1 Principles

1. **The keypress is the authority.** A deliberate key is never silently refused because of what
   UIA claims. UIA axis metadata picks a mechanism; it does not veto (Gmail's folder list reports
   horizontal-only on a vertical list). The one exception is `axisPolicy: RespectUia` (§4.4).
2. **Prefer the control's own programmatic scroll; the aimed wheel is the fallback.** UIA
   `ScrollPattern` is the only mechanism that is cursor-free everywhere, including in a VM.
3. **The hook never actuates.** Actuation runs on a dedicated actuator thread. COM calls and
   verify waits on the low-level keyboard hook thread exceed `LowLevelHooksTimeout` (~300 ms), and
   Windows then leaks the swallowed key to the app.
4. **One in flight, latest wins.** A tick that arrives while one is running is dropped; there is
   never a growing queue (the "freeze" in the prototype).
5. **Boundary guard.** If the target is already at its limit in the requested direction, do
   nothing. Never escalate to a wheel at a limit; it bubbles to a parent and scrolls the wrong pane.

### 4.2 Tap (`Scroll` with `Line`)

| Target | Mechanism |
|---|---|
| Region, `Pattern`, UIA claims the axis | `Scroll(SmallIncrement)`; verify with `MovedSince` (read the axis percent before/after, polling up to ~250 ms since Chromium commits async). Not moved and not at a limit → `Scroll(LargeIncrement)`, verify. Still not moved → one aimed wheel notch. |
| Region, `Pattern`, UIA doesn't claim the axis | `TrustWheel` → one aimed wheel notch in the requested axis. `RespectUia` → `NoOp`. |
| Region, `Bar` | One aimed wheel notch, aimed at the content beside the bar (40 px inside it), not at the bar. |
| Region, `None` | One aimed wheel notch at the region centre. |
| Window `0` | One aimed wheel notch at the window centre. |

**Rung memo.** The winning rung per (target, axis) is remembered while the target stays active,
so later taps skip the verify wait. A rung that stops moving, or throws, drops the memo and the
ladder runs again. A throw on every rung (the element is gone) returns `TargetLost`.

`rungOrder` in the policy can start the ladder at `LargeIncrement` (PowerPoint's thumbnail pane
ignores `SmallIncrement`). `forceWheel` skips UIA for the target app.

### 4.3 Hold (`SetDrive`)

A hold is a **velocity fling** through `SendInput` wheel events, which feed the app's own
smooth-scroll and inertia (`SendMessage` bypasses that pipeline and hard-steps).

- A ~60 Hz high-resolution ticker (`timeBeginPeriod(1)` + `Stopwatch`) emits sub-notch wheel
  deltas with a fractional carry.
- While a drive is set, velocity ramps toward `±velocityMax`; when it clears, velocity decays by a
  friction factor and snaps to zero below a floor.
- Starting constants (prototype): ramp time 0.07 s, max 22 notches/s, decay 0.90 kept per 16 ms,
  floor 0.6 notches/s. They are named constants, and the `scrollActuation` section may override
  them (§4.5).
- Holds use the wheel on every target kind, aimed as in §4.4, so panes glide too. `RespectUia`
  still turns a hold in an axis UIA doesn't claim into nothing.

### 4.4 Aiming the wheel

`SendInput` wheel events go to the window under the cursor (Windows 10+ default wheel routing), so
the wheel must be **aimed**:

1. **Park.** Move the (hidden) cursor to the aim point with an absolute `SendInput` move, so the
   app sees a real hover-enter. Wait a debounce (default 40 ms) before the first wheel event.
2. The cursor **stays parked** on the target until it changes or the mode ends. It is not put back
   between taps.
3. **Skip the park** when the physical cursor is already inside the active region (only for
   `Pattern` and `None` regions; bars and the window always park).
4. On exit, put the cursor back where it was when the mode started.

`axisPolicy` (`RespectUia` | `TrustWheel`) chooses what happens when UIA says a `Pattern` region
can't scroll on the requested axis. Chromium lies about axes, so a wheel is right there; a native
single-axis tree tells the truth, and a wheel in the other axis bubbles to a parent. **Default
`RespectUia`.** The built-in rules set `TrustWheel` for Chromium-family windows (see
[`builtin-rules.json`](builtin-rules.json)).

**Known envelope (to be confirmed by G):** on bare metal the parked wheel reaches the target
regardless of where the user's mouse is. In a VM the cursor can't be moved, so wheel-based
scrolling follows the physical mouse; only UIA increments are cursor-free there. Elevated target
windows block `SendInput` (UIPI): holds do nothing and taps work only through UIA.

### 4.5 `scrollActuation` fields

`axisPolicy` · `rungOrder` (`SmallFirst` | `LargeFirst`) · `forceWheel` · `parkDebounceMs` ·
`velocity { max, rampSeconds, decayPer16ms, floor }` · `tapWheelDelta` (120). Defaults are the
values above. G may add fields when it resolves.

### 4.6 Probe and commit stay separate

The "learn the winning rung" step (run a rung, observe `MovedSince`) stays separable from leaving
the content scrolled, so a later "which rung would fire — report, don't commit" diagnostic can be
added without a redesign. No report-only mode ships in v1.

## 5. Input

Decided in [C #65](https://github.com/knausj85/Windows-Hinting/issues/65) and
[D #66](https://github.com/knausj85/Windows-Hinting/issues/66).

### 5.1 Keymap → intent (the contract)

| Key (real or injected) | Intent |
|---|---|
| `Ctrl+Alt+S` (configurable) | `EnterScrollMode`; while scrolling → `ExitScrollMode(Toggle)` |
| `0`, `1`–`9` | `SwitchScrollTarget(n)` |
| `H` / `J` / `K` / `L` press | `ScrollTick(axis, ±, Line)` |
| `H` / `J` / `K` / `L` held past the hold threshold | `ScrollHoldStart(axis, ±)`, then `ScrollHoldEnd(axis)` on key-up |
| `Esc` | `ExitScrollMode(Key)` |

Direction mapping is vim's: `H` = left, `J` = down, `K` = up, `L` = right. (The prototype used
`H` up / `K` left; v1 deliberately changes that, decided in F #68.)

### 5.2 Tap versus hold

The input adapter times holds itself and ignores OS auto-repeat. On a fresh key-down it posts
`ScrollTick` and starts a timer. If the key is still down after **130 ms** it posts
`ScrollHoldStart`; the key-up then posts `ScrollHoldEnd`. A key-up before 130 ms posts nothing more.
This removes the stall that waiting for OS auto-repeat (~300 ms) caused. Talon sends a hold as
key-down, wait, key-up, which goes through the same path.

### 5.3 Full key capture

While scrolling, the low-level hook swallows **every** key: mapped keys act, all others are eaten
until exit. Key-ups of `H J K L` are swallowed too, so the app never sees a lone key-up. OS-reserved
combinations (`Ctrl+Alt+Del`, `Win+L`) still pass: a documented limit. Injected keys are handled
the same as real ones, except keys the app itself injected (none in v1, kept as a guard).

## 6. Overlay

Decided in [C #65](https://github.com/knausj85/Windows-Hinting/issues/65). The overlay renders
state and never handles input; it stays click-through, no-activate, and has zero UIA footprint.

### 6.1 Display model

```
ScrollOverlayModel { IReadOnlyList<ScrollTargetView> Targets }      // includes the window as 0
ScrollTargetView  { int Number; ScreenRect Bounds; bool IsActive; TargetKind Kind ∈ {Window, Region} }
```

The engine re-emits the whole model on each switch; numbers stay put and `IsActive` moves.

### 6.2 Rendering

- Every target gets a numbered **badge** in its top-left inner corner, inset and clamped to the
  screen. One constant accent colour for all; **the number is the identity** (no palette).
- The **active** target also gets a subtle inset outline: the region rect, or an inset border
  around the window for `0`.
- When badges would overlap, the renderer nudges them apart and draws a short leader tick. This
  needs the measured font size, so it lives in the renderer, not Core.
- No axis glyph (`ScrollCaps.Horizontal` is too unreliable to advertise). No on-screen legend.

### 6.3 Cursor

The pointer is hidden for the whole mode with `SetSystemCursor`, replacing every `OCR_*` cursor
with a transparent one. It is the only method that hides the pointer over another app's focused
window. It swaps the bitmap only, so the logical cursor still moves and hit-tests and parking still
works. Do **not** use `ClipCursor`.

`SetSystemCursor` changes a desktop-wide resource, so restoring it must be guaranteed:
`SystemParametersInfo(SPI_SETCURSORS)` on every exit path, on `ProcessExit` and on unhandled
exceptions. **Added by this spec:** write a "cursor hidden" marker when hiding and clear it on
restore; at app start, if the marker is present (a previous crash), restore the cursors.

### 6.4 Mode marker (Talon)

Every per-screen overlay window's title is `Windows Hinting Overlay [Scroll]` while scrolling and
goes back to `Windows Hinting Overlay` on exit. It must never contain `[Active]` (Talon's hint-mode
check is a substring match). No target information goes in the title. If a use case for the target
type ever appears, it would be a trailing token (`[Scroll] [List]`).

## 7. Dismissal

Validated in the prototype (`1d3d64c`) and already ported to click mode as `HintDismissalWatcher`.
While scrolling the host watches:

- **Real mouse clicks** (`WH_MOUSE_LL`, any button, skipping injected events): exit, and the click
  still reaches the app.
- **Foreground change** (`EVENT_SYSTEM_FOREGROUND`, skipping our own process): exit. The actuator
  could scroll a background window, but the global hook would keep eating keys while the user
  works elsewhere.
- **Target window move or resize** (`EVENT_OBJECT_LOCATIONCHANGE` on the target's thread, the
  window object only): exit, because the region rects are stale.

Each posts `ExitScrollMode` with its reason. Exit is idempotent.

## 8. Threading

- Every intent is serialized on the UI thread, as for click mode.
- The hook thread only maps keys to intents and returns; it never touches COM.
- The actuator owns one worker thread for taps (one in flight, latest wins) and one ticker thread
  for the velocity loop. UIA calls use an apartment the actuator owns (or marshal to the UI thread,
  as the prototype did); verify waits never run on the UI or hook thread.
- `ScrollOutcome.TargetLost` comes back as a posted intent, never a call into the engine.

## 9. External surface

Title plus keystrokes ([D #66](https://github.com/knausj85/Windows-Hinting/issues/66)): Talon reads
the mode marker and sends the §5.1 keys. There is no pipe and no RPC. An app-hosted pipe is LATER,
gated on the Talon REPL / talon-rpc research
([M #93](https://github.com/knausj85/Windows-Hinting/issues/93)); if it comes, its adapter posts the
same intents, including the hold intents, so it never needs key-up semantics.

## 10. Reconciliations

Later decisions superseded parts of earlier tickets. This spec follows the later one:

| Earlier | Now | Why |
|---|---|---|
| B: region wheel = `SendMessage(WM_MOUSEWHEEL)` at the region centre, cursor unmoved | Parked `SendInput` wheel under a hidden cursor | `SendMessage` is ignored by hover-routed surfaces (Chromium), re-hit-tested by WPF, and fails on UWP; only `SendInput` engages native inertia (G) |
| B: hard axis gate on `ScrollCaps` | Keypress is authority; `axisPolicy` per rule | Gmail's axis lie (G) |
| B: RangeValue `SetValue` rung for lone scrollbars; RangeValue range in the Gate | Bars are wheel-aimed beside the bar; detected by type + size | RangeValue is ignored (Beyond Compare), thumb-only (VS editor), or `0..0` (Terminal) (G) |
| B: numbering in `FindAll` order | Reading order | C |
| B: rung memo per key-hold, keyed on the `WM_KEYDOWN` repeat bit; A/D: fresh-vs-repeat bit on `ScrollTick` | Memo per active target; adapter ignores auto-repeat, so every `ScrollTick` is a fresh press and the bit is dropped | Self-timed holds (G, D) replace auto-repeat |
| A: `ScrollTick` per OS auto-repeat while held | `ScrollHoldStart/End` → derived `ScrollDrive` | Velocity model (G, D) |
| B: `ScrollOutcome {Scrolled, TargetLost}` | adds `NoOp` (boundary guard, `RespectUia`) | Boundary guard (G) |
| Prototype: rule panes numbered first, then generic | All survivors in one reading order | C |
| Prototype: taps always wheel | Taps try UIA first, then the wheel | §4.1 principle 2 (VM-robust, cursor-free) |
| Prototype: Home/End/PgUp/PgDn re-injected | Cut from v1 | #70 |
| Prototype: `H` up, `K` left | Vim layout: `H` left, `K` up | F #68 |

## 11. Capability limits (documented, not bugs)

- Panes the detector can't see and no rule names can only be reached through window `0`.
- `ToStart`/`ToEnd` (when #70 adds them) need a UIA scroll handle.
- In a VM, wheel-based scrolling follows the physical mouse.
- Elevated windows: no holds, taps only through UIA.
- The VS editor (WPF, scrollbar only) has no cursor-free path; the parked wheel works on bare metal.
- More than 9 regions: the first 9 in reading order.
- OS-reserved key combinations pass through scroll mode.

## 12. Map onto today's `main` seams

| Modernize concept | `main` seam |
|---|---|
| `Scrolling` mode + mode state | `HintStateManager`: add `HintMode.Scrolling` and the scroll state (target window, regions, active index) |
| Scroll intents | `HintInputHandler`: a scroll branch that maps keys (with hold timing) to calls on a new `ScrollController` |
| `EnterScrollMode` hotkey | `HotkeyWindow` + a new `ScrollHotkey` in `HintOverlayOptions` (default `Ctrl+Alt+S`), shown on the Hotkeys page |
| `ScrollRegionSource` | a new `ScrollRegionScanner` beside `UIAutomationService`; the pure stages are static helpers |
| `IScrollActuator` | new `IScrollActuator`/`ScrollActuator` beside `IElementActivator`/`ElementActivatorChain`, registered in `ServiceCollectionExtensions.AddHintOverlayServices` |
| `ScrollOverlayModel` | `OverlayForm`: a `SetScrollTargets(...)` state setter, drawn in `OnPaint`; `OverlayManager` fans it out per screen |
| Mode marker | `OverlayForm` title constant `ScrollTitle = BaseTitle + " [Scroll]"`, set like `SetActiveState` |
| Dismissal | `HintDismissalWatcher` (from `feature/hint-dismissal-parity`), reused |
| `ScrollOutcome.TargetLost` | return value handled by `ScrollController` (sets active to 0) |
| Effective policy | `WindowRuleRegistry` resolution, see [`window-rules.md` § Bridge](window-rules.md#42-bridge-to-main) |
| Scroll constants | `UIAutomationConstants.cs` (the scroll IDs are already there, unused) |

Modernize phases that later absorb this: 02/03 (mode, intents, effects, `DesiredConditions`),
04 (`ScrollRegionSource` and the stages as `IScanStage`s), 06 (overlay model), 07 (rules on disk).

## Open items (owned by G #69)

G [#69](https://github.com/knausj85/Windows-Hinting/issues/69) stays open as follow-on work. It does
not block the build; resolving it may amend §4.

1. **Bare-metal cursor-independence pass** over the matrix: File Explorer / GitHub Desktop
   (baseline), Settings (UWP), VS Solution Explorer, VS editor, Beyond Compare, VS Code, Slack.
   Each must scroll the chosen target without the user's mouse over it. All testing so far was in a
   VM.
2. **Touch-pan lane** (`InjectTouchInput`): cursor-free and inertial in theory; did nothing in the
   VM. Retest on bare metal; if it works it may replace parking for Electron/WinUI regions.
3. **VS editor (WPF, scrollbar only):** accept parking, or route it to window `0`.
4. **Acceleration curve** for long holds, and first-actuation priming (a first injected event
   ignored in the VM).
