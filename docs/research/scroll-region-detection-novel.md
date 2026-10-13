# Scroll-region detection: mechanisms the project is not yet using

**Status:** research findings only. No code was changed and nothing was benchmarked.
**Date:** 2026-10-13
**Question:** What mechanisms exist for identifying scrollable regions on the Windows desktop (OCR, AI/ML, or anything else) that Windows-Hinting is **not** already accounting for?
**Method:** primary sources only: Microsoft Learn, first-party source code (Chromium, microsoft/terminal, qt/qtbase, microsoft/vscode, Vimium, Rango), the Chrome DevTools Protocol definitions, Oracle's Java docs, and the papers themselves. Commit SHAs are given where source code is quoted, so the claims can be re-checked. Anything inferred rather than read is marked **(inference)**, and anything that needs a hands-on probe is marked **(probe)**.

---

## Bottom line

1. **The most useful new idea is passive learning from the user's own scrolling.** The project already has the parts. A low-level mouse hook sees every real wheel event and its screen point. Frame dirty rects show which rectangle then changed on screen. Chromium already fires a WinEvent and a UIA property-changed event on every real scroll. Together these can learn per-app scroll regions over time, with no probing and nothing moving. It is the only new mechanism that covers the no-signal apps (Monaco, Slack, the embedded terminal) without scrolling them.
2. **Chromium tells you when a real scroll container scrolls, with no polling.** On `SCROLL_VERTICAL_POSITION_CHANGED`, Chromium fires `EVENT_SYSTEM_SCROLLINGEND` and a UIA `VerticalScrollPercent` property-changed event on the scrolled node. That gives a free "the user just scrolled *this* element" signal. It covers only the same set of containers that `ScrollPattern` already covers, so its value is ranking and default-target choice, not new coverage.
3. **For Electron and WebView2, the DevTools Protocol gives exact answers, but only if the app was launched with a debug port.** `DOMSnapshot.captureSnapshot(includeDOMRects)` returns each node's `scrollRects` and `clientRects`. Overflow is `scrollHeight > clientHeight`, which is Rango's rule. A framework selector finds what DOM overflow cannot: Monaco's scroller is a `div.monaco-scrollable-element` with `overflow: hidden` and a virtual `Scrollable`. The gate is deployment. Chrome 136+ ignores `--remote-debugging-port` on the default profile. Electron honours the switch. WebView2 accepts it through `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`.
4. **Qt's Windows UIA provider implements no `ScrollPattern` at all.** Qt apps can only take the existing ScrollBar → `RangeValue` rung. This is a fact for the per-app profile, not a new mechanism.
5. **DXGI Desktop Duplication move rects are a real OS-level "this rectangle scrolled by (dx,dy)" signal, but apps rarely feed it.** The source is the app's own `Present1` scroll rect. Windows Terminal's AtlasEngine sets one. Searches of the GitHub mirrors of Chromium, WinUI (`microsoft-ui-xaml`), WPF and Gecko found no `pScrollRect`. Whether DWM turns an app's scroll rect into a DDA move rect is undocumented **(probe)**. Treat it as an opportunistic corroborator, not a detector.
6. **AI/vision adds little beyond the baseline.** One new item: ScreenParse (2026) has a "Scroll" class in a 55-class, web-centric taxonomy. It runs at about 276 ms on an H100 and the class is undefined, so it belongs in Tier 3 only. Apple's Screen Recognition has a "Container" class but no scroll class.
7. **Dead ends:** DirectManipulation viewports and `InteractionTracker` can't be queried from outside the process. `DM_POINTERHITTEST` carries no data. The `WM_MOUSEWHEEL` return value can't tell "handled" from "passed to parent". The CDP Accessibility domain has no scroll property. Windows.Graphics.Capture reports dirty regions only, not moves.

---

## Already accounted for

From `docs/research/uia-scroll-signals.md`, `uia-scroll-prior-art.md`, `cv-clickable-scrollable-detection.md` and `ocr-ai-region-detection.md` (each on its `research/*` branch), the `prototype/scroll-exposure-probe` docs (`cursor-free-scroll-injection.md`, `smooth-scroll-mimicry.md`, `cursor-hide-lock-scroll-mode.md`), and maps #54 and #62 (closed children #57–#61, #63–#67, #88–#92):

