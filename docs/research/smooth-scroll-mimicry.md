# Smooth-Scroll Mimicry: Injecting Native-Feeling Wheel Scroll into Arbitrary Foreground Apps

**Research question:** How to synthesize Windows-native-feeling "smooth scrolling" by injecting scroll into arbitrary foreground apps — mimicking a physical mouse wheel spun quickly — to feed a design spec for a hands-free scroll tool (C#/.NET, Win32 + UI Automation) that currently injects `WM_MOUSEWHEEL` via `SendMessage` and drives UIA `ScrollPattern`/`RangeValue`, but wants a velocity/inertia model driven at a fixed cadence instead of one discrete step per keypress.

**Status:** Read-only research. Sources are primarily Microsoft Learn / MSDN plus the Android AOSP `OverScroller` reference for the kinetic model. Where behavior is app-dependent or undocumented, it is flagged inline.

**Bottom line up front:**
- Smoothness cannot come from UIA — `ScrollPattern` is always a positional jump (§6). It must come from the **wheel path**.
- To feed an app's own inertia/animation engine, prefer the **real input pipeline** (`SendInput` with `MOUSEEVENTF_WHEEL`) over `SendMessage(WM_MOUSEWHEEL)` — Microsoft's inertia system (`InteractionTracker`) explicitly consumes "auto-redirected pointer wheel input" as an impulse, and that redirection only happens for input that flows through the pipeline (§2, §3).
- Feed the target a **stream of sub-notch wheel deltas at a fixed cadence (~60 Hz / vsync)** whose magnitude is driven by a decaying velocity model (§4, §5). This is what makes injected wheel input look like a fast physical spin rather than a hard jump.
- The unavoidable tension: `SendInput` wheel input is **not per-window addressable** — it is routed by the OS to the focus window or the window under the cursor, and cannot be aimed without owning focus or moving/placing the cursor (§2). `SendMessage` *is* per-window addressable but tends to bypass the inertia engine. This trade-off is the central design decision.

---

## 1. Wheel input plumbing

### `WM_MOUSEWHEEL` / `WM_MOUSEHWHEEL` message semantics

`WM_MOUSEWHEEL` (`0x020A`) is "sent to the **focus window** when the mouse wheel is rotated." `DefWindowProc` propagates it **up the parent chain** until a window processes it — so a control that doesn't handle it hands it to its parent automatically. `WM_MOUSEHWHEEL` (`0x020E`, Vista+) is the horizontal analogue.
Source: [WM_MOUSEWHEEL message](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel), [WM_MOUSEHWHEEL message](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousehwheel)

### `wParam` / `lParam` packing

| Field | Contents |
| --- | --- |
| `wParam` **HIWORD** (high-order `short`) | Signed wheel-delta `zDelta`, a multiple/division of `WHEEL_DELTA`. **Positive = forward/away from user (scroll up); negative = backward/toward user (scroll down).** For `WM_MOUSEHWHEEL`, positive = right, negative = left. |
| `wParam` **LOWORD** | Bitmask of held virtual keys: `MK_CONTROL 0x0008`, `MK_SHIFT 0x0004`, `MK_LBUTTON 0x0001`, `MK_MBUTTON 0x0010`, `MK_RBUTTON 0x0002`, `MK_XBUTTON1 0x0020`, `MK_XBUTTON2 0x0040`. |
| `lParam` **LOWORD** (`short`) | **x-coordinate of the pointer in SCREEN coordinates** (relative to upper-left of the screen). |
| `lParam` **HIWORD** (`short`) | **y-coordinate of the pointer in SCREEN coordinates.** |

Extract with the documented macros — not `LOWORD`/`HIWORD` for the coordinates:
```c
fwKeys = GET_KEYSTATE_WPARAM(wParam);
zDelta = GET_WHEEL_DELTA_WPARAM(wParam);   // signed
xPos   = GET_X_LPARAM(lParam);             // screen X, signed
yPos   = GET_Y_LPARAM(lParam);             // screen Y, signed
```
The coordinates are **signed** because multi-monitor systems produce negative coordinates; `LOWORD`/`HIWORD` treat them as unsigned and give wrong results across monitors.
Source: [WM_MOUSEWHEEL message](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel), [GET_WHEEL_DELTA_WPARAM macro](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-get_wheel_delta_wparam)

**Implication for injection:** when posting `WM_MOUSEWHEEL` directly, `lParam` carries **screen coordinates**, so a window that hit-tests the pointer position (many do, e.g. to pick which pane scrolls) needs coordinates that land inside the target rect — but you never have to *move* the real cursor to supply them, you just pack the numbers.

### `WHEEL_DELTA` = 120 and high-resolution / fractional deltas

`WHEEL_DELTA` is **120** and represents one detent ("click/notch") of a traditional notched wheel — the threshold at which one increment of action should occur. The value was deliberately set to 120 (not 1) so finer-resolution or free-spinning wheels can send **more frequent messages with a smaller delta each** (e.g. 40, or even single digits). If `abs(zDelta) < 120`, the wheel is high-resolution.
Source: [WM_MOUSEWHEEL message](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)

### How the OS / apps accumulate partial deltas

The documented app-side contract is either:
1. **Accumulate** incoming deltas until `WHEEL_DELTA` (120) is reached, then perform one increment of action (so a high-res wheel produces the same total response as a notched one); **or**
2. **Scroll partial lines** in response to the more frequent smaller messages; **or**
3. **Choose your own scroll granularity** and accumulate deltas until it is reached.

This accumulate-and-carry behavior is exactly the seam a smooth-scroll injector exploits: **emit many sub-120 deltas rapidly** and a well-behaved app will translate them into fractional/partial scrolling instead of one line-per-message stepping.
Source: [WM_MOUSEWHEEL message](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)

### `SPI_GETWHEELSCROLLLINES` / `SPI_GETWHEELSCROLLCHARS` and the "scroll one page" value

`SystemParametersInfo(SPI_GETWHEELSCROLLLINES, 0, &uLines, 0)` returns the user's **lines-per-notch** setting (`SPI_GETWHEELSCROLLLINES == 104`; default **3** lines). `SPI_GETWHEELSCROLLCHARS` returns the horizontal analogue (characters-per-notch) for `WM_MOUSEHWHEEL`.

The special return value **`WHEEL_PAGESCROLL`** (`UINT_MAX`, i.e. `0xFFFFFFFF`) means "one wheel notch should scroll **a full page**" — treat a notch as one click in the PageUp/PageDown region of the scrollbar, not as N lines. An injector that computes a per-notch pixel/line amount MUST special-case this value.
Source: [Using Mouse Input — Retrieving the Number of Mouse Wheel Scroll Lines](https://learn.microsoft.com/en-us/windows/win32/inputdev/using-mouse-input), [SystemParametersInfoA function](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfoa), [SystemParameters.WheelScrollLines](https://learn.microsoft.com/en-us/dotnet/api/system.windows.systemparameters.wheelscrolllines)

Canonical retrieval (from MSDN's own sample):
```c
UINT ucNumLines = 3;                                   // 3 is the default
SystemParametersInfo(SPI_GETWHEELSCROLLLINES, 0, &ucNumLines, 0);
// WHEEL_PAGESCROLL => scroll a page at a time
```
Source: [Using Mouse Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/using-mouse-input)

**Note on responsibility:** the OS does **not** convert the delta into pixels for you — the *application* multiplies `zDelta/120 × linesPerNotch × lineHeight` (or a page). So "how far a notch scrolls" is app-defined, and a native-feeling injector should respect `SPI_GETWHEELSCROLLLINES` when reasoning about how big an impulse to emit, but ultimately the target app owns the pixel mapping.

---

## 2. Injection method comparison — the key question

Two mechanisms deliver a wheel event; they differ in *where the event enters* and therefore in *what downstream machinery it activates*.

### A. `SendMessage(hwnd, WM_MOUSEWHEEL, …)` — direct window post

- **Targeting:** you choose the exact `HWND`. Fully per-window addressable. Does **not** require focus or a specific cursor position (you pack screen coords into `lParam` yourself). Never moves the user's cursor.
- **Path:** bypasses the system input queue and the raw-input / pointer-input plumbing. The message lands straight in the target's `WndProc`.
- **Consequence:** because it never traverses the real input pipeline, **it will not be "auto-redirected" into the composition/DirectManipulation inertia systems** (see §3). Apps that implement smooth scroll by watching the *input stream* (DirectManipulation via `ProcessInput`, InteractionTracker's `VisualInteractionSource`) frequently **ignore a directly-posted `WM_MOUSEWHEEL`** or treat it as a plain positional step — you get a hard jump, not a glide. Apps that implement wheel handling the classic way (a `case WM_MOUSEWHEEL:` in their `WndProc` that scrolls N lines) **do** respond, but only with their own per-message step.
- **Robustness caveats:** `SendMessage` blocks until the target processes it (use `PostMessage`/`SendMessageTimeout` to avoid stalling on a hung app). Some apps only honor wheel via lower-level hooks and ignore a synthetic `WM_MOUSEWHEEL` entirely.

Source: [WM_MOUSEWHEEL message](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)

### B. `SendInput` with `MOUSEEVENTF_WHEEL` / `MOUSEEVENTF_HWHEEL` — real input pipeline

- **Path:** "inserts the events … **serially into the … mouse input stream**," and — critically — these events are **not interspersed** with real user input or with other injectors. The event is indistinguishable to the OS from a hardware wheel tick, so it flows through raw input → pointer input → the same routing and redirection real wheel input gets.
- **`mouseData`:** for `MOUSEEVENTF_WHEEL` (`0x0800`) / `MOUSEEVENTF_HWHEEL` (`0x1000`), `mouseData` is the wheel movement; one click = `WHEEL_DELTA` = 120; sign conventions match `WM_MOUSEWHEEL`. You can pass **sub-120 values** for high-resolution/partial ticks.
- **Targeting — the crux:** `SendInput` wheel has **no window/HWND parameter**. The OS decides the target exactly as it would for a hardware wheel:
  - Documented rule: `WM_MOUSEWHEEL` goes to the **focus window**.
  - Practical modern rule: with "**Scroll inactive windows when I hover over them**" (the default in Windows 10/11), the OS routes the wheel to the **window under the pointer**. So in practice the effective target is *cursor-position-dependent*. (This hover-routing behavior is a shell/`win32k` feature and is not spelled out on the `WM_MOUSEWHEEL` reference page — flag as OS-version/setting dependent.)
  - **You cannot aim `SendInput` wheel at an arbitrary background window.** To hit a specific window you must either give it focus or ensure the cursor is over it.
- **UIPI restriction:** `SendInput` "is subject to UIPI. Applications are permitted to inject input only into applications that are at an **equal or lesser integrity level**." Injection into an elevated app from a non-elevated tool **fails silently** — neither the return value nor `GetLastError` reports the UIPI block. A hands-free tool that must work over elevated windows needs to run elevated (or accept that it can't scroll them).
- **Cursor concern:** `MOUSEEVENTF_WHEEL` alone does **not** move the cursor (`dx`/`dy` are ignored unless you also set `MOUSEEVENTF_MOVE`). So a pure wheel `SendInput` **does not move the user's cursor** — good. But *targeting* it at a chosen window may require the cursor to be over that window, which conflicts with "must not move the user's cursor." Options: (a) rely on focus routing instead of hover routing; (b) temporarily reposition the cursor and restore it (visible, undesirable); (c) accept that the tool scrolls "whatever the user is pointing at / focused on," which is usually what a hands-free user wants anyway.

Source: [SendInput function](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput), [MOUSEINPUT structure](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput), [WM_MOUSEWHEEL message](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)

### Which better triggers the app's own smooth-scroll/inertia?

**`SendInput` (real pipeline) is the better bet for triggering native inertia/animation**, because Microsoft's own inertia machinery is documented to consume **"auto-redirected pointer wheel input"** as an inertia impulse (§3) — that redirection is a property of the input pipeline, not of a directly-posted window message. `SendMessage(WM_MOUSEWHEEL)` is more *controllable* (exact HWND, no cursor/focus dependence) but more likely to produce a stepping jump because it bypasses the redirection.

### Summary table

| Property | `SendMessage(WM_MOUSEWHEEL)` | `SendInput(MOUSEEVENTF_WHEEL)` |
| --- | --- | --- |
| Per-window addressable | **Yes** (exact HWND) | **No** (OS routes by focus / cursor) |
| Requires focus or cursor over target | No | Yes (one or the other) |
| Moves user cursor | No | No (wheel-only doesn't move cursor) |
| Enters real input pipeline | No (direct post) | **Yes** (serial, un-interspersed) |
| Triggers DirectManipulation / InteractionTracker inertia | Often no → hard step | **Yes** (auto-redirected wheel impulse) |
| Blocked by UIPI / integrity level | No (but message may be ignored) | **Yes** (equal/lesser integrity only; fails silently) |
| Sub-notch (<120) deltas supported | Yes | Yes |
| Best for | Precise targeting of a known scrollable HWND | Native-feeling inertia on the focused/hovered surface |

**Design recommendation:** default to `SendInput` for the smooth path (to engage native inertia), and keep `SendMessage(WM_MOUSEWHEEL)` to a **specific child HWND** as a targeting fallback for apps that ignore pipeline wheel or where you must scroll a non-focused/non-hovered pane. This mirrors the escalation ladder already prototyped in this repo (wheel → UIA), just adding "pipeline wheel vs. direct-post wheel" as an earlier fork.

---

## 3. How modern Windows apps produce smooth/animated scrolling from wheel input

Modern Windows scrolling is produced by a **compositor-driven, off-UI-thread animation engine**, not by the app redrawing per message:

- **DirectManipulation** (Win32/legacy modern apps, IE/Edge legacy, many WinUI hosts): the app forwards keyboard/mouse messages to DirectManipulation via **`ProcessInput`**, and DirectManipulation runs the pan/zoom and — on "release" — an **inertia animation that gradually decelerates to a resting place.** The wheel is treated as an impulse that seeds that inertia.
  Source: [Direct Manipulation portal](https://learn.microsoft.com/en-gb/previous-versions/windows/desktop/directmanipulation/direct-manipulation-portal)

- **`InteractionTracker` + `Windows.UI.Composition` / `Microsoft.UI.Composition`** (UWP, WinUI 3, modern XAML `ScrollViewer`): a state machine whose position is driven on an **independent animation thread at 60 FPS**, so motion stays smooth even if the UI thread is busy. Inertia can be seeded from a gesture's release velocity **or** programmatically (`TryUpdatePositionWithInitialVelocity`). During inertia, position interpolates from the release velocity down to zero under an inertia curve.
  Source: [Custom manipulation experiences with InteractionTracker](https://learn.microsoft.com/en-us/windows/apps/develop/composition/interaction-tracker-manipulations)

- **`IsInertiaFromImpulse` — the wheel-specific signal (most relevant fact):** InteractionTracker exposes `IsInertiaFromImpulse`, which is **true when the inertia originated from a discrete/impulse source, specifically "auto-redirected pointer wheel input"** (and `TryUpdatePositionWithInitialVelocity`). Microsoft's guidance:
  > "when scrolling a page of content, a small touch delta can result in the content getting snapped back to its original starting value. **However, when scrolling with the pointer wheel, each tick of the wheel should move the content a full page per tick.** You can define snap points (inertia modifiers) for use with the pointer wheel …, but specify different criteria for … touch."

  Two consequences for an injector:
  1. **Injected wheel input *does* feed these inertia systems** — but only via the redirected pipeline path (so via `SendInput`, per §2), because the system labels it "auto-redirected pointer wheel input."
  2. **Magnitude/cadence matters and is interpreted differently from touch.** A wheel tick is treated as a *discrete impulse* (often snapping a whole page or a defined snap-point), whereas touch deltas are continuous. So emitting **many small wheel impulses at a fixed cadence** produces a fundamentally different (and more "wheel-like") motion than one big delta — and can trigger the app's per-tick inertia/snap logic repeatedly, which is what a fast physical spin does.

  Source: [InteractionTracker.IsInertiaFromImpulse](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.interactions.interactiontracker.isinertiafromimpulse), [Create snap points with inertia modifiers](https://learn.microsoft.com/en-us/windows/uwp/composition/inertia-modifiers)

- **WPF / WinForms / classic Win32:** many controls do **not** animate at all — they scroll N lines per message (`Mouse.MouseWheelDeltaForOneLine` = 120 in WPF). Smoothness there must be *manufactured externally* by the cadence of impulses you send; the app itself just steps. WPF's `ScrollViewer` gained opt-in smooth wheel scrolling in recent .NET but it is not universal.
  Source: [Mouse.MouseWheelDeltaForOneLine field](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.mouse.mousewheeldeltaforoneline)

**Does injection cadence/magnitude matter for inertia vs. hard jump?** Yes:
- One large delta (e.g. `10 × 120`) tends to read as "jump N pages now."
- A **train of small deltas at ~60 Hz**, ramping then decaying, is what an inertia processor integrates into a smooth glide — and is what a fast physical wheel spin actually produces at the hardware level (a high-res wheel emits a burst of sub-120 messages).
- On apps with no inertia engine, the *only* smoothness available is the one **your** ticker manufactures by pacing small deltas — the app will just step, but stepping every 16 ms by a fraction of a line looks smooth.

---

## 4. Kinetic / inertial scrolling algorithms

The industry-standard "fling → glide → stop" model is the Android **`OverScroller`** spline model, which is a good, well-documented reference for the velocity/decay math (iOS and DirectManipulation use conceptually identical exponential-decay-of-velocity models with different constants).

### The velocity model (fling)

1. **Velocity accumulation while "held":** input events add to a running velocity; multiple flings in quick succession **accumulate** (the "**flywheel**" — successive flings keep increasing scroll speed). This is exactly the "spin the wheel quickly / repeatedly" feel.
2. **On release:** velocity decays toward zero under an exponential/spline curve until it hits a **minimum-velocity cutoff**, then stops.

### `OverScroller` constants and formulas (AOSP reference)

```
INFLEXION        = 0.35            // point where the spline flips tension
DECELERATION_RATE = ln(0.78) / ln(0.9)  ≈ 2.358
START_TENSION    = 0.5
END_TENSION      = 1.0
GRAVITY          = 2000.0          // px/s^2 used for overscroll bounce
mFlingFriction   = ViewConfiguration.getScrollFriction()  // default ≈ 0.015
mPhysicalCoeff   = 9.80665 (g, m/s^2) * 39.37 (in/m) * ppi * 0.84
```
Spline fling math:
```
splineDeceleration(v) = ln( INFLEXION * |v| / (flingFriction * physicalCoeff) )
flingDistance = flingFriction * physicalCoeff
                * exp( DECELERATION_RATE / (DECELERATION_RATE - 1) * splineDeceleration(v) )
flingDuration(ms) = 1000 * exp( splineDeceleration(v) / (DECELERATION_RATE - 1) )
```
Source: [Android OverScroller reference](https://developer.android.com/reference/android/widget/OverScroller) and the widely-mirrored AOSP `OverScroller.java` (e.g. [OverScroller.java constants](https://github.com/Mixiaoxiao/OverScroll-Everywhere/blob/master/OverScroll/src/com/mixiaoxiao/overscroll/OverScroller.java)); [Animate a scroll gesture](https://developer.android.com/develop/ui/views/touch-and-input/gestures/scroll)

### Simpler exponential-decay model (adequate for a wheel injector)

For a fixed-cadence injector you rarely need the full spline; an exponential decay is enough and is what most "kinetic scroll" implementations use:
```
v(t+dt) = v(t) * pow(friction, dt / dtRef)      // e.g. friction 0.90–0.95 per 16 ms tick
stop when |v| < vMin
```
Typical parameter ranges observed across these frameworks:
- **Deceleration / friction:** ~0.90–0.98 velocity retained per 16 ms tick (equivalently a time constant of a few hundred ms). `OverScroller`'s effective retention is ≈0.78 over its reference interval.
- **Max velocity:** clamp to a sane ceiling so a long "hold" doesn't teleport (Android clamps to a `ViewConfiguration` maximum fling velocity, typ. a few thousand px/s).
- **Min-velocity cutoff:** a small threshold (order of a fraction of a line per tick) below which you snap to rest — prevents a visible "creep" that never stops.
- **Acceleration while held:** velocity ramps up (accumulate + flywheel) rather than jumping to max, so onset is smooth.

### What makes it feel "native" vs. sluggish/jittery
- **High, regular tick rate** (see §5). Irregular ticks read as jitter.
- **Sub-notch granularity** so each tick moves a fraction of a line, not a whole line — whole-line-per-tick is the classic "steppy" feel.
- **Exponential (not linear) decay** — linear decay feels like braking; exponential feels like coasting.
- **Onset ramp + flywheel accumulation** — instant max velocity feels like a jump; gradual build + decay feels physical.
- **Velocity clamp + min cutoff** — prevents both teleporting and never-ending creep.

---

## 5. Fixed-cadence ticker design

### Tick rate
Target **~60 Hz (≈16.67 ms/tick)**, i.e. one impulse per display refresh, matching InteractionTracker's own **60 FPS independent animation thread**. On high-refresh displays, aligning to vsync (90/120/144 Hz) reduces visible stepping further, but 60 Hz is the floor for "smooth." Below ~30 Hz motion reads as stepping.
Source: [Custom manipulation experiences with InteractionTracker](https://learn.microsoft.com/en-us/windows/apps/develop/composition/interaction-tracker-manipulations) (60 FPS animation thread)

**Timer accuracy caveat (Win32/.NET):** the default `System.Threading.Timer` / `Windows.Forms.Timer` resolution is coarse (~15.6 ms and jittery). For a steady 60 Hz cadence use a multimedia timer (`timeBeginPeriod(1)` + `timeSetEvent`) or a high-resolution waitable timer, or drive off `DwmFlush`/composition vsync. Jittery cadence is the #1 cause of "not smooth."

### Per-tick delta granularity (velocity → discrete wheel impulses)
The wheel API is quantized (integer `mouseData`), so map continuous velocity to **fractional-notch integer deltas** and **carry the remainder** — the same accumulate-and-carry the OS documents for high-res wheels (§1):
```
// velocity in "notch-units per second" (1 notch = WHEEL_DELTA = 120)
accumulator += velocity_notches_per_sec * dt;      // dt ≈ 0.01667 s
int deltaNotches = (int)floor(accumulator);        // whole sub-steps to emit this tick
accumulator -= deltaNotches;                        // keep the fraction for next tick
int mouseData = deltaNotches * (WHEEL_DELTA / N);   // N sub-steps per notch, e.g. N=4 -> 30
if (mouseData != 0) SendInput(wheel, mouseData);
```
Guidelines:
- **Emit sub-`WHEEL_DELTA` deltas** (e.g. 20–40 per tick, i.e. WHEEL_DELTA/6 … /3) rather than multiples of 120. This is precisely the "finer-resolution wheel" case the OS was designed for, and well-behaved apps translate it into partial-line scrolling.
- **Never skip the carry** — dropping the fractional remainder each tick produces uneven motion. Accumulate it.
- **One impulse per tick**, sized by current velocity, rather than bursting several `SendInput` calls — the pipeline coalesces and the visual result is cleaner.
- For apps that only step per whole notch (no partial-line support), fall back to emitting a whole `120` on the ticks where the accumulator crosses 1.0; motion is steppier but still paced by the velocity curve.

### Putting it together
`velocity model (§4) → per-tick delta with carry (§5) → SendInput wheel (§2) → app inertia engine (§3)`. The velocity curve shapes the *envelope*; the app's own inertia (if any) smooths *within* each impulse; on inertia-less apps the ticker's own pacing is the only smoothing.

---

## 6. UIA `ScrollPattern` and smoothness

**Confirmed: UIA scrolling is always a positional jump — it never animates.** Smoothness therefore cannot come from the UIA path; it must come from the wheel path (§2–§5).

- **`IScrollProvider::SetScrollPercent(horizontalPercent, verticalPercent)`** "Sets the horizontal and vertical scroll position **as a percentage** of the total content area." It is an **absolute set** of the scroll position; there is no animation, velocity, or easing in the contract — the provider moves the viewport to that position. (Pass `UIA_ScrollPatternNoScroll` for an axis you don't want to move.)
  Source: [IScrollProvider::SetScrollPercent (Win32)](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-iscrollprovider-setscrollpercent), [IScrollProvider.SetScrollPercent (.NET)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.provider.iscrollprovider.setscrollpercent)

- **`IScrollProvider::Scroll(ScrollAmount, ScrollAmount)`** moves by a **discrete increment** per axis: `SmallIncrement`, `LargeIncrement`, `SmallDecrement`, `LargeDecrement`, or `NoAmount`. `SmallIncrement` "should be equivalent to a mouse wheel or arrow-key movement"; `LargeIncrement` is a page. These are **quantized positional steps**, not a continuous animated motion. Whether the *provider* chooses to animate the resulting move is entirely up to the app and is not part of the pattern contract — you cannot rely on or control it.
  Source: [IScrollProvider.Scroll (.NET)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.provider.iscrollprovider.scroll), [Implementing the UI Automation Scroll Control Pattern](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/implementing-the-ui-automation-scroll-control-pattern), [Scroll Control Pattern (Win32)](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingscroll)

**Role of UIA in the tool:** keep `ScrollPattern`/`RangeValue` as the **reliable fallback for reaching a position** (or for apps that expose no usable wheel target), and as the way to **read** scroll extent/percent to know when you've hit the end. But treat every UIA scroll as a teleport; do not attempt to fake smoothness by rapidly calling `SetScrollPercent` in a loop — that produces visible tearing/stepping and defeats the app's own compositor animation. Smoothness lives on the wheel path.

---

## Consolidated recommendations for the design spec

1. **Two-lane actuator.** Lane 1 (smooth): fixed-cadence `SendInput(MOUSEEVENTF_WHEEL)` sub-notch impulses driven by a velocity model — engages native inertia (`IsInertiaFromImpulse`). Lane 2 (reliable position): UIA `SetScrollPercent` / `Scroll` for teleporting and for apps with no usable wheel target. This extends the existing repo escalation ladder (Small→Large→SetScrollPercent→wheel) by splitting "wheel" into pipeline-wheel (smooth) vs. direct-post-wheel (targeted).
2. **Prefer `SendInput` for feel, `SendMessage(WM_MOUSEWHEEL)` for aim.** Accept that `SendInput` wheel goes to the focused/hovered window and cannot be aimed elsewhere without focus/cursor placement; design the UX so the user's chosen scroll region *is* the focused/hovered surface (consistent with the select-a-scroll-region overlay already prototyped).
3. **Never move the user's cursor.** Pure wheel injection doesn't; if targeting forces cursor placement, prefer focus-based routing or a save/restore, and document the limitation.
4. **Velocity model:** exponential decay (retain ~0.9–0.95 per 16 ms), onset ramp, flywheel accumulation on repeated "spin," velocity clamp, min-velocity cutoff. `OverScroller` constants (§4) are a good starting calibration.
5. **Ticker:** 60 Hz minimum, vsync-aligned if possible, on a high-resolution timer; one impulse per tick sized by velocity; **accumulate the fractional remainder** and emit sub-`WHEEL_DELTA` deltas.
6. **Respect `SPI_GETWHEELSCROLLLINES` / `WHEEL_PAGESCROLL`** when reasoning about impulse magnitude, but remember the target app owns the final pixel mapping.
7. **UIPI/elevation:** run elevated if the tool must scroll elevated windows; otherwise `SendInput` fails silently over them.

---

## Open / app-dependent / undocumented items
- **Hover-vs-focus wheel routing** ("scroll inactive windows when I hover") is a shell/`win32k` behavior that governs where `SendInput` wheel lands; it is not documented on the `WM_MOUSEWHEEL` reference page and depends on OS version and a user setting. Verify empirically per target.
- **Whether a given app's inertia engine consumes injected (vs. hardware) wheel** is app/framework-specific. WinUI/UWP (InteractionTracker) and DirectManipulation hosts should; classic Win32/WPF controls generally won't (they step). Requires per-app testing — dovetails with the app-profile layer already recommended in the scroll-capability planning map.
- **`ScrollPattern` provider-side animation** is not contractual; do not depend on it.

## Primary sources
- [WM_MOUSEWHEEL message (Win32)](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)
- [WM_MOUSEHWHEEL message (Win32)](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousehwheel)
- [GET_WHEEL_DELTA_WPARAM macro](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-get_wheel_delta_wparam)
- [Using Mouse Input (wheel sample, SPI_GETWHEELSCROLLLINES)](https://learn.microsoft.com/en-us/windows/win32/inputdev/using-mouse-input)
- [SystemParametersInfoA function](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfoa)
- [SystemParameters.WheelScrollLines (.NET)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.systemparameters.wheelscrolllines)
- [SendInput function](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [MOUSEINPUT structure](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput)
- [Direct Manipulation portal](https://learn.microsoft.com/en-gb/previous-versions/windows/desktop/directmanipulation/direct-manipulation-portal)
- [Custom manipulation experiences with InteractionTracker](https://learn.microsoft.com/en-us/windows/apps/develop/composition/interaction-tracker-manipulations)
- [InteractionTracker.IsInertiaFromImpulse](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.interactions.interactiontracker.isinertiafromimpulse)
- [Create snap points with inertia modifiers](https://learn.microsoft.com/en-us/windows/uwp/composition/inertia-modifiers)
- [Mouse.MouseWheelDeltaForOneLine (WPF)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.mouse.mousewheeldeltaforoneline)
- [IScrollProvider::SetScrollPercent (Win32)](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-iscrollprovider-setscrollpercent)
- [IScrollProvider.Scroll (.NET)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.provider.iscrollprovider.scroll)
- [Implementing the UI Automation Scroll Control Pattern](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/implementing-the-ui-automation-scroll-control-pattern)
- [Android OverScroller (fling physics reference)](https://developer.android.com/reference/android/widget/OverScroller) · [AOSP OverScroller.java constants (mirror)](https://github.com/Mixiaoxiao/OverScroll-Everywhere/blob/master/OverScroll/src/com/mixiaoxiao/overscroll/OverScroller.java) · [Animate a scroll gesture](https://developer.android.com/develop/ui/views/touch-and-input/gestures/scroll)
