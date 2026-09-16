# Cursor-Free Scroll Injection: Reaching a Target Window/Region Without Moving the Mouse

**Research question:** How to inject scroll into the active target window — or a specific child region — so it reaches that target **regardless of the physical mouse cursor position, without moving the cursor**, robustly on **both a VM and bare-metal Windows**, across UI frameworks (Win32, WPF, UWP/WinUI, Chromium/Electron).

**Status:** Read-only research against high-trust primary sources (Microsoft Learn / MSDN Win32 + WinRT reference; Chromium source for the Electron/Chromium path). Where behavior is undocumented or app-dependent it is flagged inline. This document is the *targeting + VM-robustness* companion to [`smooth-scroll-mimicry.md`](smooth-scroll-mimicry.md) (which covers the wheel-plumbing/inertia physics); it does not repeat the `WM_MOUSEWHEEL` packing details established there.

---

## Bottom line up front

There is **no single API that delivers a wheel event to an arbitrary background window at a chosen point without touching either focus or the cursor** on a default Windows 11 install. The requirement is satisfiable, but only by picking a mechanism per surface. Ranked by how well each satisfies **cursor-independence + VM-robustness**:

| Rank | Mechanism | Cursor-free? | Point/window-addressable? | Engages app inertia? | VM-robust? | Works on which surfaces |
| --- | --- | --- | --- | --- | --- | --- |
| **1** | **UIA `IScrollProvider` (Scroll / SetScrollPercent)** | **Yes** (acts on the element) | **Yes** (the element) | No (positional jump) | **Yes** (message-based, no input pipeline) | Any surface exposing ScrollPattern: panes, UWP/WinUI, File Explorer, Settings, VS trees |
| **2** | **Synthetic touch injection (`InjectTouchInput`) driving a pan gesture** | **Yes** (separate pointer; does **not** move the mouse cursor) | **Yes** (screen coords, virtual-screen-relative) | **Yes** (DirectManipulation/InteractionTracker pan inertia) | **Mixed** — synthetic, hardware-independent, but VM digitizer/DPI quirks | Touch/pointer-aware surfaces: UWP/WinUI, Chromium/Electron, WPF w/ manipulation, DManip Win32 |
| **3** | **`SendMessage(WM_MOUSEWHEEL)` to a specific child HWND** | **Yes** | **Partly** (exact HWND, but lParam point may be re-hit-tested) | Usually no (bypasses pipeline) | **Yes** | Classic Win32 that handle wheel in WndProc; Chromium child HWND (fragile); **fails on WPF, UWP** |
| **4** | **`SendInput(MOUSEEVENTF_WHEEL)` + focus routing** (routing==FOCUS) | **Yes** *if* you can focus w/o cursor | No HWND; routed by focus **only when** `MOUSEWHEEL_ROUTING_FOCUS` | **Yes** | Sensitive (pipeline) | Anything, but only deterministic when the global routing setting is FOCUS |
| **5** | **`SendInput(MOUSEEVENTF_WHEEL)` under default routing** | **No** (needs cursor over target) | Cursor position | **Yes** | **Least** (VM cursor integration) | Anything the cursor is over |
| — | **UWP `InputInjector` (brokered)** | mouse-wheel: same routing as #4/#5; touch: same as #2 | — | — | — | **Bypasses UIPI** (can inject into elevated apps) but requires a *packaged* app + `inputInjectionBrokered` restricted capability |

**Design consequence:** the robust actuator is a **two-lane escalation** exactly matching the repo's existing ladder, plus one new lane:
1. **Lane A (cursor-free, reliable position):** UIA `Scroll`/`SetScrollPercent` when the target (or a selected descendant) exposes ScrollPattern. This is the *only* mechanism that is simultaneously cursor-free, window-addressable, and VM-robust — but it teleports and misses scrollbar-only surfaces.
2. **Lane B (cursor-free, native feel, NEW):** **synthetic touch pan** for touch/pointer-aware surfaces (Electron, WinUI, DManip Win32) — cursor-free and point-addressable, the best answer for the surfaces where UIA has no ScrollPattern but the app honors pointer/touch.
3. **Lane C (fallback):** wheel injection. Prefer `SendMessage(WM_MOUSEWHEEL)` to a resolved child HWND for classic Win32; fall back to `SendInput` wheel with **temporary focus or temporary routing=FOCUS** when nothing else reaches the target. This lane is where the cursor-independence guarantee is weakest.

