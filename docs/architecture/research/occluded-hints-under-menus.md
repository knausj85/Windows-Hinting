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

---

## 8. Probe results (2026-10-17)

**Probe:** `Windows-Hinting/OcclusionProbe.cs` (`--probe-occlusion`) on branch `prototype/occlusion-probe`.
It runs the real `UIAutomationService.FindClickableElements` scan on one window, then scores every hint.
It was driven automatically (`--invoke <Name>` for UIA Expand/Invoke, `--keys` for SendKeys).
Machine: Win11 26200, ARM64, one display at 200% (DPI 192). Runs were DPI-unaware; two were repeated with
`--pmv2` (per-monitor v2) and gave identical counts.

**Techniques scored per hint:**
- **4.1 containers:** UIA `ControlType.Menu` / `IsDialog=true` nodes in the target tree.
- **Popup HWNDs (new):** visible, uncloaked top-level windows of the **target's process** that sit above it
  in z-order. Their rect is the occluding rect and their `ElementFromHandle` subtree is the member set.
- **4.2 WFP:** `GetAncestor(WindowFromPoint(centre), GA_ROOT)`.
- **4.3 EFP:** UIA `ElementFromPoint` plus a RuntimeId parent walk.
- **FINAL rule:**
  - suppress a hint if it is not a member of any container or popup **and** its centre lies inside one;
  - otherwise drop it if the WFP root is neither the target nor the hint's own popup HWND.