- **UIA `ScrollPattern`:** `*Scrollable`, `ViewSize`, `ScrollPercent`, the `−1` sentinel, and `IsScrollPatternAvailable`. `ScrollItemPattern` is a weak hint. A ScrollBar child with `RangeValue` is the fallback rung. `IsOffscreen` corroborates. For terminals and documents, `TextPattern` visible range vs document range.
- **Chromium gating:** scroll attributes exist only for real Blink scroll containers. The tree is built lazily (needs warm-up or a forced build). Native UIA arrived in Chrome 138, and the legacy MSAA→UIA proxy is used before that. Virtualized lists report a bogus extent.
- **The census and prototype (#59, #61):** 9 of 11 surfaces can be scrolled through two UIA paths. Monaco, Slack and the embedded `HwndTerminal` fail. A multi-factor selection gate. A container-type gate. Scrollbar suppression. Child-HWND fragment descent. An Electron warm-up retry. A ground-truth step that scrolls and restores via `SetScrollPercent`. An actuation ladder with a `MovedSince` verify. `ElementFromPoint` at the window centre (used for actuation, not detection). The injected-wheel fallback.
- **Per-app profile = the window rule, grown (#67 and amendments):** sections `scrollDetection` and `scrollActuation`, `expectedRegions` selectors, plus the `url` and `appId` matchers.
- **Pixels:** scrollbar-affordance detection (a corroborator only). Probe-and-diff with tiled `phaseCorrelate` or DIS optical flow. Wu et al.'s UIST 2023 drag-and-template-match. ShareX scrolling capture (`SendMouseWheel` plus `GetScrollInfo`). The AMEX, Rico, WebUI and SwipeGen datasets. VLM inference of scroll regions. The note that no released pixel model labels "scrollable". An untested heuristic for OCR text clipped at a pane edge. BitBlt vs WGC vs DDA as capture paths, including the fact that DDA and WGC have dirty rects.
- **Actuation:** `SendInput` vs `SendMessage(WM_MOUSEWHEEL)`. Touch-injection pan. `WM_VSCROLL`. The DirectManipulation and InteractionTracker inertia paths. Cursor park and hide.
- **Prior art:** Vimium's `findScrollableElement` (UX analogue), Voice Access, Talon, AHK UIA-v2, FluentSearch.

Everything below is new relative to that list, or adds a fact that changes how an item on it should be used.

---

## Summary table of new mechanisms

| # | Mechanism | Detects | Coverage | Cost / latency | Moves content? | .NET / Win32 path | Maturity |
|---|---|---|---|---|---|---|---|
| 1 | **Passive user-wheel learning** (LL hook + dirty rects + scroll events) | Region rect and axis. Position only when combined with UIA | **All apps**, including Monaco, Slack and `HwndTerminal` | Always-on hook (<1 ms per event); one capture or diff per wheel burst; learns over days | **No** (observes the user) | `SetWindowsHookEx(WH_MOUSE_LL)` or Raw Input; WGC `DirtyRegions` (24H2) or DDA dirty rects; WinEvent hook | Parts are stable OS APIs; the combination is new **(inference)** |
| 2 | **Scroll WinEvents + UIA scroll-percent change events** | Which element just scrolled, plus axis | Chromium real containers; standard Win32 scrollbars; any UIA provider that raises the event | Event-driven, near zero | No | `SetWinEventHook(EVENT_SYSTEM_SCROLLINGSTART..END, EVENT_OBJECT_CONTENTSCROLLED)` + `AccessibleObjectFromEvent`; `IUIAutomation::AddPropertyChangedEventHandler` | Stable; Chromium behaviour confirmed in source and tests |
| 3 | **CDP DOM introspection** (`DOMSnapshot` / `Runtime.evaluate`) | Exact scroll containers, extent, position, axis; framework scrollers by selector | Electron apps and WebView2 hosts launched with a debug port; Chrome for Testing or non-default profiles | One snapshot: tens of ms **(probe)** | No | WebSocket JSON to `http://127.0.0.1:<port>/json`; `System.Net.WebSockets` | Stable protocol; deployment-gated |
| 4 | **Browser extension + native messaging** (Rango/Vimium-style) | Exact DOM scroll containers; Vimium's ±1 px self-restoring probe | Chrome/Edge tabs (and PWAs **(probe)**) on the user's real profile | In-page JS, about 1 ms per element | Vimium probe moves 1 px and back; Rango's rule doesn't move anything | Native-messaging host (stdin/stdout JSON) bridged to our pipe | Mature prior art (Rango, Vimium); we'd ship an extension |
| 5 | **`ItemContainerPattern` as a container corroborator** | "This is a (possibly virtualized) item container" | UIA lists, grids and trees (XAML, WinForms, Win32 list views) | One extra cached property | No | `UIA_IsItemContainerPatternAvailablePropertyId` in the existing cache request | Stable |
| 6 | **`ElementFromPoint` grid sampling + ancestor walk** | Scroll containers under sampled points, including ones a subtree walk misses | Any UIA app | ~N hit-tests per scan **(probe)** | No | `IUIAutomation::ElementFromPoint` + `TreeWalker` to the first `ScrollPattern` ancestor | Stable API; technique unproven here |
| 7 | **Java Access Bridge** | `SCROLL_PANE` / `VIEWPORT` / `SCROLL_BAR` roles with bounds | Java Swing/AWT apps (e.g. JetBrains IDEs **(inference)**) | Per-call cross-JVM IPC | No | P/Invoke `WindowsAccessBridge-64.dll` (`IsJavaWindow`, `GetAccessibleContextFromHWND`, `GetAccessibleContextInfo`) | Mature but niche; user must run `jabswitch -enable` |
| 8 | **DDA move rects** | A rect scrolled by (dx, dy), at OS level | Only apps that `Present1` with a scroll rect (Windows Terminal confirmed; Chromium, WinUI and WPF not found) **and** only if DWM forwards it **(probe)** | Always-on duplication session per monitor | No | `IDXGIOutputDuplication::GetFrameMoveRects` (Win8+) via Vortice/SharpDX or C++/CLI | API stable; signal availability unknown |
| 9 | **Win32 `GetScrollBarInfo` / `GetScrollInfo` on child HWNDs** | Standard scrollbar range, page and position | Classic Win32 windows with `WS_VSCROLL`/`WS_HSCROLL`, and scrollbar controls | Microseconds | No | P/Invoke user32 | Stable; mostly already reached through UIA's Win32 proxy |
| 10 | **Layout models with a scroll or list class** (ScreenParse; Gestalt perceptual grouping) | Candidate "Scroll" or "List" boxes from pixels | Mostly web (ScreenParse); mobile (grouping) | 276 ms on an H100 (ScreenParse) [V]; grouping not measured | No | ONNX export **(inference)**; Python reference code | Research-grade |

[V] = vendor- or author-reported number.

---

## 1. Passive learning from the user's own wheel scrolling (new, rank 1)

**Idea.** The user scrolls by hand all day. Each real wheel event comes with a screen point. Whatever rectangle then changes on screen, or whatever element then fires a scroll event, is a scroll region for that app. Keep a per-app (window-rule key) cache of learned regions. At scan time, add learned regions that UIA did not report. This turns the no-signal apps from "default-window wheel only" into numbered, selectable regions, with nothing scrolled by us.

**Primary facts it rests on:**
- **Seeing every wheel event.** `LowLevelMouseProc` is called "every time a new mouse input event is about to be posted into a thread input queue". `wParam` can be `WM_MOUSEWHEEL`, and `lParam` points to an `MSLLHOOKSTRUCT` with the point. The hook runs on the installing thread, so that thread "must have a message loop". It has to return within `LowLevelHooksTimeout`, at most 1000 ms since Windows 10 1709, or "the hook is silently removed". Microsoft recommends Raw Input instead for monitoring ([LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)). The project already met the hook-timeout trap with `MovedSince` in the #69 prototype (`ScrollSelectProbe/Program.cs` comment near line 480). Do the diffing work off the hook thread.
- **Filtering out our own injections.** The same page says input from `mouse_event` counts as "injected", which `MSLLHOOKSTRUCT` flags. Ignore injected events so we don't learn from our own actuator **(inference: `LLMHF_INJECTED` is the flag on `MSLLHOOKSTRUCT.flags`)**.
- **Finding which rectangle changed:**
  - **WGC.** `Direct3D11CaptureFrame.DirtyRegions` exists from Windows SDK 26100, i.e. 24H2 ([DirtyRegions](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframe.dirtyregions)).
  - **DDA.** `GetFrameDirtyRects` returns "non-overlapping rectangles that indicate the areas of the desktop image that the operating system updated" ([Desktop Duplication API](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api)).
  - Dirty rects say *where* something changed, not *that it scrolled*. To confirm a scroll, run the baseline's tiled `phaseCorrelate` on just that rect, comparing frames before and after the user's wheel burst. This reuses `ocr-ai-region-detection.md` §4c, with the user as the actuator instead of us.
- **A free label where UIA can see the region.** See §2: Chromium fires scroll events on the exact node. When an event arrives within the wheel burst, the learned region is that element's `BoundingRectangle`, with no pixels needed.

**What it detects:** region rectangle and axis, and the direction of the user's scroll. It does not give extent or position for no-signal apps. That doesn't matter, because actuation there is the region-centre injected wheel anyway.

**Coverage:** everything the user scrolls by hand, including Monaco, Slack, `HwndTerminal`, canvas apps, RDP and games. The weakness is cold start: an app the user has never scrolled has no learned regions. Layout changes also make learned rects stale. Store them relative to the window, keyed by window rule, and drop a rect when it stops matching any UIA pane boundary **(inference)**.

**Cost and latency:** the hook is per-event and cheap. Diffing happens only during wheel bursts, so it is off the hint-scan path. Scan time adds only a lookup in the cache.

**Destructive?** No. It only observes.

**Privacy note:** the project would hold frame data briefly. Keep only rects and hashes, never pixels **(design note)**.

**Fit:** a new source that feeds `ScrollRegionSource` as an extra harvest input, merged before Gate → Suppress → Dedup. The learned store is a natural "auto-populated `expectedRegions`" for the `scrollDetection` rule section (#67), kept in a separate learned-data file rather than user rules. Window-target `0` stays as it is.

---

## 2. Scroll WinEvents and UIA scroll-percent property-changed events (new, rank 2)

**Primary facts:**
- `EVENT_SYSTEM_SCROLLINGSTART` (0x0012) and `EVENT_SYSTEM_SCROLLINGEND` (0x0013) are sent "for standard scroll bar controls and for scroll bars attached to a window", and servers send them for custom scrollbars. `idObject` is `OBJID_VSCROLL` or `OBJID_HSCROLL`, which gives the axis.
- `EVENT_OBJECT_CONTENTSCROLLED` (0x8015) is "associated with the scrolling window". Its `hwnd` is the scrolling window and `idObject` is `OBJID_CLIENT`.
- `EVENT_OBJECT_VALUECHANGE` (0x800E) is sent for "the scroll bar" among others.
- Sources for the four bullets above: [Event Constants](https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants).
- **Chromium fires these on real scrolls.** In `ui/accessibility/platform/browser_accessibility_manager_win.cc` (chromium/chromium mirror, commit `d0b3dbcc`):
  ```cpp
  case AXEventGenerator::Event::SCROLL_VERTICAL_POSITION_CHANGED:
    FireWinAccessibilityEvent(EVENT_SYSTEM_SCROLLINGEND, wrapper);
    FireUiaPropertyChangedEvent(UIA_ScrollVerticalScrollPercentPropertyId, wrapper);
  ```
  The horizontal case does the same. `kScrolledToAnchor` fires `EVENT_SYSTEM_SCROLLINGSTART`. Chromium's own expectations file `content/test/data/accessibility/event/scroll-vertical-scroll-percent-change-expected-win.txt` records `EVENT_SYSTEM_SCROLLINGEND on <div#div> role=ROLE_SYSTEM_GROUPING name="Vertical Scroll Container"`. That shows the event names the *inner container*, not just the document.
- **Caveat: the UIA Scroll pattern does not require providers to raise events.** "This control pattern has no associated events" ([Scroll Control Pattern](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingscroll)). Property-changed events for `ScrollPercent` are therefore provider-optional. Chromium raises them, and other frameworks must be checked **(probe)**.

**What it detects:** which element scrolled and on which axis. Read position from the element afterwards.

**Coverage:** the same set as `ScrollPattern` in Chromium, because the events come from the same `kScrollY` attributes, which exist only for real Blink scroll containers. Also standard Win32 scrollbars. It does **not** add Monaco or Slack.

**Cost:** event-driven. A WinEvent hook is cheaper than a UIA handler. `AccessibleObjectFromEvent` maps `(hwnd, idObject, idChild)` to an `IAccessible`, and `IUIAutomation::ElementFromIAccessible` gives the UIA element **(inference: standard MSAA→UIA bridging)**.

**Destructive?** No.

**Fit:**
- (a) Feeds §1 with exact labels.
- (b) Gives a **"last scrolled by user" ranking**. The region the user last scrolled in this window is a strong candidate for the default target or for number 1. This matters because the Gate/Dedup stage today has to pick among many claimed scrollables: the #59 census found Gmail claims 11 and only 1 is real.
- (c) Gives the actuator a cheap post-actuation check, an alternative to the `MovedSince` sleep-and-poll: wait for the event instead of sleeping **(inference)**.

---

## 3. Chrome DevTools Protocol DOM introspection (new, rank 3)

**Primary facts:**
- `DOMSnapshot.captureSnapshot` takes `computedStyles` (an allow-list) and `includeDOMRects`. `LayoutTreeSnapshot` then carries `bounds`, `offsetRects`, `scrollRects` and `clientRects` per layout node. The last three are "only available when includeDOMRects is set to true" ([DOMSnapshot.pdl](https://github.com/ChromeDevTools/devtools-protocol/blob/master/pdl/domains/DOMSnapshot.pdl)). Ask for `computedStyles: ["overflow-x","overflow-y"]`. Then a node is a scroll container when `scrollRect.height > clientRect.height && overflow-y ∈ {auto, scroll}`, and the same for x.
- That is exactly Rango's rule. `getUserScrollableContainer` tests `scrollHeight > clientHeight && /scroll|auto/.test(overflowY)`, with special cases for `position: fixed`/`absolute` and `document.body` (david-tejada/rango `src/content/dom/getUserScrollableContainer.ts`, commit `b3938ed`).
- **Why DOM rules miss Monaco, and how to catch it anyway.** Monaco's `ScrollableElement` creates a `div` with class `monaco-scrollable-element`, `role="presentation"` and `style.overflow = 'hidden'`. It scrolls through a virtual `Scrollable` class with custom `scrollbar vertical` / `slider` DOM, not native `scrollTop` (microsoft/vscode `src/vs/base/browser/ui/scrollbar/scrollableElement.ts`). So overflow rules see nothing. A **selector rule** (`.monaco-scrollable-element` with a visible `.scrollbar.vertical .slider`) finds every Monaco scroller. The slider's position and height give scroll position and view size **(inference from the DOM structure)**. This is the same "per-framework knowledge" pattern as the project's `expectedRegions`, just in the DOM instead of UIA.
- **The CDP Accessibility domain doesn't help.** `AXPropertyName` has no scroll-related value ([Accessibility.pdl](https://github.com/ChromeDevTools/devtools-protocol/blob/master/pdl/domains/Accessibility.pdl)). Use DOMSnapshot or `Runtime.evaluate`.
- **Deployment gates:**
  - **Chrome:** from Chrome 136, `--remote-debugging-port` / `--remote-debugging-pipe` "will no longer be respected if attempting to debug the default Chrome data directory". The switch must come with a non-default `--user-data-dir` ([Chrome blog](https://developer.chrome.com/blog/remote-debugging-port)). So CDP is **not** viable for the user's everyday Chrome or Edge **(Edge inherits this: inference)**.
  - **Electron:** `--remote-debugging-port` "Enables remote debugging over HTTP on the specified port" ([Electron switches](https://www.electronjs.org/docs/latest/api/command-line-switches)). The documented fuses can disable `--inspect` (`nodeCliInspect`) but list none for `--remote-debugging-port` ([Electron fuses](https://www.electronjs.org/docs/latest/tutorial/fuses)). Whether Slack and VS Code honour it in shipped builds needs a probe.
  - **WebView2:** an external CDP client can attach by setting `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<n>` ([Remote debugging desktop WebView2 apps](https://learn.microsoft.com/microsoft-edge/webview2/how-to/remote-debugging-desktop)).
  - **Security:** an open debug port lets any local process drive the app. Make it an explicit per-app opt-in.

**What it detects:** exact scroll containers, extent, position and axis, plus framework scrollers by selector.

**Coverage:** Electron apps and WebView2 hosts the user is willing to launch with a flag (a shortcut or wrapper).

**Cost:** one snapshot of a large SPA is probably tens of ms **(probe)**. Restrict `computedStyles` to the two overflow properties.

**Destructive?** No.

**.NET path:** HTTP `GET /json` to list targets, then `ClientWebSocket` for JSON-RPC. No new dependency.

**Fit:** a per-app opt-in **source** under the `scrollDetection` rule section (`strategy: cdp`, with selectors). It sits beside UIA, not before it. Map CSS-pixel rects to screen coordinates using the window position and device scale **(probe: Electron zoom and DPI)**. Actuation can stay on the injected wheel at the region centre, or use `Runtime.evaluate("el.scrollBy(...)")` for an exact, cursor-free scroll.

---

## 4. A companion browser extension with native messaging (new, rank 4)

**Primary facts:**
- **Rango** (Talon's hinting extension) already finds scroll containers with the rule quoted in §3.
- **Vimium's** `isScrollableElement` = `doesScroll && shouldScroll`:
  - `shouldScroll` rejects `overflow: hidden` and invisible elements.
  - `doesScroll` will "scroll the element by 1 or -1 and see if it moved (then put it back)".
  - `firstScrollableElement` recurses into the largest visible children.
  - Source: [philc/vimium `content_scripts/scroller.js`](https://github.com/philc/vimium/blob/master/content_scripts/scroller.js).

  This **±1 px self-restoring probe** is the only "scroll then immediately scroll back" design with production use. At 1 px it is not visible.
- **Native messaging:** the host is registered under `HKCU\SOFTWARE\Google\Chrome\NativeMessagingHosts\<name>`. Messages are length-prefixed JSON over stdin/stdout, at most 1 MB from host to Chrome ([Chrome native messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging)).

**What it detects:** the same as §3, but on the user's real browser profile, with no debug port.

**Coverage:** Chrome and Edge tabs. Whether content scripts run inside installed PWAs needs a probe. It does **not** reach Electron apps.

**Cost:** in-page JS, cheap.

**Destructive?** Rango's rule doesn't move anything. Vimium's probe moves 1 px and back, which is invisible.

**Fit:** this is a separate product surface (an extension to ship and maintain). It overlaps Rango, which Talon users may already run. Treat it as a later option, and consider *reading Rango's state* rather than building our own **(open question)**.

---

## 5. `ItemContainerPattern` as a container corroborator (new, small)

- "Any control that can contain virtualized items must support the ItemContainer control pattern". Also: "The Scroll control pattern should be unaffected by virtualization" ([ItemContainer Control Pattern](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingitemcontainer)).
- **Use:** add `IsItemContainerPatternAvailable` to the cache request. A large element with ItemContainer but no `ScrollPattern` is a strong hint that a scrolling parent exists, or that a provider omitted Scroll. It is a corroborator for the Gate stage and a candidate for the injected-wheel rung.
- It does not help Blink virtualizers, because Chromium's tree doesn't model them as virtualized containers **(inference)**.

## 6. `ElementFromPoint` grid sampling (new, small)

- `IUIAutomation::ElementFromPoint` returns the element at a desktop point ([docs](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-elementfrompoint)). Sample a coarse grid over the window's large UIA-empty areas, then walk ancestors to the first `ScrollPattern` or `RangeValue` ScrollBar.
- **Why it might add anything:** hit-testing goes through the provider's own hit-test path, which can surface nodes a `FindAll(Subtree)` misses in a lazily built tree **(inference; probe)**. The #59 `--reach` result says fragment reachability was not the problem, so expect a small gain. Cheap to try in the existing probe.

## 7. Java Access Bridge (new, niche)

- **Roles:**
  - `AccessibleRole.SCROLL_PANE`: "allows a user to incrementally view a large amount of information. Its children can include scroll bars and a viewport".
  - `VIEWPORT`: "represents the portion of the entire data that the user can see".
  - Source: [AccessibleRole](https://docs.oracle.com/en/java/javase/21/docs/api/java.desktop/javax/accessibility/AccessibleRole.html).
- **API:** the Access Bridge DLL exposes `IsJavaWindow`, `GetAccessibleContextFromHWND`, `GetAccessibleContextAt` and `GetAccessibleContextInfo` (role, states, x/y/width/height). It is enabled with `jabswitch -enable` ([Java Access Bridge API](https://docs.oracle.com/en/java/javase/21/access/java-access-bridge-api.html)).
- **Fit:** an optional source keyed by the window rule (`IsJavaWindow`). Detection = `SCROLL_PANE` bounds. Actuation = the `SCROLL_BAR`'s AccessibleValue, or the region wheel. Only worth it if Java apps are in the target set.

## 8. DXGI Desktop Duplication move rects (checked carefully; speculative)

- **What the API promises.** `GetFrameMoveRects` returns "rectangles of pixels in the desktop image that the operating system moved to another location within the same image. Each move region consists of a destination rectangle and a source point". Clients "must first process all the move regions and then process all the dirty regions" ([Desktop Duplication API](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api); [GetFrameMoveRects](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutputduplication-getframemoverects)). It returns `DXGI_ERROR_ACCESS_LOST` on desktop switch, mode change or full-screen transitions. Minimum OS is Windows 8. Per-output only.
- **Where moves come from (the key finding).** An app supplies a scroll rect and offset in `DXGI_PRESENT_PARAMETERS` when it calls `IDXGISwapChain1::Present1`. "The runtime also uses the scrolled rectangle to optimize presentation in terminal server and indirect display scenarios". Scroll is supported only with flip-model swap chains ([DXGI_PRESENT_PARAMETERS](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/ns-dxgi1_2-dxgi_present_parameters); [Flip model, dirty rectangles, scrolled areas](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/dxgi-1-2-presentation-improvements): "Remote Desktop Connection and other remote-accessing technologies … use dirty rectangles and scroll metadata"). The kernel display-only driver path also carries `pMoves`/`NumMoves` ([DXGKARG_PRESENT_DISPLAYONLY](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmddi/ns-d3dkmddi-_dxgkarg_present_displayonly)).
- **Who actually supplies scroll rects.** GitHub code search for `pScrollRect` (2026-10-13):
  - **microsoft/terminal** sets it in `src/renderer/atlas/AtlasEngine.r.cpp` (commit `fda72a0`): `params.pScrollRect = &scrollRect; params.pScrollOffset = &scrollOffset;`.
  - **chromium/chromium: 0 hits.** It does pass `pDirtyRects`, in e.g. `software_output_device_win_swapchain.cc`.
  - **microsoft-ui-xaml, dotnet/wpf, mozilla/gecko-dev: 0 hits.**
  - Caveat: GitHub code search doesn't index everything, so a 0 is strong but not conclusive evidence.
- **Verdict:**
  - The OS has a first-class "rect R scrolled by (dx,dy)" signal. But among the sources checked, only Windows Terminal feeds it, and Windows Terminal is already reachable through UIA (#59).
  - Chromium, WinUI and WPF scrolls almost certainly show up as **dirty rects only**.
  - Microsoft does not document whether DWM turns a windowed app's scroll rect into a desktop-level DDA move rect while compositing **(probe)**. Nor does it document whether GDI `ScrollWindowEx` in classic apps does **(probe)**.
  - So DDA move rects are an opportunistic, zero-cost corroborator inside §1's always-on capture. They don't justify a DDA session on their own.
- **Windows.Graphics.Capture has dirty regions but no move rects.** `DirtyRegions`/`DirtyRegionMode` only.

## 9. Win32 standard-scrollbar queries (new to the detector; mostly redundant)

- `GetScrollBarInfo(hwnd, OBJID_VSCROLL|OBJID_HSCROLL, …)` works on "a window created with WS_VSCROLL and/or WS_HSCROLL". With `OBJID_CLIENT` it sends `SBM_GETSCROLLBARINFO` to custom scrollbar controls ([GetScrollBarInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getscrollbarinfo)).
- `GetScrollInfo` returns 32-bit range, page and position for `SB_VERT`/`SB_HORZ`/`SB_CTL` ([GetScrollInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getscrollinfo)).
- ShareX already uses `GetScrollInfo` (baseline). The new angle is to enumerate child HWNDs with `WS_VSCROLL` as candidates when UIA comes back empty for a classic app.
- Expect little gain: UIA's Win32 proxies already map standard scrollbars, and the census found native Win32 reliable. Include only as a zero-cost fallback for owner-drawn panes that still use `WS_VSCROLL`.

## 10. Layout models with a scroll or list class (beyond the baseline's taxonomies)

- **ScreenParse / ScreenVLM (arXiv 2602.14276, 2026):**
  - It has a 55-class "ScreenTag" taxonomy that **includes "Scroll" (ID 20)**, alongside "List", "List Item" and "Carousel".
  - Labels come from DOM/ARIA heuristics, are relabelled with Qwen-3-VL-8B, then filtered by a VLM judge.
  - The authors call it "predominantly web-centric". ScreenVLM has 316 M parameters and a mean latency of 276.4 ± 139.0 ms on one H100 [V]. The dataset is CC-BY-4.0.
  - The paper does **not** define how "Scroll" is assigned (container or scrollbar?) and gives no per-class scores. So this is the first open model with a scroll class, but it is unvalidated, web-only, and Tier 3 by the baseline's latency rules. Source: [arXiv 2602.14276](https://arxiv.org/html/2602.14276).
- **Perceptual grouping (Xie et al., FSE 2022, arXiv 2206.10352):**
  - An unsupervised, training-free grouping of widgets into "tabs, menus, cards, and lists" using Gestalt principles. Evaluated on 1,091 mobile GUIs.
  - Code is at [MulongXie/GUI-Perceptual-Grouping](https://github.com/MulongXie/GUI-Perceptual-Grouping) (Apache-2.0, last push 2022-06-17, dormant).
  - **Use:** a "repeated rows" detector. A list group that touches a pane edge is a scroll-region prior for no-UIA surfaces. That is the formal version of the baseline's untested "OCR text clipped at an edge" heuristic. Source: [arXiv 2206.10352](https://arxiv.org/abs/2206.10352).
- **Apple Screen Recognition (CHI 2021):** 13 classes, including "Container", with no scroll or list class ([arXiv 2101.04893](https://arxiv.org/html/2101.04893)). Dead end for scroll.

---

## Facts that sharpen existing design (not new mechanisms)

- **Qt has no `ScrollPattern` on Windows.** `QWindowsUiaMainProvider::GetPatternProvider` handles Window, Text, Value, **RangeValue** ("e.g., sliders, scroll bars, dials"), Toggle, Selection, SelectionItem, Table, TableItem, Grid, GridItem, Invoke and ExpandCollapse, but not Scroll. The `uiautomation/` directory has no scroll provider file (qt/qtbase `dev`, `src/plugins/platforms/windows/uiautomation/qwindowsuiamainprovider.cpp`, commit `0cabfd6`). **Consequence:** Qt apps can only use the ScrollBar → RangeValue rung, and a Qt scroll area exposes no container. Add a built-in rule: FrameworkId `Qt` → pair each ScrollBar with its nearest sibling pane as the region **(inference)**.
- **The visual-scrollbar corroborator can read the user's setting.** `UISettings.AutoHideScrollBars` (Windows 10 1903+) reports whether "Automatically hide scroll bars" is on, and has a change event. It reports "the specified user setting, not whether a specific scroll bar is visible" ([UISettings.AutoHideScrollBars](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings.autohidescrollbars)). Use it to weight the scrollbar-affordance corroborator. Which frameworks honour the setting (WinUI yes per the baseline; Chromium unknown) needs a probe.
- **VS Code has a first-class scroll command.** `editorScroll {to: up|down, by: line|wrappedLine|page|halfPage|editor, value, revealCursor}` ([VS Code built-in commands](https://code.visualstudio.com/api/references/commands)). It can be reached from outside through the `command-server` extension, which Talon users often already have: "Adds support for running arbitrary commands via file-based RPC. Designed for use with voice-control systems such as Talon" ([cursorless-dev/command-server](https://github.com/pokey/command-server)). This is actuation rather than detection. But it means a VS Code window rule could swap the Monaco wheel fallback for an exact, cursor-free scroll of the *focused* editor. (It won't reach a non-focused split.)

---

## Dead ends (one line each)

- **DirectManipulation viewports / `InteractionTracker`:** the app creates them in its own process. There is no cross-process enumeration API, so we can't ask "which viewports exist".
- **`DM_POINTERHITTEST` (0x0250):** sent by the system on first pointer contact. `wParam`/`lParam` are unused and the return is NULL, so it carries no data to probe with ([DM_POINTERHITTEST](https://learn.microsoft.com/en-us/windows/win32/inputmsg/dm-pointerhittest)).
- **`WM_MOUSEWHEEL` return value as a probe:** "If an application processes this message, it should return zero". `DefWindowProc` also forwards unprocessed wheel up the parent chain, so 0 can't distinguish "scrolled" from "passed up" ([WM_MOUSEWHEEL](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)). The `MSH_MOUSEWHEEL` TRUE-on-processed contract applies only to that legacy registered message.
- **CDP `Accessibility` domain:** has no scroll property (see §3).
- **UIA custom properties:** no standard or Chromium-registered scroll custom property found.
- **Precision-touchpad / `WM_POINTER` scroll chaining:** no API to query chaining targets was found. Chaining is internal to DirectManipulation and the app.
- **Windows.Graphics.Capture move rects:** none exist. WGC has dirty regions only.
- **Gradient-fade / clipped-glyph overflow heuristics:** no primary-source method or evaluation was found beyond the baseline's untested OCR-clip idea and §10's grouping prior.
- **Screen Recognition, Ferret-UI, ScreenAI taxonomies:** no scroll class (the last two are covered in the baseline).

---

## Ranking and how each slots into the design

The existing chain is UIA detection → injected-wheel fallback → per-app profile.

| Rank | Mechanism | Where it slots | Why this rank |
|---|---|---|---|
| 1 | **Passive user-wheel learning** (§1) | A new harvest input to `ScrollRegionSource`: learned rects join the UIA candidates before Gate/Suppress/Dedup and are actuated by the existing region-centre wheel rung. Stored as learned data under the window-rule key (`scrollDetection`), separate from user rules. | Only non-destructive route to *numbered regions* in Monaco, Slack and `HwndTerminal`; uses APIs the project already uses (hooks, WinEvents) plus one capture path. |
| 2 | **Scroll WinEvents + UIA property-changed** (§2) | (a) A labeller for §1. (b) A "last user-scrolled" ranking signal in Dedup/Cap+number and default-target choice. (c) An event-based alternative to the `MovedSince` sleep in the actuator. | Free and event-driven, and confirmed in Chromium source and tests. Adds ranking, not coverage. |
| 3 | **CDP DOM introspection** (§3) | An opt-in per-app source (`scrollDetection.strategy: cdp` plus selectors such as `.monaco-scrollable-element`). Runs beside UIA for Electron/WebView2 rules. Optional `Runtime.evaluate` scroll actuation. | Exact answers *including Monaco*, but it needs a launch flag, opens a debug port (security), and doesn't work on the default Chrome profile. |
| 4 | **ItemContainer corroborator** (§5) and **ElementFromPoint sampling** (§6) | Extra facts in the COM harvest, consumed by the Gate stage. | Near-free, low expected gain. Try them in the existing probe. |
| 5 | **Browser extension + native messaging** (§4) | An external source on the same pipe as the Talon channel. | Exact for browser tabs, but a whole extension to ship and it overlaps Rango. Better: interoperate with Rango. |
| 6 | **Java Access Bridge** (§7) | An optional source behind an `IsJavaWindow` rule. | Real coverage gain only if Java IDEs are targets. |
| 7 | **DDA move rects** (§8) | A corroborator inside §1's capture loop. | Signal availability is unproven, and the known producer (WT) is already covered. |
| 8 | **`GetScrollBarInfo`/`GetScrollInfo`** (§9) | A fallback harvest for classic HWNDs when UIA is empty. | Mostly redundant with UIA's Win32 proxy. |
| 9 | **ScreenParse / perceptual grouping** (§10) | Tier-3 vision or a no-UIA prior, gated per app. | Too slow (ScreenParse) or dormant and mobile-only (grouping). The "Scroll" class is undefined. |

---

## Open questions that need a hands-on probe

1. **§1 feasibility.** With a `WH_MOUSE_LL` hook (or Raw Input) plus WGC `DirtyRegions` on 24H2, does one user wheel notch in VS Code, Slack and the VS embedded terminal yield a dirty rect that matches the scrolled pane, and within how many frames? How often do caret blink, animations and sticky headers pollute it? Does tiled `phaseCorrelate` on just the dirty rect separate the scrolled pane from co-dirty noise?
2. **§1 hook budget.** Does a wheel-only `WH_MOUSE_LL` that hands off to a worker stay well under `LowLevelHooksTimeout` while Windows-Hinting's keyboard hook is also active? Should we use Raw Input (`RIDEV_INPUTSINK`) instead?
3. **§2 coverage.** Which non-Chromium frameworks raise `UIA_ScrollVerticalScrollPercentPropertyId` changes or `EVENT_SYSTEM_SCROLLINGEND`/`EVENT_OBJECT_CONTENTSCROLLED` on user scroll: WinUI 3, WPF, File Explorer, Settings, conhost, Windows Terminal? Use Accessible Event Watcher or AccEvent, as the event-constants page advises.
4. **§3 deployment.** Do shipped VS Code and Slack builds honour `--remote-debugging-port` (and does Slack expose its message list as a DOM overflow container or a transform virtualizer)? How long does `captureSnapshot` with two computed styles take on Slack and VS Code? How do CSS px map to screen px under Electron zoom and per-monitor DPI?
5. **§4 PWAs.** Do extension content scripts run inside installed Chrome/Edge PWAs (Slack PWA, Gmail PWA)? Can Windows-Hinting read Rango's existing state instead of shipping its own extension?
6. **§8 move rects.** On Win11 24H2 with DWM, does a DDA session receive *any* move rects when (a) Windows Terminal scrolls (it sets `pScrollRect`), (b) a classic GDI app scrolls via `ScrollWindowEx`, (c) Chromium scrolls? Or only when windows are dragged?
7. **§5/§6 gain.** On the #59 matrix, does adding `IsItemContainerPatternAvailable` or 9–16-point `ElementFromPoint` sampling turn up any region the subtree census missed?
8. **Qt.** In a real Qt 6 app (e.g. Qt Creator), does the scroll area's ScrollBar expose a settable `RangeValue`, and does `SetValue` move the content (the existing rung)?

---

## Sources

**Microsoft Learn (Win32 / WinRT / drivers)**
- Event Constants (`EVENT_SYSTEM_SCROLLINGSTART/END`, `EVENT_OBJECT_CONTENTSCROLLED`, `EVENT_OBJECT_VALUECHANGE`): https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants
- Scroll Control Pattern ("no associated events"): https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingscroll
- ItemContainer Control Pattern: https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingitemcontainer
- IUIAutomation::ElementFromPoint: https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-elementfrompoint
- LowLevelMouseProc: https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc
- WM_MOUSEWHEEL: https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel
- DM_POINTERHITTEST: https://learn.microsoft.com/en-us/windows/win32/inputmsg/dm-pointerhittest
- GetScrollBarInfo / GetScrollInfo: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getscrollbarinfo · https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getscrollinfo
- Desktop Duplication API / GetFrameMoveRects: https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api · https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutputduplication-getframemoverects
- DXGI_PRESENT_PARAMETERS / flip model and scrolled areas: https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/ns-dxgi1_2-dxgi_present_parameters · https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/dxgi-1-2-presentation-improvements
- DXGKARG_PRESENT_DISPLAYONLY (`pMoves`): https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmddi/ns-d3dkmddi-_dxgkarg_present_displayonly
- Direct3D11CaptureFrame.DirtyRegions: https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframe.dirtyregions
- UISettings.AutoHideScrollBars: https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings.autohidescrollbars
- WebView2 remote debugging (desktop): https://learn.microsoft.com/microsoft-edge/webview2/how-to/remote-debugging-desktop

**Source code (first-party)**
- Chromium `ui/accessibility/platform/browser_accessibility_manager_win.cc` (GitHub mirror chromium/chromium, `d0b3dbcc`), and `content/test/data/accessibility/event/scroll-vertical-scroll-percent-change-expected-win.txt`
- microsoft/terminal `src/renderer/atlas/AtlasEngine.r.cpp` (`fda72a0`): https://github.com/microsoft/terminal/blob/main/src/renderer/atlas/AtlasEngine.r.cpp
- qt/qtbase `src/plugins/platforms/windows/uiautomation/` (`dev`, `0cabfd6`): https://github.com/qt/qtbase/tree/dev/src/plugins/platforms/windows/uiautomation
- microsoft/vscode `src/vs/base/browser/ui/scrollbar/scrollableElement.ts`: https://github.com/microsoft/vscode/blob/main/src/vs/base/browser/ui/scrollbar/scrollableElement.ts
- philc/vimium `content_scripts/scroller.js`: https://github.com/philc/vimium/blob/master/content_scripts/scroller.js
- david-tejada/rango `src/content/dom/getUserScrollableContainer.ts` (`b3938ed`): https://github.com/david-tejada/rango/blob/main/src/content/dom/getUserScrollableContainer.ts
- cursorless-dev/command-server: https://github.com/pokey/command-server

**Protocols / platform docs**
- CDP DOMSnapshot.pdl / Accessibility.pdl: https://github.com/ChromeDevTools/devtools-protocol/blob/master/pdl/domains/DOMSnapshot.pdl · https://github.com/ChromeDevTools/devtools-protocol/blob/master/pdl/domains/Accessibility.pdl
- Chrome 136 remote-debugging change: https://developer.chrome.com/blog/remote-debugging-port
- Chrome native messaging: https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging
- Electron command-line switches / fuses: https://www.electronjs.org/docs/latest/api/command-line-switches · https://www.electronjs.org/docs/latest/tutorial/fuses
- VS Code built-in commands (`editorScroll`): https://code.visualstudio.com/api/references/commands
- Java AccessibleRole / Java Access Bridge API: https://docs.oracle.com/en/java/javase/21/docs/api/java.desktop/javax/accessibility/AccessibleRole.html · https://docs.oracle.com/en/java/javase/21/access/java-access-bridge-api.html

**Papers**
- ScreenParse / ScreenVLM (arXiv 2602.14276): https://arxiv.org/html/2602.14276
- Xie et al., perceptual grouping of GUI widgets (FSE 2022, arXiv 2206.10352): https://arxiv.org/abs/2206.10352 · code https://github.com/MulongXie/GUI-Perceptual-Grouping
- Zhang et al., Screen Recognition (CHI 2021, arXiv 2101.04893): https://arxiv.org/html/2101.04893