Two mechanisms named in the brief are **dead ends for wheel**: `CreateSyntheticPointerDevice`/`InjectSyntheticPointerInput` support **only PT_TOUCH and PT_PEN** (no mouse, no wheel), and `WM_VSCROLL`/`WM_HSCROLL` posting is unreliable except on classic controls that actually implement it.

---

## 1. Pointer-injection APIs — can any deliver a wheel to a specific point/window without moving the cursor?

### 1a. `CreateSyntheticPointerDevice` / `InjectSyntheticPointerInput` — **touch/pen only, no wheel**

`CreateSyntheticPointerDevice`'s `pointerType` parameter: *"The pointer injection device type. Must be either **PT_TOUCH** or **PT_PEN**."* `InjectSyntheticPointerInput` is documented as *"Simulates pointer input (**pen or touch**)."* There is **no mouse pointer type and therefore no wheel** on this API. So it cannot deliver a wheel event directly.

What it *can* do that is relevant: the injected pointers' `ptPixelLocation` in `POINTER_TYPE_INFO` is *"specified relative to top left of the **virtual screen**"* — i.e. fully **point-addressable in screen space**, and being a pen/touch pointer it does **not** move the mouse cursor. So a **pen/touch pan gesture** (down → series of moves → up) at a chosen point is a cursor-free, point-addressable way to *scroll* on any surface that consumes pointer/touch input — see §1c. Min Windows 10 1809; desktop apps only.
Source: [CreateSyntheticPointerDevice](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createsyntheticpointerdevice), [InjectSyntheticPointerInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-injectsyntheticpointerinput)

### 1b. `InitializeTouchInjection` / `InjectTouchInput` — cursor-free, point-addressable touch (the pan path)

`InjectTouchInput` takes an array of `POINTER_TOUCH_INFO` where *"The screen coordinates of each contact must be within the bounds of the desktop"* — again **screen-coordinate addressable**, no HWND needed and no cursor movement (touch is a distinct pointer). *"The injected input is sent to the desktop of the session where the injection process is running."* The contract distinguishes **hover** vs **interactive** states via `pointerFlags`, and requires monotonically-increasing timestamps (`dwTime` ≥1 ms apart, or `PerformanceCount` ≥0.1 ms apart) — which is exactly the cadence knob you need to synthesize a **smooth pan**: `DOWN` at (x,y) → a train of `UPDATE` frames walking y → `UP`. A moving contact is what DirectManipulation/InteractionTracker integrate into pan **inertia** (see [`smooth-scroll-mimicry.md`](smooth-scroll-mimicry.md) §3). Min Windows 8.
Source: [InjectTouchInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-injecttouchinput), [InitializeTouchInjection](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-initializetouchinjection)

**Why this is the strongest cursor-free path for scrollbar-only modern surfaces:** it is point-addressable *and* cursor-free *and* engages the app's real inertia engine — the only mechanism that hits all three. Its limits: (a) the target must handle touch/pointer pan — classic Win32 scrollbar-only apps (Beyond Compare's detached scrollbar) and the VS WPF editor generally will **not** pan from touch; (b) a touch contact is a *click-through* gesture — a stray tap can hit a button, so the pan must start on scrollable content and stay in-contact; (c) VM caveats in §6.

### 1c. UWP `Windows.UI.Input.Preview.Injection.InputInjector` — brokered, **UIPI-exempt**, but packaged-app-only

`InputInjector` exposes `InjectMouseInput` (wheel via `InjectedInputMouseOptions.Wheel` + `MouseData`, where `WHEEL_DELTA`=120, so `MouseData = -360` is three detents backward), plus `InjectTouchInput`, `InjectPenInput`, `InjectKeyboardInput`, and `InjectShortcut`. Two decisive facts:

