# Cursor hide / lock for keyboard-driven scroll mode

**Status:** research findings only (no code changed)
**Date:** 2026-09-17
**Context:** prototype `#69` scroll capability; actuation parks the physical cursor to aim the injected wheel (SendInput `MOUSEEVENTF_MOVE|ABSOLUTE` -> debounce hover-enter -> SendInput `MOUSEEVENTF_WHEEL` -> restore). The visible cursor "jump" is the UX wart to hide.

## Problem statement

While scroll mode is active, an overlay (topmost, click-through, `WS_EX_NOACTIVATE`) sits over a target app that keeps keyboard focus. To aim the wheel, actuation borrows the physical cursor, moves it onto the chosen region's content point, injects the wheel, and moves it back. We want the cursor to be **invisible and/or pinned** for the duration of the modal, keyboard-driven scroll, so the borrow-and-restore jump is never seen — **without** breaking the aiming mechanism and while our process is *not* the foreground/focused window.

Two facts dominate every answer below:
1. **We are not foreground.** The target app owns keyboard focus; our overlay is `NOACTIVATE`. Any API whose effect is scoped to "the calling thread's window" or "the foreground window" is therefore weak or unavailable to us.
2. **The cursor image is owned by whoever's window it is over.** As the cursor moves, the system sends `WM_SETCURSOR` to the window under it and that window sets its class cursor via `SetCursor`. When the cursor is over the *target* app's window, the *target* process decides the arrow — not us.

---

## Approach A - Hide the cursor image

### A1. `ShowCursor(FALSE)` -- **NO-GO for our scenario**

`ShowCursor` "sets an internal display counter... The cursor is displayed only if the display count is greater than or equal to 0" ([ShowCursor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showcursor)). It is a decrement/increment counter (initial 0 with a mouse). Two problems make it unusable here:

- The counter that governs visibility is scoped to the input/thread processing the cursor, and the visible arrow is (re)drawn by the **window under the cursor** via `WM_SETCURSOR` + `SetCursor` ([Using Cursors -> Displaying a Cursor](https://learn.microsoft.com/en-us/windows/win32/menurc/using-cursors): "when the cursor moves, the system redraws the class cursor at the new location"). Since the cursor is parked over the **target app's** window, the target's `WM_SETCURSOR` handler keeps re-asserting *its* arrow. Our `ShowCursor(FALSE)` does not suppress another process's class cursor.
- It cannot hide the arrow "globally" from a background/`NOACTIVATE` process. Practically it only reliably hides the cursor over windows the calling thread owns (or while that thread has mouse capture), which we don't have over the target.

Verdict: **No-go.** Does not hide the cursor over another process's focused window from our non-foreground overlay.

### A2. `SetCursor(NULL)` / transparent class cursor -- **NO-GO cross-process**

`SetCursor(NULL)` and a fully-transparent class cursor only affect **our own** windows' cursor, applied in **our** `WM_SETCURSOR`. The cursor is over the target's window, not ours, so this never fires for the pixels we care about. Same root cause as A1.

Verdict: **No-go** for hiding over the target. (Fine only for our own overlay surface, which is click-through anyway.)

### A3. `SetSystemCursor` with a transparent cursor -- **VIABLE (the only reliable global hide)**

`SetSystemCursor` "replaces the contents of the system cursor specified by *id*... and then destroys *hcur*" ([SetSystemCursor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setsystemcursor)). This swaps the **actual shared system cursor resource**, so it takes effect **desktop-wide, across every process**, regardless of who is foreground. Replace the relevant `OCR_*` ids (`OCR_NORMAL`, `OCR_IBEAM`, `OCR_HAND`, and any others a target might show over scrollable content) with a 1x1 fully-transparent cursor and the pointer becomes invisible everywhere for the mode's duration.

Key caveats / mechanics:
- Requires the `OEMRESOURCE` define before `windows.h` to use the `OCR_` constants (documented in Remarks).
- `hcur` is **destroyed** by the call; you cannot pass a `LoadCursor` handle directly -- `CopyCursor` a resource first, or build a transparent cursor with `CreateCursor`/`CreateIconIndirect`.
- It replaces only the specific ids you override. A target showing the I-beam or hand over its content still shows those unless you also blank `OCR_IBEAM`/`OCR_HAND`. Blank the full set you expect during scroll mode.
- **No elevation required** for a normal integrity process (works per the docs on desktop apps; it changes the current user's window-station cursors, not a protected resource).
- **Global side effect + restoration is mandatory:** it changes the cursor for *all* apps. Restore with `SystemParametersInfo(SPI_SETCURSORS, 0, NULL, SPIF_SENDCHANGE)`, which reloads the user's default system cursors in one call. This is the safety net for "we overrode every OCR_ id."

Verdict: **Viable / conditional.** It is the only documented way to make the pointer disappear over a foreign focused window from a non-foreground process. Cost: it is a global, heavy-handed swap that must be reliably restored (see Approach D).

---

## Approach B - Lock / confine the cursor

### B1. `ClipCursor(rect)` (e.g. pin to a 1px rect) -- **CONDITIONAL, and it fights the aimer**

`ClipCursor` "confines the cursor to a rectangular area on the screen. If a subsequent cursor position (set by `SetCursorPos` or the mouse) lies outside the rectangle, the system automatically adjusts the position to keep the cursor inside" ([ClipCursor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clipcursor)). Rect is in **screen coordinates**. Documented constraints:

- "The cursor is a shared resource. If an application confines the cursor, it must release the cursor by using `ClipCursor` before relinquishing control to another application."
- "The calling process must have `WINSTA_WRITEATTRIBUTES` access to the window station." (Normal desktop apps have this; no elevation needed.)
- **Not documented as focus-gated, but the OS clears it out from under you.** The clip is released on foreground-window changes, session/secure-desktop transitions (UAC prompt, `Ctrl+Alt+Del`, Win+L / lock), and monitor/resolution changes. Because our overlay is `NOACTIVATE` and the **target** holds foreground, a clip we set is fragile: any activation change silently drops it. This is a well-known behavior (games lose their clip on Alt-Tab) even though the reference page states only the "you must release it yourself" half.

**Interaction with aiming (critical):** pinning the cursor to a 1px rect *prevents our own `SendInput` absolute-move from relocating it* -- the system snaps any move back inside the rect. So a hard clip **breaks the park-to-region aiming**: we could no longer move the logical cursor onto the chosen region to land the wheel. To keep both, you would have to `ClipCursor(NULL)` -> move -> wheel -> re-clip on every actuation, which reintroduces movement and defeats the point.

Verdict: **Conditional and not recommended for the aim path.** Useful only if we abandon cursor-park aiming (i.e., aim the wheel some other way) and merely want to keep a hidden cursor from drifting. Even then it is fragile because we are not foreground.

### B2. Raw Input `RIDEV_CAPTUREMOUSE` / `RIDEV_NOLEGACY` -- **NO-GO for hide/lock**

`RIDEV_CAPTUREMOUSE`: "the mouse button click does not activate the other window. **RIDEV_CAPTUREMOUSE can be specified only if RIDEV_NOLEGACY is specified for a mouse device**" ([RAWINPUTDEVICE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputdevice)). It is about *receiving raw mouse deltas* and *suppressing click-to-activate*, not about hiding or confining the pointer:

- It does **not** hide the cursor and does **not** confine it. (Games that use it hide/recenter the cursor themselves.)
- Raw input by default is delivered only "as long as it has the window focus"; background delivery needs `RIDEV_INPUTSINK` (and a non-NULL `hwndTarget`). We are not foreground, so without `INPUTSINK` we get nothing.
- `RIDEV_NOLEGACY` suppresses `WM_MOUSE*` legacy messages **for the registering application**. It does not stop the target app from receiving its own wheel messages, so it buys us nothing for aiming, and misusing it would only blind us.

Verdict: **No-go** for the hide/lock goal. Wrong tool -- it is a capture-and-read mechanism for the foreground app, not a cursor-suppression or confinement mechanism.

---

## Approach C - Can we keep aiming the wheel while the cursor is hidden and/or locked?

Short answer: **Yes, if we hide via `SetSystemCursor` and do NOT hard-clip; the existing park-and-wheel aimer keeps working, now invisibly.** Detail per option:

- **(a) SendInput wheel targets the window under the *logical* cursor even while the image is hidden.** `SetSystemCursor` only changes the drawn bitmap; the logical cursor position and hit-testing are unchanged. Our `SendInput` absolute-move still repositions the logical cursor, the hover-enter still fires, and `MOUSEEVENTF_WHEEL` still lands on the window under that logical point. So we can still "park invisibly." This is the crux that makes hide-without-clip work.
- **(b) Directing a wheel at a coordinate/window without moving the cursor (`SendMessage(WM_MOUSEWHEEL)` with the point in `lParam`) is unreliable.** `WM_MOUSEWHEEL` "is sent to the focus window" and its `lParam` carries **screen** coordinates of the pointer ([WM_MOUSEWHEEL](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)). Posting it to an arbitrary child HWND with a synthetic point works for some classic Win32 controls but is routinely ignored by Chromium/Electron/Monaco surfaces, which scroll by internal hit-testing/hover rather than trusting the injected `lParam`. This matches prior prototype findings (`#61`/`#69`: hover-based wheel is the robust actuator; message injection is not). So we cannot rely on message-injection to avoid moving the cursor.
- **(c) Absolute-move while hidden still fires hover-enter.** Because hiding is only a bitmap swap, the move generates the same `WM_MOUSEMOVE`/hover-enter the target needs before accepting the wheel. The debounce we already use still applies.

**Conclusion:** keep the current park-aim actuator; hide the cursor globally so the park is invisible; do **not** clip to a pin (that would forbid the move the aimer depends on).

### VM caveat (unchanged by hiding)

On VMs, SendInput absolute-move often "doesn't stick" (the injected move is overridden by the guest/host pointer integration), so the wheel follows the *physical* mouse instead of our target point. Hiding via `SetSystemCursor` still works on a VM (it is a resource swap, independent of pointer motion), but **aiming still fails on a VM** because we cannot relocate the logical cursor. So hiding does not fix the VM aiming gap -- it is orthogonal. On a VM the honest outcome is: cursor hidden, but wheel lands wherever the physical pointer is; treat VM as a known-degraded matrix cell.

---

## Approach D - Restoration & robustness (mandatory)

Whatever we override, it is global and **must** be restored on every exit path (Esc, foreground-loss dismissal, kill, crash):

- **`SetSystemCursor`:** the override outlives our process if we die mid-mode -- the whole desktop would keep the invisible pointer. Restore with `SystemParametersInfo(SPI_SETCURSORS, 0, NULL, SPIF_SENDCHANGE)` (reloads default cursors) in a `finally`/SEH-guarded teardown, on process-exit handlers, and on the mode's foreground-loss dismissal. Consider a watchdog (separate thread/process) that restores if the main path is lost, given the user-visible severity of a stuck invisible cursor.
- **`ShowCursor` refcount balance (if ever used):** every `ShowCursor(FALSE)` must be matched by exactly one `ShowCursor(TRUE)`; the counter is per input-processing context and leaks silently. Given A1's verdict we should simply not use it.
- **`ClipCursor` (if used at all):** always `ClipCursor(NULL)` on exit. Note the OS may already have released it on a foreground/secure-desktop transition, so treat release as idempotent and also re-check on resume. Never assume the clip you set is still in force.
- **General:** scroll mode should have a single, guaranteed teardown that (1) `SPI_SETCURSORS` restore, (2) `ClipCursor(NULL)` if we ever clipped, (3) restore parked cursor position, run on all dismissal causes including WM_ (de)activation and unhandled-exception paths.

---

## Approach E - Win11 / security restrictions

- **UIPI (integrity).** A normal (medium-IL) process cannot `SendInput`/`SendMessage`-inject into a **higher-integrity (elevated)** window. If the focused target is elevated, our injected wheel and any `WM_MOUSEWHEEL` post are silently dropped -- aiming fails regardless of cursor hiding. `SetSystemCursor`/`ClipCursor` still function (they act on the window station, not the target), but the actuation won't reach an elevated target. Known limitation; document it.
- **Elevation.** None of `SetSystemCursor`, `ClipCursor`, or `ShowCursor` requires elevation for a normal desktop app. `ClipCursor` needs `WINSTA_WRITEATTRIBUTES` on the window station, which interactive desktop apps have.
- **Secure desktop / session transitions.** UAC prompts, `Ctrl+Alt+Del`, and lock (Win+L) switch to the secure desktop: our overlay is hidden, any `ClipCursor` is released, and input injection is blocked there. Our teardown must fire on these (`WM_ACTIVATEAPP`/session-change notifications) and restore the system cursors so the secure desktop / return is never left with an invisible pointer.
- **Per-monitor DPI / multi-monitor.** Absolute-move uses the 0..65535 normalized virtual-desktop coordinate space; compute the region content point in physical pixels with a per-monitor-DPI-aware process, or the park lands off-target on mixed-DPI setups. `WM_MOUSEWHEEL` `lParam` is screen coords -- use `GET_X_LPARAM`/`GET_Y_LPARAM`, never `LOWORD`/`HIWORD` (negative on multi-monitor).
- **No policy blocks `SetSystemCursor`/`ClipCursor`** for a standard interactive user; they are long-standing per-user window-station operations.

---

## Recommendation

For scroll mode, **hide the pointer globally with `SetSystemCursor` (transparent 1x1 cursor over the `OCR_` set) for the mode's duration, and keep the existing park-and-wheel aimer unchanged.** Hiding is only a bitmap swap, so the logical cursor still moves, hover-enter still fires, and the injected wheel still lands on the chosen region -- the borrow-and-restore jump simply becomes invisible, which is exactly the wart we wanted gone.

**Do not** rely on `ShowCursor`/`SetCursor(NULL)`/transparent-class-cursor: they only affect our own or foreground windows and cannot suppress the arrow the *target* process draws over its own window. **Do not** hard-`ClipCursor` to pin the cursor: it forbids the very move the aimer needs, and it is fragile because we are not foreground (the OS drops the clip on activation/secure-desktop changes). Raw-input `RIDEV_CAPTUREMOUSE` is the wrong tool -- it captures/reads deltas for a foreground app, it neither hides nor confines.

**Mandatory guardrails:** a single guaranteed teardown that calls `SystemParametersInfo(SPI_SETCURSORS, ...)` on every exit path (Esc, foreground-loss, crash, secure-desktop transition), ideally backed by a watchdog, because a leaked `SetSystemCursor` leaves the whole desktop with an invisible pointer. **Known gaps unchanged by this:** aiming still fails on VMs (SendInput move doesn't stick -> wheel follows the physical mouse) and against elevated targets (UIPI blocks injection); hiding works in both cases but does not fix aiming, so keep both as known-degraded matrix cells.

### Verdict table

| Approach | Hides over foreign focused window? | Confines? | Compatible with park-aim? | Verdict |
| --- | --- | --- | --- | --- |
| `ShowCursor(FALSE)` | No | - | n/a | **No-go** |
| `SetCursor(NULL)` / transparent class cursor | No (our windows only) | - | n/a | **No-go** (cross-process) |
| `SetSystemCursor` transparent | **Yes (global)** | No | **Yes** | **Viable** (must restore) |
| `ClipCursor` pin | No | Yes | **No** (blocks the move) + fragile (not foreground) | **Conditional / avoid** |
| Raw Input `RIDEV_CAPTUREMOUSE` | No | No | n/a | **No-go** |

## Sources

- [ShowCursor function (winuser.h)](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showcursor)
- [SetCursor function (winuser.h)](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcursor)
- [SetSystemCursor function (winuser.h)](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setsystemcursor)
- [ClipCursor function (winuser.h)](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clipcursor)
- [Using Cursors (Displaying / Confining a Cursor)](https://learn.microsoft.com/en-us/windows/win32/menurc/using-cursors)
- [RAWINPUTDEVICE (winuser.h) -- RIDEV_CAPTUREMOUSE / NOLEGACY / INPUTSINK](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputdevice)
- [RegisterRawInputDevices function (winuser.h)](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerrawinputdevices)
- [WM_MOUSEWHEEL message](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)
- [SystemParametersInfo (SPI_SETCURSORS to restore) reference](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)
