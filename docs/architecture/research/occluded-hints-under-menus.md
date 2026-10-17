# Research: Hints on elements covered by an open menu/popup

**Ticket:** [#123](https://github.com/knausj85/Windows-Hinting/issues/123): "Opening various menus on
e.g. github creates confusing array of hints". The examples are the hamburger menu, the "more" menu
and the branch-selection menu on `github.com/knausj85/Windows-Hinting`, so the case that matters
first is **web content in Edge/Chrome**. The ticket says "probably nothing to be done, but worth a
bit of research".

**Question:** When a menu, popup or flyout is open, the scan also returns elements of the window
underneath that the menu covers, and their hints get mixed in with the menu's own hints. Can we tell
which elements are covered? How much would it cost, and how would it fail?

**Prior art in this repo (cited here, not re-derived):**

- **#77, menu-aware chaining.** Code is banked at `55f50f6` on `prototype/chained-hinting`. It uses a
  dedicated `CUIAutomation` subscribed to **global UIA `MenuOpened`(20003) / `MenuClosed`(20007)**
  on root+Subtree, and scans the **event sender's subtree** directly. Sender HWND is 0 for XAML
  flyouts. It also has a `FindMenuClickableElements(windowRoot)` helper that FindAlls
  `ControlType.Menu|MenuBar` containers and scans only inside them. Validated live on
  VS/Word/Notepad/Explorer. The finding was that `MenuOpened` **does** fire there, contrary to #45.
  See the [#77 resolution comment](https://github.com/knausj85/Windows-Hinting/issues/77#issuecomment-5750902256).
- **#71 / `research/edge-chromium-settle`.** For a UIA client, Chromium's a11y tree is a complete
  browser-process snapshot, so no "awakening" is needed.

All source permalinks below are pinned to the commit current on 2026-10-17. Chromium
`5926523b`, dotnet/wpf `65d15ade`, Vimium `5aa29614`, Vimium C `31885269`, Hunt-and-Peck
`067a8f7a`, Primer React `7f96c0d5`, Primer ViewComponents `1e47782e`.

---

## VERDICT (TL;DR)

**Something can be done, and it is cheap.** UIA does not filter covered elements for us.
`IsOffscreen`, `BoundingRectangle` and Chromium's `ClickablePoint` all ignore occlusion, so today's
`IsOffscreen=false` + `ClickablePoint` filter cannot catch this. However, the open menu almost
always identifies itself, and two cheap techniques together cover the cases in the ticket:

1. **Popup-container suppression.** This handles same-HWND popups such as web menus and XAML
   flyouts, which is the GitHub case.
   - Find the open popup containers in the scanned tree. Use `ControlType.Menu` (not `MenuBar`) or
     `IsDialog=true`, and optionally the `MenuOpened` sender from #77.
   - Then drop every hint that is **not inside a container but whose rectangle sits under one**.
   - The policy variant "menu open ⇒ hint only the menu" is simpler still.
   - It needs about one extra cross-process call per container, not one per element.
   - Chromium raises `UIA_MenuOpenedEventId` whenever a `role="menu"` node is inserted or
     un-ignored, and GitHub's Primer `ActionMenu` renders `role="menu"`. So the GitHub "more" menu
     is directly detectable.
2. **`WindowFromPoint` occlusion check.** This handles popups in a separate HWND (classic `#32768`
   menus, Office `NetUIHwnd`, windowed XAML popups, and any other top-level window lying over the
   target).
   - For each hint, hit-test its centre (and corners, if needed) with `WindowFromPoint`.
   - Drop the hint if the root window at that point is not one of the windows being hinted.
   - This is a user32 call made inside our own process, not a UIA round-trip to the target app, so
     even hundreds of hints should be cheap (unmeasured, see §5). Our overlay is
     `WS_EX_LAYERED|WS_EX_TRANSPARENT`, which per the docs passes hit tests through to the windows
     beneath.

**What not to build first:** per-element **UIA `ElementFromPoint`** occlusion testing in the style of
Vimium. It costs N cross-process calls plus ancestor walks. On Chromium, the case this ticket is
about, the synchronous answer is the browser process's **`ApproximateHitTest`**. That is a bounds
check where "later in the tree wins", which Chromium's own comments call "wrong in cases of z-index,
overflow, and other more complicated layouts". It would add a lot of cost and still be no more
accurate than (1), which does the same rectangle reasoning locally with no extra calls.

**Recommended next step:** a small **probe**, described in §7. Dump the containers, their rects and
the would-be-suppressed hints for the three GitHub menus plus Notepad, Explorer and Word menus,
before any product change. The open question the probe answers is what each GitHub menu actually
exposes: `role=menu`, an `aria-modal` dialog, or a native modal `<dialog>`. These behave differently
in Chromium on Windows (§3.3).

---

## 1. What the code does today

`Windows-Hinting/Services/UIAutomationService.cs` (on `main`):

- **Condition:** `IsEnabled=true AND IsOffscreen=false AND ControlType ∈ {Button, …, Menu,
  MenuItem, …}` (lines 62–78). One `FindAllBuildCache(TreeScope_Descendants)` runs per resolved root
  (line 190).
- **Cache:** `BoundingRectangle`, `ClickablePoint`, ControlType, pattern-availability, Name,
  ClassName, ProcessId, LegacyIAccessible state and NativeWindowHandle (lines 81–102). RuntimeId is
  **not** cached.
- **Post-filter:**
  - Drop elements with an empty rect.
  - If the cached `ClickablePoint` is `VT_EMPTY`, keep the element only when its MSAA legacy state
    lacks the offscreen bit (lines 240–253).
  - Then require an activatable pattern.
- **Scope:** the foreground window's HWND, or a rule-selected sub-root (`ResolveRootElements`). The
  scan never asks whether anything lies **above** an element.
- Side note: `IsLegacyElementOffscreenOrInvisible` tests mask `0x00010000` (line 703). That is
  `STATE_SYSTEM_OFFSCREEN` only. `STATE_SYSTEM_INVISIBLE` is `0x00008000`
  ([Object State Constants](https://learn.microsoft.com/en-us/windows/win32/winauto/object-state-constants)),
  but the doc comment says it checks both. This is unrelated to #123 but worth fixing in passing.

So the scan filters on `IsOffscreen` and `ClickablePoint`. Neither of those signals accounts for
occlusion, as §2 shows.

## 2. What UIA actually guarantees

### 2.1 `IsOffscreen`: explicitly not occlusion-aware

Per [Automation Element Property Identifiers](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-automation-element-propids),
`UIA_IsOffscreenPropertyId`:

> "…indicates whether the automation element is entirely scrolled out of view … or collapsed out of
> view … **The value of the property is not affected by occlusion by other windows**, or by whether
> the element is visible on a specific monitor."

Chromium's implementation matches. `BrowserAccessibility::IsOffscreen()` computes the element's
bounds **clipped by its ancestors/viewport** and reports `kOffscreen` only if nothing survives the
clip ([browser_accessibility.cc#L934-L939](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/ui/accessibility/platform/browser_accessibility.cc#L934-L939)).
A page element that sits under an open popover is inside the viewport, so it reports
`IsOffscreen=false`.

### 2.2 `BoundingRectangle`: may include obscured points

Same page, `UIA_BoundingRectanglePropertyId`: "It can contain points that are not clickable if the
shape or clickable region of the UI item is irregular, **or if the item is obscured by other UI
elements**."

### 2.3 `ClickablePoint`: occlusion-aware on paper, not in practice

- **Docs.** The same page says, for `UIA_ClickablePointPropertyId`: "An element cannot be clicked if
  it is completely or partially obscured by another window." The method doc
  [`IUIAutomationElement::GetClickablePoint`](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationelement-getclickablepoint)
  adds nothing about how this is enforced.
- **Chromium provider ignores occlusion.** It returns the **unclipped bounds' centre** whenever the
  node is not offscreen. There is no occlusion test
  ([ax_platform_node_win.cc#L5348-L5352](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/ui/accessibility/platform/ax_platform_node_win.cc#L5348-L5352)
  and [`CreateClickablePointArray`, #L571-L588](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/ui/accessibility/platform/ax_platform_node_win.cc#L571-L588)).
  Side note: it computes this point in `kScreenDIPs`, whereas `BoundingRectangle` is physical
  pixels. That could matter on scaled displays and is worth a check in the probe.
- **Microsoft's managed client knows provider points can't be trusted.** In
  `System.Windows.Automation`, `AutomationElement.TryGetClickablePoint` takes the provider's point
  and then **validates it** with `AutomationElement.FromPoint` and a parent walk until it reaches
  itself
  ([AutomationElement.cs#L878-L904](https://github.com/dotnet/wpf/blob/65d15adece1df9a73e3d1abc11f8bbf99e9fc7a9/src/Microsoft.DotNet.Wpf/src/UIAutomation/UIAutomationClient/System/Windows/Automation/AutomationElement.cs#L878-L904)).
  If that fails it falls back to `ClickablePoint.HitTestForClickablePoint`
  ([ClickablePoint.cs#L20-L77](https://github.com/dotnet/wpf/blob/65d15adece1df9a73e3d1abc11f8bbf99e9fc7a9/src/Microsoft.DotNet.Wpf/src/UIAutomation/UIAutomationClient/MS/Internal/Automation/ClickablePoint.cs#L20-L77)):
  - hit-test the centre;
  - bail if a top-level window is obscuring it (`IsTopLevelWindowObscuring`);
  - then try the four edge midpoints, a sparse grid, and a diagonal line;
  - with "don't more than about 25 hits" per pattern (header comment at L20–21: "There are times
    when it would take so long … it is just not worth it").
- **What this means for us.** That validation is **client-side, in the managed library only**. We
  use native COM and read the **provider-supplied cached `ClickablePoint` property**, which gets
  none of it. The native `GetClickablePoint` is closed source, and no primary source says it does
  any hit-testing.

**Conclusion §2:** no UIA property tells us an element is covered. Any fix has to compute occlusion
itself. Microsoft's own client library does that by hit-testing, at a deliberately capped cost.

## 3. Detecting the popup itself

### 3.1 UIA / WinEvent signals

| Signal | Source of truth | Notes |
|---|---|---|
| `UIA_MenuOpenedEventId` 20003 / `UIA_MenuClosedEventId` 20007 | [UIA event IDs](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-event-ids) | Proven to fire on Notepad/Explorer/VS/Word in #77. XAML flyouts fire **before** their items are in the tree, so #77 added a progressive settle. |
| `EVENT_SYSTEM_MENUPOPUPSTART` 0x0006 | [Event Constants](https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants) | System sends it for HMENU menus, and servers for custom menus. "This event is not sent consistently by the system." |
| `ControlType.Menu` container | (structural) | `FindMenuClickableElements` in `55f50f6` already uses Menu/MenuBar. For occlusion only **Menu** counts, because a MenuBar is permanent chrome. |
| `UIA_IsDialogPropertyId` 30174 | [Property IDs](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-automation-element-propids) | Chromium sets it from the role: `IsDialog(GetRole())` ([ax_platform_node_win.cc#L5443-L5446](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/ui/accessibility/platform/ax_platform_node_win.cc#L5443-L5446)), i.e. `role=dialog`/`alertdialog`. |
| `UIA_IsPeripheralPropertyId` 30150 | Property IDs | "popups, flyouts, context menus, or floating notifications". This is a possible extra marker, but it is provider-optional and coverage is unknown. |
| `#32768` / `NetUIHwnd` / `Xaml_WindowedPopupClass` window classes | #45/#77 prior art | Only for popups that have their **own HWND**. Handled by §4.2 anyway. |

### 3.2 Chromium: web `role="menu"` raises `MenuOpened`

- **Event generation.** Chromium's event generator adds `MENU_POPUP_START` when a `kMenu` node
  becomes un-ignored
  ([ax_event_generator.cc#L419-L425](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/ui/accessibility/ax_event_generator.cc#L419-L425)).
  It also does so when a menu is inserted already visible ("This catches menus inserted already
  visible", [#L1064-L1069](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/ui/accessibility/ax_event_generator.cc#L1064-L1069)).
- **Mapping on Windows.** The Windows manager maps `MENU_POPUP_START` to **both**
  `EVENT_SYSTEM_MENUPOPUPSTART` and `UIA_MenuOpenedEventId`
  ([browser_accessibility_manager_win.cc#L559-L566](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/ui/accessibility/platform/browser_accessibility_manager_win.cc#L559-L566)).
- **GitHub's menus.** Primer React's `ActionMenu` renders its list with `listRole: 'menu'`
  ([ActionMenu.tsx#L337](https://github.com/primer/react/blob/7f96c0d5f95581d539c02e540da56a4514f2953c/packages/react/src/ActionMenu/ActionMenu.tsx#L337)).
  So a GitHub "more"-style action menu should arrive as a `ControlType.Menu` subtree **and** fire
  `MenuOpened` in Edge/Chrome. This is unverified on the live page and is probe item P1.

### 3.3 Chromium on Windows: some modals already hide the background, others don't

- **Native modal `<dialog>` hides the background.** `dialog.showModal()` makes the rest of the
  document inert. Blink marks inert nodes **ignored** (`cached_is_inert_` → ignored), so they leave
  the platform tree
  ([ax_object.cc#L4043-L4048, #L4092-L4117](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/third_party/blink/renderer/modules/accessibility/ax_object.cc#L4092-L4117)).
  For these, **we already get no background hints**. Primer ViewComponents' `SelectPanel` opens
  this way (`this.dialog.showModal()`,
  [select_panel_element.ts#L936-L938](https://github.com/primer/view_components/blob/1e47782e3944cf7460fa22eff03e1b69f617e80a/app/components/primer/alpha/select_panel_element.ts#L936-L938)).
- **`aria-modal="true"` alone does NOT hide the background on Windows.** Blink only prunes for
  aria-modal when `AriaModalPrunesAXTree` is set, and Chromium sets it on Mac and Android only:
  "aria-modal currently prunes the accessibility tree on Mac and Android only"
  ([render_accessibility_impl.cc#L93-L96](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/content/renderer/accessibility/render_accessibility_impl.cc#L93-L96);
  gate at [ax_object.cc#L4118-L4121](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/third_party/blink/renderer/modules/accessibility/ax_object.cc#L4118-L4121)).
  Primer React's `Dialog` is a `div role=dialog aria-modal`, not a native `<dialog>`
  ([Dialog.tsx#L415-L420](https://github.com/primer/react/blob/7f96c0d5f95581d539c02e540da56a4514f2953c/packages/react/src/Dialog/Dialog.tsx#L415-L420)).
  So on Windows the whole page behind it stays in the UIA tree, which is the confusing case. It
  still exposes `IsDialog=true` on the container, so a container-based filter catches it.
- **This explains the ticket's mix.** Some GitHub popups would be clean already (native modal
  dialog), and others leak background hints (`role=menu` popovers, `aria-modal` divs). Probe item P1
  confirms which GitHub menus fall where.

### 3.4 Could "menu is open ⇒ restrict to the menu" work?

Yes. #77 already does it for chaining. On `MenuOpened` it scans **the sender's subtree only**, so
the window underneath gets no hints by construction. Two things are needed to make it the general
fix:

- **Run it on the first activation too**, not only during chaining. If a menu is already open when
  the user presses the hint key, use the Menu/IsDialog containers in the scan, or the last
  `MenuOpened` sender if it is still alive. That gives a "popup is open" state without relying on
  event timing.
- **Use it as policy, not occlusion.** "Hint only the menu" also hides page elements that are *not*
  covered. Most of these menus are light-dismiss: a click outside just closes them. So hiding those
  elements is arguably what users want. This is a product call, discussed in §6.

## 4. Occlusion-testing techniques compared

### 4.1 Popup-container rect suppression (local, no per-element round-trips)

**Algorithm.**

1. Find containers *C* among the scan's subtree: `ControlType.Menu` ∪ `IsDialog=true` ∪ (the
   #77 `MenuOpened` sender).
2. Read each container's `BoundingRectangle`.
3. Find the hints that belong inside each container. Use a scoped `FindAllBuildCache` under it, as
   `FindMenuClickableElements` does, or compare RuntimeIds if RuntimeId is added to the cache.
4. Drop every other hint whose rect is mostly (or whose centre is) inside a container rect.

**Cost.** One FindAll for containers, which can be folded into the existing condition as an OR-arm
with a ControlType/IsDialog check, plus one scoped FindAll per container. The cost is
**O(#containers)** round trips, not O(#elements). The rectangle math is local.

**Why it's no worse than UIA hit-testing in Chromium.** Chromium's synchronous hit test is itself a
rectangle containment walk over the cached tree (§4.3). This technique does the same reasoning,
using knowledge of which subtree is the popup.

**Failure modes.**

- **Over-suppression** if a container's rect is bigger than what is drawn. An example would be a
  full-viewport `role=dialog` wrapper whose visible panel is small. Mitigation: use the innermost
  container, or the union of the container's children's rects.
- **Under-suppression** if a popup exposes neither Menu nor IsDialog. Examples: a bare
  `role=listbox` popover, a hovercard, a custom `div`. Mitigation: in Chromium, also treat
  `IsPeripheral`, or `ControlType.Window`/`Pane` nested inside the document, as a container. Needs
  per-framework care, so it is a probe item.
- **Stale containers** where a Menu stays in the tree with real bounds after closing. Chromium marks
  CSS-hidden content ignored, so this should be rare there. Keep the MenuBar exclusion.
- **Office ribbon dropdowns and galleries:** container type unknown, but they are separate
  `NetUIHwnd` HWNDs, so §4.2 covers them.

### 4.2 `WindowFromPoint` / z-order (Win32, no UIA round-trip)

**Algorithm.** For each hint's centre point, `GetAncestor(WindowFromPoint(pt), GA_ROOT)`. If the
result is not the scanned window, or a window it owns that we are also hinting, the point is
covered by another top-level window.

**Docs.**

- [`WindowFromPoint`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-windowfrompoint)
  "does not retrieve a handle to a hidden or disabled window". Over a static control it returns the
  window underneath.
- For our own overlay, layered-window hit testing ignores the window's shape when it has
  `WS_EX_TRANSPARENT`, "and the mouse events will be passed to other windows underneath the layered
  window" ([Window Features § Layered Windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)).
  Our overlay sets `WS_EX_TRANSPARENT|WS_EX_LAYERED|WS_EX_TOPMOST|WS_EX_NOACTIVATE`
  (`Windows-Hinting/Forms/OverlayForm.cs:321-325`), so it should be invisible to the check even
  while shown. **Verify in the probe** (P3). If it doesn't hold, run the check before the overlay
  paints, or skip results whose root is our own HWND.

**Alternatives.** Walk the z-order (`GetWindow(GW_HWNDPREV)` from the target), intersect
`DWMWA_EXTENDED_FRAME_BOUNDS` rects, and skip `DWMWA_CLOAKED` windows
([DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)).
This is region subtraction once per scan instead of once per point. It is more code, and it is
wrong for windows that are visually transparent or irregular but whose rects are large (shadows,
glass, tool windows). `WindowFromPoint` asks the system's real hit test, so it avoids that guess.

**Coverage.** It catches covering windows that have their **own HWND**: classic `#32768` menus,
Office `NetUIHwnd` popups, windowed XAML popups, Chromium's *browser-UI* menus (Views widgets), and
any unrelated always-on-top window. It **cannot** see popups drawn inside the same HWND. Web-page
menus are one HWND (the Chromium render widget), so this technique alone does not fix the GitHub
case.

**Failure modes.**

- Hit-transparent-but-visible windows (layered or `WS_EX_TRANSPARENT` decorations drawn by other
  apps) count as "not covering", so their hints are kept. That is the safe direction.
- Hit-testable-but-invisible windows count as covering, which wrongly drops hints. Cloaked windows
  are a candidate; whether `WindowFromPoint` skips them is undocumented (probe P3).
- A centre point covered while the corners are visible gives partial-occlusion false negatives.
  Mitigation: test the centre plus up to 4 inset corners, as Vimium does (§4.4). Keep a hint if any
  point is ours.
- **DPI:** the app is PerMonitorV2 (`Windows-Hinting.csproj:22`) and UIA rects are physical pixels.
  `WindowFromPoint` coordinates therefore need to be in the same physical space. Verify on a scaled
  secondary monitor (P3).

### 4.3 UIA `ElementFromPoint` + ancestry check (Vimium-style, cross-process)

**Algorithm.** For each hint, call `IUIAutomation::ElementFromPoint(centre)`, ideally
`ElementFromPointBuildCache` with RuntimeId. Keep the hint if the hit element **is** the hint
element or a descendant of it, judged by RuntimeId plus a parent walk (WPF's validation loop does
exactly this).

**Cost.** At least one cross-process call per hint, plus a parent walk on a miss. Ancestors cannot
be pre-cached: a cache request's scope is "only the element itself … or for the element and its
children or descendants"
([put_TreeScope](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationcacherequest-put_treescope)).

**Chromium behaviour, the ticket's case.**

- `ElementProviderFromPoint` descends with `HitTestSync`
  ([ax_fragment_root_win.cc#L187-L213](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/ui/accessibility/platform/ax_fragment_root_win.cc#L187-L213)).
  That reaches `BrowserAccessibilityManager::CachingAsyncHitTest`
  ([browser_accessibility.cc#L1256-L1265](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/ui/accessibility/platform/browser_accessibility.cc#L1256-L1265)).
  It **fires an async renderer hit test**, and returns the true answer synchronously only if the
  point lies within the *last* async result's bounds.
- Otherwise it falls back to a local search: "This is generally right for simple pages but **wrong
  in cases of z-index, overflow, and other more complicated layouts**. The hope is that if the user
  is moving the mouse, this fallback will only be used transiently"
  ([browser_accessibility_manager.cc#L2077-L2085](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/ui/accessibility/platform/browser_accessibility_manager.cc#L2077-L2085)).
- That fallback, `ApproximateHitTest`, walks children **backwards**, "we assume the object that
  occurs later in the tree is on top"
  ([browser_accessibility.cc#L769-L815](https://github.com/chromium/chromium/blob/5926523bcfedb77ba2533ace1dd93b477673f72a/ui/accessibility/platform/browser_accessibility.cc#L769-L815)).
  An overlay portaled to the end of `<body>` (Primer's pattern) happens to win. A `popover` or
  top-layer element earlier in the DOM loses.
- Batch-hitting hundreds of *distinct* points is the worst case for this cache. Almost every call
  takes the approximate path, and each one also posts a renderer hit test.

**Other providers.** WPF and XAML providers are expected to do real visual hit tests. That is not
verified from source here and is fog for a later pass if §4.1/§4.2 leave gaps.

**Failure modes.**

- `UIA_E_ELEMENTNOTAVAILABLE` races, which
  [the docs](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-elementfrompoint)
  tell clients to retry.
- Hits on our overlay if it is not hit-transparent.
- Elements whose centre is covered by their *own* decoration child or label sibling cause false
  "covered" results. Vimium needs the `contains` check in both directions for this.
- Latency multiplied by N.

**Batching.** No batched hit-test API exists in `IUIAutomation`. Windows 11's UIA **Remote
Operations** batch many calls into one cross-process round trip, but "Remote operations are bound
to a single process"
([CoreAutomationRemoteOperation](https://learn.microsoft.com/en-us/uwp/api/windows.ui.uiautomation.core.coreautomationremoteoperation)).
Their operation set is built around elements already obtained; no hit-test primitive was found in
the primary docs. Treat this as not a viable batching route without further evidence.

### 4.4 How other tools handle it (from source)

| Tool | Occlusion handling | Source |
|---|---|---|
| **Vimium** (browser) | Yes. For each hint, `elementFromPoint` at the middle, then at 4 corners inset 0.1px. Keep the hint if the hit element contains the hint's element or is contained by it; otherwise "assume it's completely covered". | [link_hints.js#L1443-L1483](https://github.com/philc/vimium/blob/5aa29614bf1dce05e0d316f8c38722e17f9b38c3/content_scripts/link_hints.js#L1443-L1483); shadow-DOM-aware `getElementFromPoint` at [#L1355-L1370](https://github.com/philc/vimium/blob/5aa29614bf1dce05e0d316f8c38722e17f9b38c3/content_scripts/link_hints.js#L1355-L1370) |
| **Vimium C** | Yes. `filterOutNonReachable` uses `elementsFromPoint` stacks, **with a time budget** `ElementsFromPointTakesTooSlow = 1000` (ms) after which it stops filtering. | [local_links.ts#L692-L724](https://github.com/gdh1995/vimium-c/blob/31885269460a3f0635c7dd69176ed387ad138e96/content/local_links.ts#L692-L724); constant in `typings/vimium_c.d.ts` |
| **Rango** (Talon browser hints) | Yes, for hint placement/visibility: `document.elementsFromPoint(centre)`, then a `contains` check. | [ElementWrapper.ts#L561-L575](https://github.com/david-tejada/rango/blob/fdc339847cf0481d052b926624709d832c6bbc81/src/content/wrappers/ElementWrapper.ts#L561-L575) |
| **Hunt-and-Peck** (Windows UIA hinter, the closest analogue) | **No.** `FindAll(Descendants, ControlView ∧ IsEnabled ∧ IsOffscreen=false)` on the foreground window, keeping any non-empty rect. It has exactly our gap. | [UiAutomationHintProviderService.cs#L103-L125](https://github.com/zsims/hunt-and-peck/blob/067a8f7a80a6cb58928bebe44b6b577dd01e2634/src/HuntAndPeck/Services/UiAutomationHintProviderService.cs#L103-L125) |
| **.NET `System.Windows.Automation`** | Yes, for a single element on demand: `FromPoint` + ancestry, then a grid/line search capped around 25 hits. | §2.3 |
| Talon (core), Fluent Search, ShortcutKeys | Closed source; no primary documentation on occlusion found. | — |
| Accessibility Insights | Not a hinter. Its inspect-on-hover uses point hit-testing for a single element; not relevant to batch filtering. | — |

**Lesson.** Browser hinters can afford per-element hit tests because `elementFromPoint` is an
in-process, exact, layout-backed call, and even then Vimium C time-boxes it. A UIA hinter pays a
cross-process call per element and, on Chromium, gets an approximate answer. That pushes us towards
§4.1 (for in-HWND popups) and §4.2 (for HWND popups).

## 5. Performance

| Technique | Extra cross-process UIA calls per scan | Local work |
|---|---|---|
| 4.1 container suppression | 1 + #containers (often 0–2) | O(hints × containers) rect math |
| 4.2 `WindowFromPoint` | 0 | 1–5 user32 calls per hint (win32k, our process) |
| 4.3 UIA `ElementFromPoint` | ≥ N (+ parent walks on miss) | — |

- **No primary source gives a per-call latency** for UIA `ElementFromPoint` or for `WindowFromPoint`.
  The probe should time them (P4).
- Proxies for "it's expensive":
  - WPF's client caps itself at about 25 hits per element, and documents that it gives up because
    it "would take so long" (§2.3).
  - Vimium C caps the whole filter at 1000 ms, even with in-process DOM calls (§4.4).
  - The repo already logs `FindAllBuildCache` and `ProcessElements(n)` timings through
    `PerformanceMetrics` (`UIAutomationService.cs:183,211`), so the probe can report the overhead
    as a percentage of the existing scan.

## 6. Recommendation

The cheapest robust approach is **4.1 + 4.2 together, gated on "a popup is present"**. Each covers
what the other cannot see.

1. **Gate.** Do nothing unless the scan found a non-MenuBar `ControlType.Menu` or `IsDialog=true`
   container, or #77's `MenuOpened` has a live sender. Normal scans stay unchanged and pay nothing.
2. **In-HWND popups (4.1).** Choose a policy:
   - **(a) Menu-only:** hint only elements inside the popup container(s), as #77 already does on
     `MenuOpened`.
   - **(b) Occluded-only:** drop hints outside the container whose rect is under it.

   Start with (b), because it is less surprising for non-modal side panels. Offer (a) as a
   preference if users prefer "menu open ⇒ menu only".
3. **Separate-HWND popups and other windows on top (4.2).** A `WindowFromPoint` root-ancestor check
   at each hint's centre. Add corners only when the centre is covered. This can run
   **unconditionally**, since it costs nothing cross-process. It also fixes the wider "another
   window overlaps the target" family.
4. **Don't build 4.3** unless the probe shows gaps that 4.1 and 4.2 cannot close.

**Residual / not fixable here:** custom popups that expose neither Menu/IsDialog nor their own HWND
(an arbitrary `div` popover) stay under-suppressed. Fixing them would need app or site profiles,
which the per-app profile layer from maps #54/#72 could host.

## 7. Proposed probe (next step)

A throwaway `--probe-occlusion` mode in the style of `TaskbarOverlayProbe`. It needs no overlay
change, just logs.

- **P1 (GitHub semantics).** In Edge, on `github.com/knausj85/Windows-Hinting`, open the hamburger,
  "more" and branch menus. For each:
  - log the containers found (ControlType, IsDialog, IsPeripheral, AriaRole, rect, child count);
  - log whether `MenuOpened` fired;
  - log the number of hints that 4.1(b) would suppress, against the total.
- **P2 (native menus).** The same log for a Notepad context menu (`#32768`), an Explorer flyout, a
  Word ribbon dropdown and a WinUI `MenuFlyout`. Record 4.1 and 4.2 verdicts per hint.
- **P3 (Win32 edge cases).** Confirm that `WindowFromPoint` skips our live overlay and skips cloaked
  windows. Check coordinate consistency on a 150% secondary monitor, and Chromium `ClickablePoint`
  DIP vs physical.
- **P4 (cost).** Time `WindowFromPoint` × N and `ElementFromPoint` × N on a GitHub page with more
  than 300 hints, next to the existing `FindAllBuildCache` timing.

**Exit criteria:** on the three GitHub menus, background hints under the popup are 0 and menu hints
are unchanged, with overhead under 10% of the scan. If so, promote it to a `modernize` scan-pipeline
filter. Phase 04 is the natural home.