- **It bypasses UIPI/integrity limits.** *"Input injection enables your Windows application to simulate input from a variety of input devices and **direct that input anywhere, including outside your app's client area (even to apps running with Administrator privileges, such as the Registry Editor)**."* This is the one documented way to inject into an **elevated** target from a non-elevated tool — `SendInput` cannot (it is capped at equal-or-lesser integrity, silently). The tradeoff: it requires the `inputInjectionBrokered` **restricted capability** and therefore a **packaged (MSIX) app** — a non-trivial constraint for a Win32/.NET tool, and restricted-capability packages need special provisioning to sideload/publish.
- **Targeting is the same as the raw pipeline.** `InjectMouseInput` mouse *wheel* still has no window parameter; `DeltaX`/`DeltaY` are *"the change in the x-coordinate value of the **mouse cursor**"* — i.e. mouse injection **moves the cursor** (relative, or absolute with `Move|Absolute|VirtualDesk`). So the mouse-wheel path here inherits the focus/hover routing of §2 and is **not** cursor-free unless routed by focus. Its `InjectTouchInput` is the same cursor-free pan path as §1b, just from a packaged app.
Source: [InputInjector class](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.preview.injection.inputinjector), [Simulate user input through input injection](https://learn.microsoft.com/en-us/windows/uwp/design/input/input-injection), [InjectedInputMouseInfo.DeltaX](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.preview.injection.injectedinputmouseinfo.deltax), [InjectMouseInput](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.preview.injection.inputinjector.injectmouseinput)

**Verdict for §1:** For *wheel*, none of these three inject cursor-free to an arbitrary window (synthetic-pointer has no wheel; InputInjector wheel routes like the pipeline). For *scrolling*, **touch injection** (either `InjectTouchInput` Win32 or `InputInjector.InjectTouchInput`) **is** a cursor-free, point-addressable, inertia-engaging path — for surfaces that honor touch. Only `InputInjector` clears UIPI/elevation.

---

## 2. Focus-routed wheel — and the queryable routing setting

Yes: `WM_MOUSEWHEEL` is documented as going to the **focus window**. But since Windows 10 the effective route is governed by a **user setting** that you can both **query and set** via `SystemParametersInfo`:

- **`SPI_GETMOUSEWHEELROUTING` (0x201C)** / **`SPI_SETMOUSEWHEELROUTING` (0x201D)** read/write a `DWORD` routing option:
  - `MOUSEWHEEL_ROUTING_FOCUS` (**0**) — wheel goes to the **focus/foreground** window.
  - `MOUSEWHEEL_ROUTING_HYBRID` (**1**) — focus for desktop apps, under-pointer for Store apps (no longer offered in Settings).
  - `MOUSEWHEEL_ROUTING_MOUSE_POS` (**2**) — wheel goes to the window **under the pointer**. **This is the Windows 10+ default** ("Scroll inactive windows when I hover over them" = On).
- The same state is the registry value `HKCU\Control Panel\Desktop\MouseWheelRouting` (`0` = focus-only, `2` = under-pointer).
Source: [SystemParametersInfoA (SPI_GET/SETMOUSEWHEELROUTING)](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfoa)

**Implications for a deterministic, cursor-free wheel:**
- **The tool can detect the setting** (`SPI_GETMOUSEWHEELROUTING`) and branch: if it reads `FOCUS` (0), then simply **giving the target foreground/focus** (`SetForegroundWindow`) makes `SendInput` wheel land on it deterministically **without any cursor move**. That is the clean cursor-free wheel path — but only under a non-default setting.
- **Under the default `MOUSE_POS` (2), focus does not help** — the wheel follows the cursor, so cursor-free wheel injection is *not* deterministic. Options: (a) temporarily `SPI_SETMOUSEWHEELROUTING` → FOCUS, inject, restore (global, intrusive, and racy if the user scrolls meanwhile); (b) fall back to a non-wheel lane (UIA / touch).
- **There is no documented per-process opt-out** of hover routing, and no documented per-thread API to force wheel to a chosen window while leaving the global setting alone. `SetFocus`/`SetForegroundWindow` only matter when routing is FOCUS. So "aim wheel at a background window without touching cursor or the global setting" is **not achievable** via documented APIs.

---

## 3. Targeting a specific child window

### Resolving the child that owns the scroll
- **`WindowFromPoint`** returns the window under a screen point but **ignores disabled/hidden and does not descend past the top hittable window** in all cases.
- **`ChildWindowFromPointEx`** searches immediate children of a given parent with skip flags (`CWP_SKIPINVISIBLE`, `CWP_SKIPDISABLED`, `CWP_SKIPTRANSPARENT`).
- **`RealChildWindowFromPoint`** *"Retrieves a handle to the child window at the specified point. The search is restricted to **immediate child windows**; grandchildren and deeper descendant windows are not searched."* It differs from `ChildWindowFromPoint` in `HTTRANSPARENT` handling: it *"returns the child window **behind** a transparent part of a control"* (e.g. behind a groupbox) — which is usually what you want when picking the real scroll container. Both need **client** coordinates of the parent.
Source: [RealChildWindowFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-realchildwindowfrompoint)

**These only help on multi-HWND Win32 apps.** WPF, UWP/WinUI, and Chromium render their whole content as **one HWND** (WPF: a single `HwndSource`; Chromium: content is drawn by the renderer, with at most a `Chrome_RenderWidgetHostHWND` legacy child). On those, child-window resolution "bottoms out" at the top content HWND and you **cannot address a sub-region by HWND** — you must use UIA (which sees the logical element tree) or a point-addressed gesture (touch/wheel-with-coords). This is the fundamental split the tool must detect per surface.

### Posting `WM_MOUSEWHEEL` to the resolved child HWND
`SendMessage(childHwnd, WM_MOUSEWHEEL, …)` is cursor-free and HWND-addressable, and works on classic controls whose `WndProc` handles wheel. Caveats (see §4/§5): the `lParam` screen point is **advisory** — frameworks that re-hit-test (WPF) or route through DirectManipulation may ignore it or ignore the message entirely.

### `WM_VSCROLL(SB_LINEDOWN)` / `WM_HSCROLL` — unreliable as a general scroll
`WM_VSCROLL` is *"sent to a window when a scroll event occurs in the window's **standard vertical scroll bar**… also sent to the **owner** of a vertical scroll bar control."* Its `LOWORD(wParam)` carries `SB_LINEUP`/`SB_LINEDOWN`/`SB_PAGEUP`/`SB_PAGEDOWN`/`SB_THUMBPOSITION`/…; if sent by a scrollbar control `lParam` is that control's HWND, else NULL. **It only scrolls if the target actually implements `WM_VSCROLL` to move content** — the docs note *"If an application scrolls the content of the window, it must **also** reset the position of the scroll box with `SetScrollPos`,"* i.e. scrolling is entirely the app's responsibility, not the OS's. Many modern apps draw **custom** scrollbars and never process `WM_VSCROLL` (matches the empirical Beyond Compare finding). Extra caveat: `WM_VSCROLL` carries only **16 bits** of thumb position (max 65,535), so `SB_THUMBPOSITION` cannot address long documents precisely. **Conclusion: `WM_VSCROLL` posting is a niche fallback for classic scrollbar-owning windows, not a general mechanism.**
Source: [WM_VSCROLL](https://learn.microsoft.com/en-us/windows/win32/controls/wm-vscroll)

---

## 4. WPF specifics — why cursor-free wheel into WPF content is effectively impossible from outside

A WPF top-level window is a single `HwndSource` HWND. When a `WM_MOUSEWHEEL` arrives, WPF does **not** trust the message's `lParam` coordinates; it raises the routed `MouseWheel` event using WPF's **own** notion of the element under the pointer (`Mouse.DirectlyOver`), which is driven by WPF's internal pointer tracking (the live cursor), and routing is further overridden by focus/capture. The WPF docs state for the wheel event: *"**Focus or mouse capture take precedence over where the mouse pointer is**; therefore, if you receive this event from a focused or captured element, the mouse pointer might in fact be over another element."* And `Mouse.DirectlyOver` *"gets the element the mouse pointer is directly over."*
Source: [Mouse.PreviewMouseWheel / MouseWheel remarks](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.mouse.previewmousewheel), [Mouse.DirectlyOver](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.mouse.directlyover), [WPF Input Overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/input-overview)

**Net effect:** a posted `WM_MOUSEWHEEL` with fabricated coordinates is re-hit-tested against the *real* cursor/focus, so it scrolls whatever the cursor is actually over — **the injected point is discarded**. There is **no documented way to deliver a routed `MouseWheelEvent` to a chosen WPF element from another process** (WPF's routed-event tree is in-process; UIA is the only cross-process door WPF opens, and it exposes `ScrollPattern`, not wheel). **Confirmed: cursor-free wheel into a specific WPF region is not possible short of (a) UIA `ScrollPattern`, or (b) parking/warping the cursor over the region.** For the VS *editor* (WPF, scrollbar-only, no usable ScrollPattern per the prototype), that leaves cursor-park as the only content-moving wheel path — which the brief rules out as intrusive. Practical answer for VS editor: prefer UIA where any scroll element exists, else accept cursor-park or a keyboard-key surrogate (PageDown) as the fallback.

---

## 5. Chromium/Electron specifics (VS Code/Monaco, Slack)

Chromium on Windows registers itself as a **DirectManipulation consumer** and forwards `WM_MOUSEWHEEL`/`WM_MOUSEHWHEEL` into `IDirectManipulationManager::ProcessInput` on the UI thread (the `DirectManipulationHelper` on `LegacyRenderWidgetHostHWND`). Two consequences:
- A **posted `WM_MOUSEWHEEL`** to the Chromium content child HWND (`Chrome_RenderWidgetHostHWND` / "Chrome Legacy Window") *can* be picked up because Chromium does process the message — this is why direct-post wheel sometimes works on Electron where it fails on WPF. But it is **fragile**: DirectManipulation is designed around **pipeline** pointer input, the child HWND name/hierarchy is an implementation detail, and high-precision-touchpad/DManip paths may ignore a synthetic message that lacks real pointer state.
- The **robust cursor-free path for Electron is synthetic touch pan** (§1b): Chromium consumes pointer/touch and routes it through DirectManipulation to produce native inertial scrolling, and touch is point-addressable + cursor-free. This aligns with the prototype's "injected-wheel fallback for no-signal apps (Monaco/Slack)" — but touch pan is a cleaner cursor-free variant than pipeline wheel because it does not depend on cursor position.
Source (Chromium is not MS-primary, but is the authoritative implementation): [IDirectManipulationManager::ProcessInput](https://learn.microsoft.com/en-us/windows/win32/api/directmanipulation/nf-directmanipulation-idirectmanipulationmanager-processinput), [Chromium `legacy_render_widget_host_win.h`](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/content/browser/renderer_host/legacy_render_widget_host_win.h), [Chromium DManip scroll patch (crrev 1283913002)](https://codereview.chromium.org/1283913002)

**Flagged as app-dependent/undocumented:** Electron/Chromium expose **no UIA ScrollPattern** for the web content (the DOM scroller is not surfaced as a UIA scroll provider in the general case), so Lane A does not apply; and whether a given Electron build honors direct-post `WM_MOUSEWHEEL` vs only pipeline input must be verified per app/version. Treat "does SendMessage wheel move Monaco/Slack?" as an empirical per-version fact, not a guarantee.

---

## 6. VM input caveats — why injected input behaves differently, and what is most VM-robust

VMs replace the physical mouse with a **guest-additions absolute pointing device** so the host and guest cursors stay in sync. VirtualBox's own docs describe this: with Guest Additions the guest exposes an **"Absolute Pointing Device,"** and *"the guest and host must agree about which screen positions correspond to which positions on the guest screens"* for integration to work; the additions device *"sends position information but not button information"* and coordinates with a second device. Hyper-V enhanced session and RDP similarly deliver **absolute**-mode pointer input. (These are vendor sources, not MS-primary — flagged.)
Source: [VirtualBox: Investigating problems with mouse input](https://www.virtualbox.org/wiki/MouseInput), [VirtualBox IMouse interface](https://www.virtualbox.org/sdkref/interface_i_mouse.html)

Why injected input differs under a VM, and the robustness ranking:
- **`SetCursorPos` / relative `SendInput` move drift.** When the guest runs in absolute-pointer mode, relative-motion (`MOUSEEVENTF_MOVE` without `MOUSEEVENTF_ABSOLUTE`) and `SetCursorPos` can land in the wrong place or fight the host's absolute stream, because the guest is being fed absolute host coordinates simultaneously. If you must warp the cursor, use **absolute** coordinates (`MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`, normalized to 0–65535 across the virtual desktop) — but note the whole point is to *avoid* moving the cursor.
- **First-event priming / latency.** The first injected event after idle can be swallowed or delayed while the guest pointer integration "wakes"; injectors often send a priming event. This makes any **cursor-position-dependent** wheel (`SendInput` under `MOUSE_POS` routing) the **least** VM-robust: the cursor may not be where you think at the instant the wheel fires.
- **DPI / coordinate scaling.** Guest display scaling and multi-monitor virtual-screen layout change how screen coordinates map, affecting `InjectTouchInput`/`InjectSyntheticPointerInput` `ptPixelLocation` and any hit-test. Use the **virtual-screen** origin consistently (both APIs specify virtual-screen-relative coordinates).

**Most VM-robust mechanisms** (no dependence on the guest cursor pointer):
1. **UIA `IScrollProvider`** — pure cross-process messaging, no input pipeline, no cursor, no coordinate mapping. Immune to guest-additions pointer quirks. **Most VM-robust.**
2. **`SendMessage(WM_MOUSEWHEEL)` / `WM_VSCROLL` to an HWND** — message-based, no cursor; robust where the app honors it.
3. **Synthetic touch pan** — hardware-independent (works with no digitizer) and cursor-free, but subject to DPI/coordinate mapping and possible guest timestamp sensitivity; verify per hypervisor.
4. **`SendInput` wheel with focus routing** — depends on focus, not cursor; moderately robust if routing==FOCUS.
5. **`SendInput` wheel under default routing / cursor-park** — depends on the guest cursor being exactly positioned; **least robust**, most VM-sensitive.

---

## 7. Automated test harness — verifying a target *actually scrolled* (not just thumb-moved)

### Verification strategy (the hard part)
The prototype already learned that a moved thumb ≠ moved content (VS editor `RangeValue`). So the oracle must read **content position**, with a tiered approach per surface:

- **Tier 1 — UIA scroll read (authoritative where present).** Before/after read of `ScrollPattern.VerticalScrollPercent` (and `VerticalViewSize`) via `IUIAutomation` `GetCurrentPropertyValue`. A genuine scroll changes `VerticalScrollPercent`. This is the ground truth for File Explorer, Settings, VS trees, and any ScrollPattern surface.
- **Tier 2 — `RangeValue` read, with the thumb-vs-content trap.** Where only a scrollbar `RangeValuePattern` exists, read `Value` before/after — but **cross-check against a pixel diff** because the VS-editor case proved `RangeValue` can move without content moving. Treat Tier 2 as *corroborating*, never sole.
- **Tier 3 — pixel diff (Electron and any no-read surface).** Capture a fixed sub-rect of the target's client area with the DWM-accurate path (`PrintWindow` with `PW_RENDERFULLCONTENT` = 0x2, which captures DWM/DirectComposition surfaces that plain BitBlt misses) or a Graphics.CopyFromScreen fallback, before and after. Compare with a cheap metric (per-channel MAD, or a downsampled perceptual hash). A scroll shows a **vertical shift** of content; a null-op shows near-zero diff. To distinguish *scrolled* from *merely changed*, optionally cross-correlate the two captures vertically and assert the best-match offset is nonzero and in the expected direction. This is the only oracle for Monaco/Slack.
- **Cursor-free assertion (every case).** Snapshot `GetCursorPos()` immediately before and after the actuation and assert **zero movement** (the core requirement). Also snapshot the foreground window (`GetForegroundWindow`) if the lane is not allowed to steal focus.

### Deterministic launch/positioning
- Launch each app by known path; for UWP/WinUI (Settings) launch via URI (`start ms-settings:`) or `IApplicationActivationManager`. Wait for the top-level HWND via UIA/`FindWindow` with a timeout.
- Move/size deterministically with `SetWindowPos` to a fixed rect (e.g. 1200×800 at a fixed origin on the virtual screen) so captured sub-rects and injected screen coordinates are stable across runs and between VM/bare-metal.
- Record the resolved scroll target: the UIA element (Tier 1/2) and/or the child HWND (`RealChildWindowFromPoint` on the parent's client point) so the same target is re-addressed each iteration.

### Harness shape (C#/.NET)
```
ScrollHarness (net8.0-windows, <UseWindowsForms/WPF>false, P/Invoke)
├─ Launchers        : start app, wait-for-HWND, SetWindowPos to fixed rect
├─ TargetResolver   : screen point → { UIA element (ScrollPattern/RangeValue?), child HWND }
├─ Actuators        : IScrollActuator implementations, one per lane
│     UiaScroll      (SetScrollPercent / Scroll)          // Lane A
│     TouchPan       (InitializeTouchInjection + frames)  // Lane B
│     WheelMessage   (SendMessage WM_MOUSEWHEEL → HWND)   // Lane C1
│     WheelPipeline  (SendInput MOUSEEVENTF_WHEEL, +focus/routing toggle) // Lane C2
│     VScrollMessage (WM_VSCROLL SB_LINEDOWN → HWND)      // niche
├─ Oracles          : UiaScrollRead | RangeValueRead | PixelDiff(PrintWindow PW_RENDERFULLCONTENT)
├─ Assertions       : contentMoved(expectedDir) && cursorUnmoved && (focusUnchanged?)
└─ Matrix runner    : {app × actuator × oracle} → pass/fail + VM/bare-metal expectation, JSON/CSV out
```
Iterate quickly by running the **whole matrix** per app and printing a grid; a lane that fails the `cursorUnmoved` or `contentMoved` assertion is disqualified for that surface. Run the identical matrix inside the VM and on bare metal; **divergence is the finding.**

### Proposed test matrix
Legend: **A**=UIA scroll, **B**=touch pan, **C1**=`SendMessage` wheel→HWND, **C2**=`SendInput` wheel (+focus). Oracle in parentheses. Expectation is the *cursor-free* result.

| Surface | Framework | Expected working lane(s) | Oracle | Bare-metal expectation | VM expectation | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| **File Explorer** list | Win32 + UIA | **A** (also C1) | UIA % | A passes | A passes | Baseline pane; ScrollPattern present |
| **Settings** page | UWP/WinUI | **A**; **B** | UIA % | A passes | A passes; **B may need digitizer/DPI tweaks** | Pane exposes ScrollPattern per prototype |
| **VS Solution Explorer** | WPF tree | **A** | UIA % | A passes | A passes | Single HWND; UIA tree is the only cursor-free door |
| **VS editor** | WPF, scrollbar-only | **none cursor-free** (A absent, C fails/needs cursor-park) | PixelDiff + RangeValue cross-check | fails cursor-free → fallback = keyboard/PageDown or cursor-park | same | Confirms §4; RangeValue moves thumb-only |
| **Beyond Compare** | native, detached scrollbar | C1/VScroll **iffy**; **A** if any pattern; else pixel-diff only | PixelDiff | likely fails wheel/VScroll | same | Custom scrollbar ignores WM_VSCROLL (prototype) |
| **VS Code / Monaco** | Electron/Chromium | **B** (touch pan); C1 fragile | PixelDiff (PW_RENDERFULLCONTENT) | B passes; C1 per-version | **B: verify DManip under VM** | No UIA ScrollPattern for content |
| **Slack** | Electron/Chromium | **B**; C1 fragile | PixelDiff | B passes | **B: verify** | Same as Monaco |

**Key assertions to encode:** (1) `cursorUnmoved` for every passing lane; (2) `contentMoved` proven by Tier-1 UIA % *or* Tier-3 pixel shift, **never** Tier-2 RangeValue alone; (3) for lanes that must not steal focus, `focusUnchanged`; (4) a **VM-vs-bare-metal delta column** so the harness surfaces exactly which mechanism degrades under virtualization.

---

## What can and cannot satisfy "cursor-independent + VM-robust"

**Can (cursor-free AND VM-robust):**
- **UIA `IScrollProvider.Scroll`/`SetScrollPercent`** — the only mechanism that is cursor-free, window/element-addressable, and immune to VM pointer integration. Limited to ScrollPattern surfaces; teleports (no inertia).
- **`SendMessage(WM_MOUSEWHEEL)` / `WM_VSCROLL` to a resolved HWND** — cursor-free and VM-robust *where the target honors the message* (classic Win32; Chromium child HWND is fragile; **WPF/UWP do not**).

**Can (cursor-free, native feel) but VM-caveated:**
- **Synthetic touch pan (`InjectTouchInput`)** — cursor-free, point-addressable, engages real inertia; best for Electron/WinUI scrollbar-only surfaces. Must validate DPI/coordinate/timestamp behavior per hypervisor. Only `InputInjector`'s touch clears UIPI (packaged-app requirement).

**Cannot satisfy the requirement:**
- **`SendInput(MOUSEEVENTF_WHEEL)` under default `MOUSEWHEEL_ROUTING_MOUSE_POS`** — needs the cursor over the target; most VM-sensitive. Becomes deterministic only if you toggle routing to FOCUS (global, intrusive) or park the cursor.
- **`CreateSyntheticPointerDevice`/`InjectSyntheticPointerInput`** — no mouse/wheel at all (touch/pen only).
- **Cursor-free routed wheel into a specific WPF or UWP element from another process** — not possible; UIA `ScrollPattern` or cursor-park are the only content-moving options for WPF scrollbar-only surfaces (VS editor).

---

## Open / undocumented / app-dependent items (flagged)
- **No per-process/per-thread wheel-routing override** is documented; only the global `SPI_*MOUSEWHEELROUTING`. "Aim wheel at a background window without cursor or global-setting changes" is unachievable via documented APIs.
- **Electron/Chromium honoring direct-post `WM_MOUSEWHEEL`** is version- and DManip-path-dependent; verify empirically per app/build. No UIA ScrollPattern for web content in the general case.
- **Touch injection under VMs** (DPI scaling, absolute-pointer coexistence, timestamp cadence) is not documented by Microsoft for the VM case — must be measured on each hypervisor (Hyper-V enhanced session, VirtualBox, VMware).
- **WPF wheel re-hit-testing against the live cursor** is WPF-internal behavior inferred from the routed-event/`Mouse.DirectlyOver` contract; it is not spelled out as "posted lParam is ignored" on a single reference page. Behavior is consistent with the prototype's empirical finding.
- **VM pointer-integration facts** are from hypervisor-vendor docs (VirtualBox), not Microsoft primary sources.

## Primary sources
- [CreateSyntheticPointerDevice](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createsyntheticpointerdevice) · [InjectSyntheticPointerInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-injectsyntheticpointerinput)
- [InjectTouchInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-injecttouchinput) · [InitializeTouchInjection](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-initializetouchinjection)
- [InputInjector class](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.preview.injection.inputinjector) · [Simulate user input through input injection](https://learn.microsoft.com/en-us/windows/uwp/design/input/input-injection) · [InjectMouseInput](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.preview.injection.inputinjector.injectmouseinput) · [InjectedInputMouseInfo.DeltaX](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.preview.injection.injectedinputmouseinfo.deltax)
- [SystemParametersInfoA (SPI_GET/SETMOUSEWHEELROUTING)](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfoa)
- [WM_MOUSEWHEEL](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel) · [WM_VSCROLL](https://learn.microsoft.com/en-us/windows/win32/controls/wm-vscroll)
- [RealChildWindowFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-realchildwindowfrompoint) · [ChildWindowFromPointEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-childwindowfrompointex) · [WindowFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-windowfrompoint)
- [Mouse.PreviewMouseWheel](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.mouse.previewmousewheel) · [Mouse.DirectlyOver](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.mouse.directlyover) · [WPF Input Overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/input-overview)
- [IDirectManipulationManager::ProcessInput](https://learn.microsoft.com/en-us/windows/win32/api/directmanipulation/nf-directmanipulation-idirectmanipulationmanager-processinput) · [Chromium legacy_render_widget_host_win.h](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/content/browser/renderer_host/legacy_render_widget_host_win.h)
- [PrintWindow (PW_RENDERFULLCONTENT)](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow)
- [VirtualBox: MouseInput](https://www.virtualbox.org/wiki/MouseInput) · [VirtualBox IMouse](https://www.virtualbox.org/sdkref/interface_i_mouse.html) *(vendor, non-MS)*
- IScrollProvider / ScrollPattern: see [`smooth-scroll-mimicry.md`](smooth-scroll-mimicry.md) §6 for the primary-source citations.