| Scenario | Hints | In popup | FINAL suppressed | Menu-only would leave | MenuOpened fired? | Popup lives in | Auto-hint signals, ms after trigger (see §9) |
|---|---|---|---|---|---|---|---|
| GitHub, no menu (baseline) | 89 | 0 | 0 | — | — | — | — |
| GitHub **"More items"** (repo nav overflow) | 93 | 3 | 1 (menu's own wrapper) | 3 | yes (`role=menu`) | same HWND | UIA `FocusChanged`→menuitem, UIA `MenuOpened`, WinEvent `MENUPOPUPSTART`, opener `Expanded` (40–90 ms) |
| GitHub hamburger (`Open menu`) | 110 | 19 | 26 | 19 | **no** (`role=dialog`, aria-modal side sheet) | same HWND | **only** UIA `FocusChanged` into the `IsDialog` node (56–77 ms) |
| GitHub branch picker (`main branch`) | 108 | 18 | 19 | 18 | yes (inner `role=menu`) | same HWND | UIA `MenuOpened` + WinEvent `MENUPOPUPSTART` + opener `Expanded` (69–122 ms); `FocusChanged`→`Filter branches` box inside the dialog (64–93 ms) |
| GitHub **Watch** (`Watch: …`) | 87 | 4 | 6 | 4 | yes (`role=menu`, `menuitemradio` items) | same HWND | UIA `MenuOpened` (21–41 ms), `FocusChanged`→`menuitemradio` (32–41), opener `Expanded` (37–43), WinEvent `MENUPOPUPSTART` (36–45) |
| GitHub green **Code** button | 96 | 0 (**no Menu/Dialog role**) | 0 (~6 background hints under the popover stay) | n/a (~14 if the popover is detected, §10) | **no** | same HWND (portal `Group`, §10) | **only** `FocusChanged`→`Local` tab inside the popover (23–33 ms), opener `Expanded` (35–37), WinEvent `OBJECT_SHOW` of a grouping node (37–41); **no** menu events |
| GitHub **+** (`Create new...`) | 91 | 8 | 3 | 8 | yes (`role=menu`) | same HWND | UIA `MenuOpened` (25–26 ms), `FocusChanged`→`New issue` (25–30), opener `Expanded` (32–40), WinEvent `MENUPOPUPSTART` (34–46) |
| Edge favorites-bar folder (`Coding`) | 94 | 4 | 9 | 4 | **no** | own popup HWND, items **also** in main tree | WinEvent `MENUSTART`/`MENUPOPUPSTART` on the popup HWND (39–43 ms); `FocusChanged` not dependable on mouse open |
| Edge `Other favorites` | 92 | 2 | 8 | 2 | no | same as above | WinEvent `MENUSTART`/`MENUPOPUPSTART` (44–51 ms); `FocusChanged` only when opened via UIA, none on mouse click |
| Edge `Settings and more` | 114 | 24 | 41 | 24 | no | same as above | WinEvent `MENUSTART`/`MENUPOPUPSTART` (52–83 ms); `FocusChanged` via UIA/Alt+F, none on mouse click |
| Edge context menu: page (Shift+F10) | 98 | 15 | 34 | 15 | no | own popup HWND, items also in main tree | WinEvent popup `OBJECT_CREATE` (41 ms), `FocusChanged`→first item (49), `MENUSTART`/`MENUPOPUPSTART` (62–65) |
| Edge context menu: page (right-click) | 98 | 15 | 21 | 15 | no | same | `OBJECT_CREATE` (23 ms), `MENUSTART`/`MENUPOPUPSTART` (33–36); **no** focus move |
| Edge context menu: link (`MIT license`) | 93 | 10 | 26 | 10 | no | same | `OBJECT_CREATE` (22 ms), `MENUSTART`/`MENUPOPUPSTART` (40–45); no focus move |
| Edge context menu: tab | 96 | 13 | 24 | 13 | no | same | `OBJECT_CREATE` (26 ms), `MENUSTART`/`MENUPOPUPSTART` (39–41); no focus move |
| Edge context menu: address bar | 92 | 9 | 25 | 9 | no | same | `FocusChanged`→the omnibox, the *opener* (69 ms); `OBJECT_CREATE` (70), `MENUSTART`/`MENUPOPUPSTART` (81–89) |
| Edge context menu: favorites item (`Coding`) | 97 | 14 | 23 | 14 | no | same | `OBJECT_CREATE` (44 ms), `MENUPOPUPSTART`/`MENUSTART` (67–68); no focus move |
| Notepad File menu (WinUI MenuFlyout) | 43 | 13 | 0 (nothing hintable beneath) | 13 | yes | `PopupWindowSiteBridge` HWND, items also in main tree | WinEvent `MENUPOPUPSTART`, UIA `MenuOpened`, `FocusChanged`→`MenuFlyoutItem` (55–166 ms) |
| Notepad context menu | 41 | 11 | 0 | 11 | yes | same as above | same three signals (52–183 ms) |
| Notepad system menu (`#32768`) | 35 | 5 | 3 | 5 | yes | `#32768` HWND | all: `MENUSTART`, `MENUPOPUPSTART`, `#32768` `OBJECT_SHOW`, UIA `MenuOpened` with a real rect, `FocusChanged` (47–53 ms) |
| Explorer context menu (Win11 XAML) | 145 | 0 (UIA) / 16 (popup) | 2 | — | yes, but sender is a 0×0 offscreen `ApplicationBar` | `PopupWindowSiteBridge` HWND | WinEvent `MENUPOPUPSTART`, UIA `MenuOpened` (`ApplicationBar`), `FocusChanged`→`AppBarButton`, all 280–380 ms (Explorer is slow to show the menu) |

### Findings

1. **Occlusion filtering alone does not fix the ticket's "More" case.**
   - The GitHub "More items" menu (240×140 px) covers almost no hints, so occlusion filtering removes 1 of 93.
   - The confusion in #123 is the 90 page hints *around* the menu, not under it.
   - Only the **menu-only policy** (§3.4, option a) fixes that: it leaves 3 hints.
   - The hamburger is the same story. It is an aria-modal side sheet with a dimming backdrop. Occlusion
     removes the 26 hints under the sheet, but 65 hints remain on the dimmed, inert-to-click page beside it.
2. **The menu's items are almost always already in our scan.**
   - Chromium Views menus (favorites, settings) live in their own popup HWND, yet appear in the Edge
     window's UIA tree as `ControlType.Menu` (`SubmenuView`).
   - The same holds for WinUI MenuFlyouts (Notepad), `#32768` (Notepad's system menu) and Explorer's XAML
     context menu.
   - So "find the open menu, keep only its subtree" works across every surface probed.
3. **Detect the menu structurally; don't rely on the `MenuOpened` event.**
   - The event fired for web `role=menu`, WinUI and `#32768` menus.
   - It did **not** fire for Edge browser-UI menus (favorites and settings) or for the GitHub hamburger
     (`role=dialog`).
   - For Explorer it fired with an unusable 0×0 sender.
   - Detection needs to combine the `Menu`/`IsDialog` container FindAll with the same-process popup-HWND
     enumeration.
4. **Naive `WindowFromPoint` is wrong on its own.**
   - It marked **every menu item** in Edge, Notepad and Explorer as "covered", because the hit root is the
     popup HWND, not the target.
   - It is only safe once hints that are members of a popup are exempted, as in the FINAL rule.
   - With that exemption and same-process popups handled as containers, WFP dropped **0 extra hints** in
     every scenario.
   - Keep WFP only as a cheap guard against *foreign* windows: the taskbar, always-on-top tools, and other
     apps' tooltips.
5. **UIA `ElementFromPoint` (4.3) is unusable as a filter, as predicted.**
   - It reported 13 of 89 hints "covered" with **no menu open**, including Edge's caption buttons.
   - It reported Chromium menu items and GitHub's own menu items as covered.
   - It cost 190–380 ms per scan, against 60–120 ms for the scan itself.
6. **Cost is small.**
   - Container FindAll: 21–35 ms (Explorer 122 ms).
   - Popup HWND enumeration plus member FindAlls: 0.3–80 ms. It is highest when WinUI shows many access-key
     tooltip `PopupHost` windows.
   - WFP: under 1 ms for about 100 hints.
   - RuntimeId per hint is probe-only overhead. Production would add it to the cache request.
   - The container FindAll is the one meaningful add-on. It could be folded into the existing scan's
     condition.
7. **Edge favorites bar specifics:**
   - The bar's own buttons are hinted normally.
   - A folder dropdown is a `Chrome_WidgetWin_1` popup owned by the Edge window. Its items are in the main
     tree and get hints.
   - Background hints under the folder dropdown: 9 for `Coding`, 8 for `Other favorites`. All of them are
     removed by the popup rule.
   - Chromium's item tooltip is a separate same-process popup (528×52 px), and it also suppressed the hints
     beneath it. That is correct, since the tooltip really covers them.
   - The chevron (`Menu containing hidden favorites`) opened only a small 130×32 popup with no Menu node, so
     no hints were affected.
8. **Small over-suppression at popup edges.**
   - Chromium popup window rects include about 15 px of drop shadow (for example, the HWND spans 300..855
     while `SubmenuView` spans 315..840).
   - So a few hints whose centre sits in the shadow, with 2 of 4 corners visible, are suppressed.
   - Fix: prefer the UIA container rect when a popup has one, or shrink the HWND rect by its non-hit-test
     margin.
9. **DPI.** UIA rects and `WindowFromPoint` coordinates agree in both unaware and per-monitor-v2 mode.
   - One gotcha: in the DPI-unaware process, rects cached on **`MenuOpened` event senders** came back in
     physical pixels while everything else was logical.
   - Production should be per-monitor v2 before relying on event rects.

### Recommendation (updated)

- **Detection.** When a scan finds an open popup, treat it as a "popup session". A popup is either a
  non-MenuBar `Menu` / `IsDialog` node with a non-empty, on-screen rect, or a visible, uncloaked
  same-process HWND above the target whose subtree contains hints.
- **Default policy: menu-only.** Hint only the popup's members. This is what actually de-clutters #123's
  examples, and it matches light-dismiss semantics: clicking elsewhere just closes the menu.
- **Fallback when the popup has no hintable members** (pure tooltips, the chevron): use occluded-only
  suppression, with the FINAL rule above.
- **Foreign windows.** Keep the WFP root check as an always-on guard.
- **Residual cases:**
  - non-modal side panels marked `IsDialog` (menu-only may be too aggressive; consider occluded-only for
    `IsDialog` without aria-modal);
  - custom popovers with neither role nor own HWND (unchanged from §6).



---

## 9. Auto-hint pathways: trigger probe (2026-10-17)

**Question.** For every popup in §8, which signal could trigger hints automatically when it opens, how
early does it arrive, and when is the popup's content ready to scan? This feeds map #44 (auto-hinting) and
#72/#77 (chained, menu-aware hinting).

**Probe.** Run `--probe-occlusion --trigger --pmv2` (file `OcclusionProbe.Trigger.cs`). Each popup was
opened by every way that applies:
- a real mouse click or right-click (`--click` / `--rclick`);
- UIA Expand/Invoke (`--invoke`), which is how our own hint activation opens things;
- the keyboard (`--keys`: `Alt+F`, `Shift+F10`, `Alt+Space`).

The probe records:
- every WinEvent from the target process;
- UIA `MenuOpened`/`Closed`, `Window_WindowOpened`, global `FocusChanged`, and changes to
  `ExpandCollapseState`;
- a readiness poll that logs popup containers, on-screen MenuItems in the tree, and same-process popup
  HWNDs whenever they change.

32 trigger runs in total (§10 adds Watch, Code and Create new; §11 adds Edge context menus); raw timelines are in `occlusion-probe-trig-*.log` next to the exe.

All times are ms after the trigger. "—" means the signal did not fire.

| Popup (openers tried) | WinEvent `MENUPOPUPSTART` | UIA `MenuOpened` | UIA `FocusChanged` into popup | Opener `Expanded` | Content scannable |
|---|---|---|---|---|---|
| GitHub "More items" (click, UIA) | 67–89 | 40–52 | 40–51 (first menuitem) | 45–55 | ~80–100 |
| GitHub hamburger (click, UIA) | — | — | 56–77 (`Close menu` in the dialog) | — | ~120–160 |
| GitHub branch picker (click, UIA) | 116–122 | 69–119 | 64–93 (`Filter branches` text box in the dialog) | 68–117 | dialog at ~120–215; list still growing until ~350 (a burst of 23 `OBJECT_SHOW`s, menu rect resized) |
| GitHub Watch (click, UIA) | 36–45 | 21–41 | 32–41 (`menuitemradio`) | 37–43 | ~61–92 |
| GitHub green Code (click, UIA) | — | — | 23–33 (`Local` tab in the popover) | 35–37 | first poll ~60–90 (no Menu/Dialog to detect; see §10) |
| GitHub + `Create new...` (click, UIA) | 34–46 | 25–26 | 25–30 (`New issue`) | 32–40 | ~72–90 |
| Edge favorites folder `Coding` (click, UIA) | 39–43 (+`MENUSTART`) | — | UIA 22; click 68 | — | ~90–110 |
| Edge `Other favorites` (click, UIA) | 49–51 (+`MENUSTART`) | — | UIA 25; **click: none** | — | ~120 |
| Edge `Settings and more` (click, UIA, Alt+F) | 54–83 (+`MENUSTART`) | — | UIA 32, Alt+F 62; **click: none** | — | ~125–150 |
| Edge context menus: page, link, tab, address bar, favorites item (right-click; page also Shift+F10) | 36–89 (+`MENUSTART`, popup HWND `OBJECT_CREATE` 22–70) | — | **only Shift+F10** (49, first item); right-click: none (address bar: the omnibox opener only) | — | ~110–215 |
| Notepad File menu (click, Alt+F) | 55–146 | 56–161 | 54–166 (`MenuFlyout`, then the first item) | — | ~250–375 (the poll itself was slow, 190–380 ms) |
| Notepad context menu (right-click, Shift+F10) | 71–168 | 52–163 | 66–183 | — | ~170–300 |
| Notepad system menu `#32768` (Alt+Space) | 51 (+`MENUSTART`, `#32768` `OBJECT_CREATE`/`SHOW`) | 52, with a real rect | 47 (`Restore`) | — | ≤170 |
| Explorer context menu (right-click on a file, Shift+F10) | 315–372 | 283–382 (`ApplicationBar`, rect sometimes 0×0) | 374–380 (`AppBarButton`) | 379–419 (`ApplicationBar`) | ~570–860 (Explorer polls take 370–475 ms) |

### Lessons for auto-hinting

1. **WinEvent `SYSTEM_MENUPOPUPSTART` is the broadest single trigger.**
   - It fired for **every menu-type popup in every opener** probed: Chromium web menus, Chromium Views
     menus, WinUI MenuFlyouts, `#32768`, and Explorer's XAML context menu.
   - The only miss was the GitHub hamburger, which is a `role=dialog`, not a menu.
   - Where it lands varies:
     - Chromium web menus: on `Chrome_RenderWidgetHostHWND`;
     - Views menus and `#32768`: on the popup HWND;
     - WinUI/XAML menus: on an `InputSiteWindowClass` with small positive object ids.
   - So treat it as "a menu opened in process P", then locate the container structurally (§8 detection)
     rather than resolving the event's object.
   - This revises #45's "legacy `#32768` / `SYSTEM_MENUSTART` only for old Win32 apps": modern Chromium and
     WinUI raise `MENUPOPUPSTART` too.
2. **UIA `MenuOpened` misses Chromium's own browser menus.**
   - It never fired for Edge's favorites or settings menus.
   - Its sender rect can be 0×0: the WinUI `MenuFlyout`, and Explorer's `ApplicationBar` on Shift+F10.
   - It is a useful second channel, but not sufficient. #77's MenuOpened-only trigger misses Edge's browser
     menus and the hamburger.
3. **UIA `FocusChanged` is the only signal for dialog-style popups, and it is not dependable for
   mouse-opened browser menus.**
   - GitHub's hamburger and branch picker move focus into the `IsDialog` node: to the close button, or to
     the filter box.
   - So "focus landed inside a new `Menu`/`IsDialog` container" is required to catch aria-modal dialogs.
   - But Edge Views menus opened by **mouse** often don't move UIA focus. That happened in 2 of 3 runs,
     including `Other favorites` and `Settings and more`. Focus therefore complements `MENUPOPUPSTART`; it
     can't replace it. This corrects the earlier claim that FocusChanged fired for every popup and opener.
4. **The recommended trigger is a union, deduplicated by container:**
   - WinEvent `MENUPOPUPSTART` (pid = foreground);
   - UIA `MenuOpened`;
   - UIA `FocusChanged` whose element has a `Menu` (non-MenuBar) or `IsDialog` ancestor.

   Each one leads to the same step: find the open popup container(s) in the foreground window and its
   same-process popup HWNDs. Deduplicate on the container's RuntimeId. The opener's
   `ExpandCollapseState→Expanded` is a confirming signal that identifies the opener, useful for chained
   hinting.
5. **Readiness varies by surface.**
   - **Chromium (web and Views), and `#32768`:** content is complete on the first scan after the signal,
     about 100–150 ms after the trigger.
   - **GitHub branch picker:** the dialog appears at about 120 ms, but its virtualized list keeps
     rendering until about 350 ms.
   - **WinUI (Notepad):** about 170–375 ms.
   - **Explorer:** the menu itself only appears 280–380 ms after the click, and it is scannable at about
     570–860 ms.
   - So use #74's progressive-reconcile settle, scoped to the container, rather than a fixed delay. A
     fixed 100 ms wait would under-scan the branch picker, WinUI and Explorer.
6. **Expect noise that the watcher must filter:**
   - **Focus storms.** WinUI re-raised `FocusChanged` on the same item about every 10 ms for around 400 ms
     (Notepad `New tab`). Explorer did the same for over a second (`View`). Deduplicate by RuntimeId.
   - **Access-key tooltips.** Alt-key opens in WinUI spawn about 14 tiny `PopupWindowSiteBridge` windows,
     each about 80×100 px.
   - **Edge chrome popups.** Edge spawns a link-hover status bubble and menu-item tooltips, all
     `Chrome_WidgetWin_1` windows from the same process.
   - **Stale focus.** In one run, focus briefly returned to the previous run's opener before the menu
     opened.
   - None of these is a popup by itself, so a new same-process HWND must be confirmed by a menu signal or
     a Menu container.
7. **Opener doesn't matter for the trigger design**, except for lesson 3's mouse-and-focus gap. Clicks,
   UIA invokes and keyboard opens produce the same `MENUPOPUPSTART`/`MenuOpened` pair. So one watcher
   serves hands-off auto-hint (#44) and chained hinting after our own activation (#72) alike.
8. **Scope the scan to the container.** It yields the menu-only hint set that §8 found is what
   de-clutters #123. It is also much cheaper on slow surfaces: Explorer's whole-window poll took 370–475
   ms.
9. **Close signals** (not timed in detail): `MENUPOPUPEND`/`MENUEND`, the container leaving the tree,
   focus leaving the container, or the popup HWND's `OBJECT_HIDE`.


---

## 10. More GitHub popups: Watch, Code, Create new (2026-10-17)

**Probe.** Same probe as §8 (occlusion) and §9 (triggers), with mouse and UIA openers. The trigger mode
now also logs two things after the popup opens:
- the opener's `ControllerFor` (`aria-controls`);
- the **ancestry of the focused element** up to the Document.

Openers are selected by prefix or Nth match (`"Watch:*"`, `"Code#3"`), because the green Code button
shares its name with the `Code` tab.

**Watch and Create new behave like "More items".** Each is a `role=menu` ActionMenu:
- all four signals fire within about 45 ms;
- the content is scannable at about 60–90 ms;
- menu-only hinting leaves 4 and 8 hints respectively.

**The green Code button is the residual case §6 predicted.** Its popover has **no** `role=menu` and no
`role=dialog`. It is a plain `group` holding an underline-nav (`Local`/`Codespaces`), the clone URLs and
download links.
- **No `MenuOpened`, no `MENUPOPUPSTART`, and no container for §8's detection.** So occlusion filtering
  does nothing: about 6 background hints under the popover stay, mixed with about 14 popover hints.
- **Only two signals fire:** `FocusChanged` into the popover (`Local`, 23–33 ms) and the opener's
  `ExpandCollapseState→Expanded` (35–37 ms). A WinEvent `OBJECT_SHOW` of a grouping node also fires at
  37–41 ms, but every GitHub popup raises that one too.

### The Primer portal pattern (all six GitHub popups)

Focus ancestry shows that **every** GitHub popup probed (More, Watch, Create new, Code, branch picker,
hamburger) is portaled to the end of the document with the same shape:

```
Document
└─ Group  rect 0×0                 ← Primer portal root (__primerPortalRoot__)
   └─ Group  rect = popup bounds   ← overlay wrapper  (hamburger: + prc-Dialog-Backdrop group, full page)
      └─ Menu | Dialog | (nothing: Code) …   ← semantic container, when the page provides one
```

- `ControllerFor` was **empty** for all six openers. GitHub doesn't wire `aria-controls`, or Chromium
  doesn't expose it here, so it can't link opener to popup.
- The **opener's `Expanded` state plus focus moving into a new Document-level sibling subtree** is
  enough to identify the popup without any role.

### Lessons (adds to §9)

1. **Opener `ExpandCollapseState→Expanded` deserves promotion from "confirming" to "trigger" for web
   content.**
   - It fired for every GitHub `aria-haspopup` button: More, Watch, Create, branch and Code.
   - It is the only *semantic* signal for role-less popovers like Code.
   - It doesn't fire for the hamburger (a dialog opener with no `aria-expanded`); FocusChanged covers that
     case.
2. **Use focus ancestry as the container fallback.**
   - When a trigger fires but no `Menu`/`IsDialog` ancestor exists, walk up from the focused element.
   - Take the top-most ancestor below the Document (or below a zero-size portal Group) that is **not** the
     page's main content subtree, and use it as the popup container.
   - For Code that yields the 1003×900 px overlay wrapper, and its subtree is exactly the popover's hints.
   - This is a heuristic for portal-based web UI libraries (Primer here; Radix/MUI/Headless UI portal the
     same way), so it should be gated:
     - only in browser documents;
     - only when the wrapper is a later sibling than the main content;
     - only when it appeared since the trigger.
3. **The hamburger's backdrop is a modality tell.** The `prc-Dialog-Backdrop` group spans the whole
   document. A full-document group sibling just above the dialog marks it as modal, which supports
   menu-only hinting for it.

**Residual (unchanged):** popovers that neither take focus nor flip an opener's `Expanded` state (for
example, hover cards) stay invisible to every trigger channel probed here.


---

## 11. Edge context menus (2026-10-17)

**Probe.** Same probe as §8 and §9. A new `--rclick` opener drives occlusion mode too. The probe
right-clicked:
- the page (body text);
- a link (`MIT license`);
- the active tab;
- the address bar;
- a favorites-bar item (`Coding`).

It also opened the page's menu with **Shift+F10**. To target the tab, the probe matched on the tab's full
name. The window's root view also starts with `B | knausj85…`, so a plain prefix match right-clicked the
page centre instead. That run is kept as the page right-click row.

**Findings.**

1. **Every Edge context menu is a Chromium Views menu.** That includes the page and link menus, which are
   browser UI, not web content.
   - It lives in its own `Chrome_WidgetWin_1` popup HWND, yet its items sit in the **main window's** UIA
     tree as a `ControlType.Menu` (`SubmenuView`).
   - The chain above it is `MenuScrollView` → a `MenuScrollViewContainer` reported as **`MenuBar`**
     (50010) → `MenuHostRootView` → the popup.
   - So §8's detection works unchanged. The "skip MenuBar" rule still holds, because the item container
     is the inner `Menu`.
   - Menu-only hinting leaves 9–15 hints instead of 92–98.
   - Occluded-only filtering hides 21–34 page hints under the menu.
2. **The triggers are identical to the favorites and settings menus in §9.**
   - The popup HWND's `OBJECT_CREATE`/`SHOW` arrives at 22–70 ms, then `SYSTEM_MENUSTART` +
     `SYSTEM_MENUPOPUPSTART` at 33–89 ms.
   - **No UIA `MenuOpened`** in any of the 12 runs (6 trigger, 6 occlusion).
3. **A mouse-opened context menu never moves focus.** UIA focus stayed on the page element (or the
   omnibox, for the address bar) in all 5 right-click runs. Only **Shift+F10** moved focus to the first
   item (`Back`). This confirms §9 lesson 3: for Edge menus, `MENUPOPUPSTART` is the only dependable
   trigger, and FocusChanged-only designs miss mouse opens entirely.
4. **They are ready about 110–215 ms after the trigger**, complete on the first scan. That is consistent
   with the other Views menus.
5. **The opener isn't identifiable from the menu.**
   - There is no `Expanded` state on anything and no `ControllerFor`.
   - For chained hinting that is fine: the menu belongs to the foreground window, and it is the newest
     `Menu` container whose popup HWND matches the `MENUPOPUPSTART` hwnd.
