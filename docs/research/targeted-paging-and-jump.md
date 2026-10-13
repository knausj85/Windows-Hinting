# Targeted Paging & Jump: Page Up/Down and Home/End for a *Picked*, Non-Focused Control

**Research question:** How far can we send the equivalent of **Page Up / Page Down** — and **hop to start / end** (Home/End → ToStart/ToEnd), plus "anything similar" (line steps, scroll-item-into-view, scroll-to-percent, scroll-by-pixel) — to a **specific control the user picked**, which is generally **not** the keyboard-focused control and **not** under the mouse cursor? Worked examples: the **Slack PWA** (Edge-installed web app) and **VS Code** (Electron), whose panes expose **no UIA ScrollPattern** in the prototype's census and are currently driven only by a parked-cursor wheel.

**Status:** Read-only research against primary sources: Chromium source (GitHub mirror of `chromium/src` `main`, snapshot 2026-10-13 — line numbers refer to that snapshot and will drift), VS Code source (`microsoft/vscode` `main`), Microsoft Learn (UIA), Chrome DevTools Protocol spec, Electron docs, Slack Help Center. **Proven-by-source** vs **inferred** is marked inline; anything not confirmed is **UNVERIFIED**. Companion to [`cursor-free-scroll-injection.md`](cursor-free-scroll-injection.md) (targeting/VM-robustness of wheel paths) and [`smooth-scroll-mimicry.md`](smooth-scroll-mimicry.md) (wheel physics + ScrollPattern citations) — not repeated here. Context: issue #70 ("focus-based paging & absolute-jump"), prototype `prototype/ScrollSelectProbe/Program.cs` (`ReinjectAtTarget`, `Jump`, `ScrollViaPattern`).

---

## TL;DR

1. **Chromium's ScrollPattern rule is much broader than the census suggests.** Chromium exposes UIA `ScrollPattern` on **every unignored node that is an overflowing scroll container** (any `LayoutBox` whose `PaintLayerScrollableArea` `HasOverflow()`, plus the document) — **no role restriction, no feature flag, not even a "user-scrollable" check** (§1). So for Slack, whose lists scroll *natively*, a ScrollPattern-bearing node **very probably exists somewhere above/around** `c-virtual_list__scroll_container` — the probe likely isn't looking at it (it may be a role-less `generic` div the probe's walker/filter skips). **This is the #1 thing to prototype.** For VS Code, by contrast, the list widget **deliberately defeats native scrolling** (it resets the DOM `scrollTop` to 0 on every `scroll` event — `listView.ts` L454–461), so *any* DOM-scroll-based mechanism (UIA ScrollPattern, ScrollIntoView, IA2, CDP `scrollTop`) is **dead on VS Code lists by design**.
2. **Where ScrollPattern exists, it is the right tool and is fully aimed:** `Scroll(LargeIncrement)` = exactly **one visible box height** (a true page), `SmallIncrement` = 40 DIP × scale, `SetScrollPercent` 0/100 = Home/End. **Caveat (source-proven bug trap):** Chromium's `SetScrollPercent` does **not** special-case `UIA_ScrollPatternNoScroll` (−1) — passing −1 for an axis that *is* scrollable moves that axis to its start. The prototype's `Jump()` passes `NoScroll` for H; harmless on V-only panes, wrong on a 2-axis pane.
3. **The surprise mechanism: IA2 `scrollToPoint`.** Reachable from our process on Edge/Chrome/Electron via MSAA → `IServiceProvider` → `IAccessible2`, it scrolls a node's ancestors so the node's **top-left lands at a given screen point**. "Take the last visible row and `scrollToPoint` it to the pane's top edge" is a **pixel-exact, focus-free, cursor-free Page Down** on any *natively-scrolling* web pane (Slack), even without ScrollPattern. Page Up is the mirror. Not usable on VS Code lists (point 1).
4. **Home/End without ScrollPattern:** iterate `ScrollItemPattern.ScrollIntoView` (supported on **every** Chromium node) on the first/last *realized* child until the list stops growing — works with virtualization because each hop realizes new rows; Chromium has **no** `ItemContainerPattern`/`VirtualizedItemPattern`, so there is no one-shot.
5. **Dead ends (source-proven):** posted `WM_VSCROLL` to Chromium is a zero-delta event (offsets never parsed); posted `WM_KEYDOWN` goes to the **focused** DOM element whatever HWND you post to; Chromium's own paging actions (`kScrollForward/Backward/Up/Down`, 4/5-page) have **no Windows call site**; `IAccessibleText::scrollSubstringTo` is `E_NOTIMPL`; MSAA `accSelect` only focuses.
6. **VS Code needs an app channel:** the only targeted path is *focus the view, then run the list/editor command* — `<viewId>.focus` + `list.focusPageDown` / `list.focusFirst` / `list.focusLast`, or for the editor `editorScroll {to, by:'page'}` / `revealLine` (which target the active editor **without** needing focus). Reachable cross-process via **pokey/command-server** (Talon precedent). Side effect: VS Code's internal view focus and list *focus row* move.
7. **CDP (`--remote-debugging-port`) is technically perfect and practically unshippable** for the Slack PWA (Chrome 136+ refuses it on the default profile; whole-browser flag) and is a security hole for VS Code — dev-only.

### Ranked recommendation (actuator ladder for Page / Home / End on a picked region)

| Rank | Lane | Use when | Page | Home/End |
| --- | --- | --- | --- | --- |
| **1** | **UIA ScrollPattern on the picked node *or its nearest scroll-container ancestor/descendant found in the RAW view*** | Any Chromium/Edge/Electron pane that scrolls natively (Slack); all native UIA panes | `Scroll(LargeIncrement/Decrement)` | `SetScrollPercent(curH, 0/100)` (pass the *current* H %, not −1) |
| **2** | **IA2 `scrollToPoint` on a boundary row** (Chromium only, via MSAA→IA2) | Natively-scrolling web pane with no ScrollPattern found | last visible row → pane top (down); first visible row → pane bottom − row h (up) | combine with rank 3 |
| **3** | **`ScrollItemPattern.ScrollIntoView` on first/last realized child, repeated until stable** | Any UIA tree that realizes children (Chromium: every node has ScrollItem) | (approximate: ScrollIntoView of the first row below the fold — centres it ≈ half page) | **Yes**, iterative for virtualized lists |
| **4** | **App command channel, focus-then-command** (VS Code via command-server; Slack via documented keys after focusing the pane) | VS Code lists/trees (custom scroll model), Slack message list when 1–3 fail | `list.focusPageDown` / Slack PageDown in message focus | `list.focusFirst/Last`; Slack Home/End |
| **5** | **Parked-cursor wheel (current)** | Last resort / continuous scroll | multi-notch ≈ page (approximate) | No (no "to end" in a wheel) |
| — | CDP `Runtime.evaluate` / `Input.dispatchMouseEvent` | Dev/test harness only | exact | exact |

### Capability matrix

Legend: ✅ proven by source (or documented) · 🟡 inferred / conditional · ❌ proven not to work · ❓ UNVERIFIED, needs prototype.

| Mechanism | Reaches picked non-focused control | Page | Start/End | Slack PWA | VS Code sidebar list/tree | VS Code tab bar / editor | Side effects |
| --- | --- | --- | --- | --- | --- | --- | --- |
| UIA `ScrollPattern.Scroll` / `SetScrollPercent` | ✅ element-addressed | ✅ Large = one visible box | ✅ 0/100 | 🟡 likely on an unfound ancestor/descendant scroller (§1.4) | ❌ list resets native scroll (§1.5) | ✅ tab bar & editor expose it (census) | Fires DOM `scroll`; none to focus/selection |
| UIA `ScrollItemPattern.ScrollIntoView` | ✅ element-addressed | 🟡 ~half page (centre alignment) | ✅ iterative on realized edge item | 🟡 rows exist as Tree/List items ❓ | ❌ native scroll reset | 🟡 tab item (reveals tab) | None to focus; skips if already visible |
| IA2 `scrollTo(IA2_SCROLL_TYPE_*)` | ✅ | same as ScrollIntoView (Chromium always centres) | ✅ iterative | 🟡 ❓ | ❌ | 🟡 | None |
| **IA2 `scrollToPoint`** | ✅ | ✅ pixel-exact via boundary row | 🟡 with rank 3 | 🟡 ❓ (most promising) | ❌ | 🟡 | None |
| `ITextRangeProvider.ScrollIntoView` | ✅ (text ranges) | ❌ | 🟡 doc start/end ranges | ❌ n/a | ❌ | 🟡 Monaco renders only visible lines ❓ | None |
| ItemContainer / VirtualizedItem patterns | — | — | — | ❌ not implemented in Chromium | ❌ | ❌ | — |
| Chromium `kScrollForward/Backward/Up/Down` | ❌ no Windows call site | (4/5 page) | ❌ | ❌ | ❌ | ❌ | — |
| MSAA `accSelect(TAKEFOCUS)` / `accDoDefaultAction` | focus / click only | ❌ | ❌ | building block for focus-then-key | building block | building block | **Moves focus / clicks** |
| UIA `SetFocus` + `SendInput` PageDown/Home/End | 🟡 only after moving focus | ✅ app-native | ✅ app-native | 🟡 needs *message* focus (§5) | ✅ list focus-row moves | ✅ editor | **Focus + selection/caret/focus-row change**; must restore |
| Posted `WM_KEYDOWN` to `Chrome_RenderWidgetHostHWND` | ❌ goes to focused DOM element | — | — | ❌ | ❌ | ❌ | — |
| Posted `WM_VSCROLL(SB_PAGEDOWN/TOP/BOTTOM)` | ❌ zero-delta in Chromium | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| `SendMessage(WM_MOUSEWHEEL)` by coordinate | ❌ hover-routed (issue #70) | — | — | ❌ | ❌ | ❌ | — |
| Parked-cursor `SendInput` wheel (current) | ✅ by parking | 🟡 notches ≠ page | ❌ | ✅ | ✅ | ✅ | Cursor move (hidden) |
| VS Code command-server (`<view>.focus` + `list.*` / `editorScroll`) | 🟡 via VS Code-internal focus | ✅ | ✅ | n/a | ✅ | ✅ editor (no focus needed) | VS Code view focus + list focus-row move; window must be foreground |
| Slack documented keys (F6/Ctrl+F6 + message focus) | 🟡 via focus | ✅ | ✅ (oldest/newest) | ✅ message list only | n/a | n/a | Focus moves; `Esc` marks read |
| CDP `Runtime.evaluate` (`el.scrollBy/scrollTop`) / `Input.dispatchMouseEvent mouseWheel` | ✅ exact | ✅ | ✅ | ❌ not launchable on user's profile (§9) | 🟡 wheel yes, `scrollTop` no | ✅ | **Opens a full-control debug port** |

---

## 1. UIA ScrollPattern — exactly when Chromium exposes it

### 1.1 The gate (UIA side) — source-proven
- `AXPlatformNodeWin::GetPatternProviderFactoryMethod` (`ui/accessibility/platform/ax_platform_node_win.cc`, ~L8710–8857):
  ```cpp
  case UIA_ScrollPatternId:
    if (IsScrollable()) { return &PatternProvider<IScrollProvider>; }
  ```
  `IsPatternProviderSupported` (~L6636) just returns that factory; no role check anywhere on this case.
- `AXPlatformNodeBase::IsScrollable()` (`ui/accessibility/platform/ax_platform_node_base.cc`, ~L1031) only tests that the scroll-offset ints are **present**:
  ```cpp
  return (HasIntAttribute(kScrollXMin) && HasIntAttribute(kScrollXMax) && HasIntAttribute(kScrollX)) ||
         (HasIntAttribute(kScrollYMin) && HasIntAttribute(kScrollYMax) && HasIntAttribute(kScrollY));
  ```
  The bool `kScrollable` is **not** consulted on Windows. `IsVerticallyScrollable()` (~L1050) = `IsScrollable() && kScrollYMin < kScrollYMax`; percent getters return `UIA_ScrollPatternNoScroll` for a non-scrollable axis (`GetVerticalScrollPercent`, win.cc ~L8662).

### 1.2 The gate (Blink side) — source-proven
- `AXObject::SerializeUnignoredAttributes` (`third_party/blink/renderer/modules/accessibility/ax_object.cc`, ~L2565): `if (IsScrollableContainer()) SerializeScrollAttributes(node_data);` — unignored nodes only.
- `IsScrollableContainer()` (~L7455) = `!!GetScrollableAreaIfScrollable()`; `AXNodeObject::GetScrollableAreaIfScrollable` (`ax_node_object.cc` ~L8484): the document → `LayoutViewport()`; a `LayoutBox` → its scrollable area **if `HasOverflow()`**, else null.
- `SerializeScrollAttributes` (ax_object.cc ~L2087–2110): *"Only mark as scrollable if user has actual scrollbars to use"* sets `kScrollable = IsUserScrollable()`, then *"Provide x,y scroll info if scrollable in any way (programmatically or via user)"* — always adds `kScrollX/Y`, `…Min`, `…Max`.
- Scroll containers are never pruned as ignored: `ax_node_object.cc` ~L1553–1557 — *"scrollable containers are important for determining bounding boxes, so don't ignore them"* → `if (IsScrollableContainer()) return false;`.

**Consequence:** ScrollPattern appears on **any overflowing scroll container**, of **any role** — including a role-less `<div>` (UIA `Group`, often Chromium's `generic` role), and per the "programmatically" comment possibly even `overflow:hidden` boxes with overflow (🟡 inferred; `PaintLayerScrollableArea` creation rules not checked).

### 1.3 What the actuations do — source-proven
- `Scroll(h, v)` (win.cc ~L3004): `E_FAIL` if `!IsScrollable()`, else `kSetScrollOffset` to `CalculateUIAScrollPoint(h, v)`.
- `CalculateUIAScrollPoint` (~L589–655): **Large step = the node's clipped visible bounds** (`GetBoundsRect(kScreenDIPs, kClipped)` width/height — one full viewport, not 4/5); **Small step = `kSmallScrollIncrement` (40.0f, ~L264) × scale factor**; clamped to [min, max]. Base is the browser-side **cached** `kScrollX/Y` → rapid repeated calls before the tree update can reuse a stale base (🟡 inferred; matches the prototype's "Chromium commits async" poll in `MovedSince`).
- `SetScrollPercent(h, v)` (~L3022): `x = ClampRound(h/100*(xmax-xmin)+xmin)` for both axes, then `kSetScrollOffset`. **No special case for −1** → for a scrollable axis, −1 targets just below min (≈ start; Blink clamp UNVERIFIED). **Action for the prototype:** pass the axis's *current* percent instead of `NoScroll` on Chromium.
- Blink `AXObject::PerformAction` (ax_object.cc ~L8002) → `SetScrollOffset(…, kAbsoluteScroll)` → `area->SetScrollOffset(…, ScrollType::kProgrammatic)` (~L7506–7515): instant, fires the DOM `scroll` event (🟡). No focus/selection change.

Microsoft's contract agrees: `LargeIncrement` is *"equivalent to pressing the PAGE DOWN key … If one page is not a relevant amount … an amount equal to the current visible window"*; `SmallIncrement` *"equivalent to pressing an arrow key"* — [ScrollAmount](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-scrollamount); [Scroll control pattern guidelines](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingscroll) (non-scrollable axis → ViewSize 100, percent `UIA_ScrollPatternNoScroll`).

### 1.4 Why doesn't Slack's virtual list get ScrollPattern? (inferred — prototype it)
The probe reports `c-virtual_list__scroll_container` with **full content height (~3400 px)**. A scroll *container* has the **viewport** height; an element whose bounds equal the full content height is the **scrolled content**, not the scroller. So per §1.2 the actual scroller is most likely an **ancestor** (Slack's custom-scrollbar wrapper — class name UNVERIFIED) or the node's own parent `generic` div — and Blink must give that node scroll attributes and keep it unignored. Why the census missed it is UNVERIFIED; candidate causes, cheapest first:
1. The probe walks the **Control view** (or filters by ControlType/Name) and the scroller is a role-less `generic` Group that the walker/filter skips → walk with `RawViewWalker` / `TreeScope_Ancestors` + `IsScrollPatternAvailable`.
2. The probe checked the Tree/List itself (the content) rather than its parent.
3. Slack scrolls via `transform`/JS rather than native overflow (would make it like VS Code — then ranks 1–3 are dead for Slack too). The ~3400 px reported height is weak evidence *against* this (a JS-virtualized scroller usually reports viewport height).

The same reasoning applies to the Slack *message* list (census: "no UIA scroll signal at all").

### 1.5 Why VS Code's monaco-list doesn't (and can't usefully) — source-proven
VS Code lists/trees use a custom scroll model. `src/vs/base/browser/ui/list/listView.ts` ([main](https://github.com/microsoft/vscode/blob/main/src/vs/base/browser/ui/list/listView.ts)):
- L424–428: rows container gets `transform: translate3d(0,0,0)`, `overflow: hidden`, `contain: strict`.
- L438: wrapped in a `SmoothScrollableElement` driven by a JS `Scrollable` (custom scrollbars).
- L761: `rowsContainer.style.height = ${this._scrollHeight}px`; L975: `rowsContainer.style.top = -${renderTop}px` — scrolling is done by **moving the rows container**, not by native scroll offset.
- **L454–461 — the decisive line:**
  ```ts
  this.disposables.add(addDisposableListener(this.scrollableElement.getDomNode(), 'scroll', e => {
      // Make sure the active element is scrolled into view
      const element = (e.target as HTMLElement);
      const scrollValue = element.scrollTop;
      element.scrollTop = 0;
      if (options.scrollToActiveElement) { this.setScrollTop(this.scrollTop + scrollValue); }
  ```
  Any native scroll on the list's scrollable DOM node is **immediately reset to 0**, and only translated into a real list scroll when `scrollToActiveElement` is set (an opt-in `IListViewOptions` field, L101; not set in `platform/list/browser/listService.ts` — default off for workbench lists, 🟡).
- **Consequence:** even if Chromium *did* expose ScrollPattern on that node (its child is taller and `overflow:hidden`, so it might), `kSetScrollOffset`, `kScrollToMakeVisible`, IA2 `scrollTo*` and CDP `el.scrollTop = …` would all be **swallowed**. Only VS Code's own `Scrollable` (wheel events, keyboard, commands) moves the list. This is the root cause behind [microsoft/vscode#336483](https://github.com/microsoft/vscode/issues/336483) (open, assigned meganrogge: *"VSCode does not expose the scrollable regions via UIAutomation in the sidebars … Ideally, the appropriate regions would expose ScrollPattern objects"*) — a real fix needs VS Code to bridge the a11y scroll into `setScrollTop`, e.g. by enabling the `scrollToActiveElement`-style translation.
- The tab bar and Monaco editor *do* expose working ScrollPattern per the census (they presumably use `DomScrollableElement`/native scroll or a hidden native scroller — UNVERIFIED which), so rank 1 is fine there.

---

## 2. ScrollItemPattern.ScrollIntoView — start/end hop on realized items

- **Every** Chromium node returns `UIA_ScrollItemPatternId` unconditionally (win.cc ~L8756–8757). `ScrollIntoView()` (~L2978–2998) sends `kScrollToMakeVisible` with the node's local bounds, **centre/centre alignment**, `scroll_behavior = kDoNotScrollIfVisible`.
- Renderer: `content/renderer/accessibility/render_accessibility_impl.cc` ~L371–378 → `ScrollToMakeVisibleWithSubFocus`; Blink `OnNativeScrollToMakeVisibleWithSubFocusAction` (ax_object.cc ~L8518) → `ScrollLayoutObjectRectToVisible`, scrolling the **container chain** (`layout_object->Container()`, ~L8493), `kProgrammatic`.
- Microsoft's contract: *"Scrolls the content area of a container object in order to display the control within the visible region … will not guarantee the position"* — [IScrollItemProvider::ScrollIntoView](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-iscrollitemprovider-scrollintoview).
- **Virtualization:** UIA only sees *realized* rows. Chromium does **not** implement `ItemContainerPattern` / `VirtualizedItemPattern` (win.cc ~L8834–8850, listed under `// Not currently implemented.` → `nullptr`), so there is no `FindItemByProperty` + `Realize()` one-shot. **End** = loop { find last child (`TreeScope_Children`, last), `ScrollIntoView`, wait for structure change/RuntimeId set to settle (reuse the progressive-reconcile settle from map #72) } until the last child's RuntimeId stops changing. Home = mirror on the first child. Each hop advances by roughly the realized-window size, so a 3400 px list finishes in a few hops.
- **As a "page":** ScrollIntoView of the first row *below* the viewport centres it → ≈ half a page, not a page; and `kDoNotScrollIfVisible` makes it a no-op on a partially-visible row. Use rank 2 for exact pages.
- Side effects: none on focus/selection (🟡 — it's a programmatic scroll, no focus action).
- **VS Code lists:** ❌ (§1.5 — native scroll reset). Tab items: 🟡 should reveal the tab via native scroll if the tab strip scrolls natively.

---

## 3. IAccessible2 / MSAA on Chromium — reachable, and `scrollToPoint` is a real page primitive

### 3.1 Reachability from our process — source-proven
- `LegacyRenderWidgetHostHWND::OnGetObject` (`content/browser/renderer_host/legacy_render_widget_host_win.cc` ~L355–420): `UiaRootObjectId` → UIA provider only if `IsUiaProviderEnabled()`; **`OBJID_CLIENT` → always** `LresultFromObject(IID_IAccessible, …)`. So `AccessibleObjectFromWindow(Chrome_RenderWidgetHostHWND, OBJID_CLIENT)` (or `AccessibleObjectFromPoint` at a row's centre) yields the node's `IAccessible`, then `IServiceProvider::QueryService(IID_IAccessible, IID_IAccessible2)`.
- `AXPlatformNodeWin::QueryService` (win.cc ~L5929–5962) accepts `IID_IAccessible2`, `IID_IAccessible2_2`, `IID_IAccessibleText`, table, value. It calls `OnPropertiesUsed(WinApiType::kMsaa)` — may raise the AXMode (perf, 🟡).
- UIA is native-by-default: no `kUiaProvider` feature remains in `ui/accessibility/accessibility_features.cc`; `ui/accessibility/platform/ax_platform.h` ~L248–250: *"The UI Automation provider may be disabled if incompatible accessibility tools are detected"* (`uia_provider_enabled_ = true`). IA2/MSAA stay available alongside. Edge divergence: UNVERIFIED (shares this code).
- **Mapping a UIA element we hold to its IA2 object:** simplest is `AccessibleObjectFromPoint` at the UIA element's bounding-rect centre and verify the role/name match; `IUIAutomationLegacyIAccessiblePattern.GetIAccessible()` on Chromium's UIA provider is UNVERIFIED.

### 3.2 What the IA2 methods do — source-proven
- **`IAccessible2::scrollTo(IA2ScrollType)`** (win.cc ~L2401) → `AXPlatformNodeBase::ScrollToNode(TopLeft/BottomRight/TopEdge/…/Anywhere)` (ax_platform_node_base.cc ~L1673–1712) → `kScrollToMakeVisible` on a sub-rect — but **always centre/centre + `kDoNotScrollIfVisible`**, so the IA2 type only changes the sub-rect. Equivalent to §2.
- **`IAccessible2::scrollToPoint(coordType, x, y)`** (~L2432) → `kScrollToPoint` with a screen point → Blink `OnNativeScrollToGlobalPointAction` (ax_object.cc ~L8536) with `LeftAlways`/`TopAlways`: **scrolls the ancestor scrollers so the node's top-left sits at that point.** Page Down = `lastFullyVisibleRow.scrollToPoint(SCREEN, paneLeft, paneTop)`; Page Up = `firstVisibleRow.scrollToPoint(SCREEN, paneLeft, paneBottom − rowHeight)`. Exact, focus-free, cursor-free. Requires native scrolling (❌ VS Code lists).
- **`IAccessibleText::scrollSubstringTo` / `scrollSubstringToPoint`** (~L4917–4932): `return E_NOTIMPL;` ❌.
- **MSAA `accDoDefaultAction`** (~L1661) → `kDoDefault` → a click. **`accSelect`** (~L2058): only `SELFLAG_TAKEFOCUS` acts (`kFocus`); other flags return `S_FALSE`. Neither scrolls by itself; `accSelect(TAKEFOCUS)` is a cross-process "focus this exact node" primitive for §5 (Blink focus usually scrolls into view — UNVERIFIED for the a11y path).
- IA2 spec reference: [IAccessible2 `scrollTo` / `scrollToPoint`](https://accessibility.linuxfoundation.org/a11yspecs/ia2/docs/html/interface_i_accessible2.html).

---

## 4. Chromium accessibility actions — which are wired to Windows

`ui/accessibility/ax_enums.mojom` ~L475–500 defines `kScrollBackward/Down/Forward/Left/Right/Up` (*"Scrolls by approximately one screen … called on a node that has scrollable boolean set to true"*), `kScrollToMakeVisible`, `kScrollToPoint`, `kSetScrollOffset`, `kScrollToPositionAtRowColumn`.

| Action | Windows entry point (`ax_platform_node_win.cc`) |
| --- | --- |
| `kSetScrollOffset` | UIA `IScrollProvider::Scroll`, `SetScrollPercent` |
| `kScrollToMakeVisible` | UIA `ScrollIntoView`; IA2 `scrollTo` |
| `kScrollToPoint` | IA2 `scrollToPoint`; UIA text-range `ScrollIntoView` |
| `kScrollForward/Backward/Up/Down/Left/Right`, `kScrollToPositionAtRowColumn` | **none** |

Blink's handler for the paging actions (`AXObject::Scroll`, ax_object.cc ~L7517–7590) steps `max(ClampRound(bounds.height()*4/5), 1)` (*"This 4/5ths came from the Android implementation"*) and, behind `SynthesizedKeyboardEventsForAccessibilityActionsEnabled`, dispatches synthetic PageUp/PageDown key events — but only Android reaches it (`web_contents_accessibility_android.cc` ~L2965); ATK doesn't either. **Not reachable from a Windows client.** (They are serialized as available actions only when `IsUserScrollable()` — `SerializeActionAttributes` ~L1412 — which is moot here.)

---

## 5. Focus-then-key

**Mechanism:** move keyboard focus *into* the picked pane (UIA `SetFocus` on the pane or a row, MSAA `accSelect(SELFLAG_TAKEFOCUS)`, or the app's own pane-cycling key), `SendInput` the real PageDown/Home/End, optionally restore focus. This is what `ReinjectAtTarget` lacks: it parks the cursor, but keys follow focus (issue #70).

**Side effects (general):** the focused element changes (focus ring), which in list widgets usually also moves the **focus row / selection**; restoring requires remembering the previously focused UIA element and re-`SetFocus`ing it (and Chromium's DOM focus restore may scroll *that* element into view). Window must be foreground for `SendInput`.

**Slack (documented):** [Navigate Slack with your keyboard](https://slack.com/help/articles/115003340723-Navigate-Slack-with-your-keyboard): next section *"F6"* on desktop, *"Ctrl F6"* in Windows/Linux **browsers** (previous: *"Ctrl Shift F6"*) — whether the Edge PWA behaves as "browser" or "desktop" is UNVERIFIED. Message list: *"Move focus to the empty message field and press ↑ to start navigating between individual messages. Then … Page up or Page down to jump up or down · Home to go to the oldest message · End to go to the most recent message · Shift Page Up … start of the previous day's messages"*; threads: *"Press → to enter the thread … all of the above keys will function the same way."* Warning: *"If you're not typing a message and there aren't any menus open, pressing Esc will mark messages as read"* — never use Esc to "unfocus" in Slack. Sidebar: *"Jump to previous/next channel or DM in the sidebar — Alt ↑ / Alt ↓"* ([Slack keyboard shortcuts](https://slack.com/help/articles/201374536-Slack-keyboard-shortcuts)) — that **switches channel**, not a scroll. **Verdict:** focus-then-key gives true Page/Home/End on the Slack message list (with a focus-row side effect); for the sidebar Tree, PageDown after `SetFocus` on a tree item is plausible ARIA-tree behaviour but UNVERIFIED.

**VS Code:** `SetFocus` on a list row then PageDown hits `list.focusPageDown` (keybinding `when: WorkbenchListFocusContextKey`), which moves the list's **focus row** a page and reveals it (see §7). Works, with the focus-row/view-focus side effect.

---

## 6. Window messages to Chromium (`Chrome_RenderWidgetHostHWND`)

- **`WM_KEYDOWN`** (posted/sent): `LegacyRenderWidgetHostHWND::OnKeyboardRange` (~L434–453) forwards to the parent's `HandleKeyboardMessage` (*"We send keyboard/mouse/touch messages to the parent window"*, ~L426) → `ui/views/win/hwnd_message_handler.cc` `HandleKeyboardMessage` (~L1331) → `OnKeyEvent` (~L2281) → normal key path → renderer → **the document's focused element** (downstream routing 🟡). The HWND you post to does **not** pick the DOM target. ❌ for targeting; equivalent to focus-based `SendInput` but less faithful (no real keyboard state).
- **`WM_VSCROLL` / `WM_HSCROLL` (`SB_PAGEDOWN`, `SB_TOP`, `SB_BOTTOM`)**: mapped to `OnScrollMessage` (`hwnd_message_handler.h` ~L479–480; `.cc` ~L2861) → `ui::ScrollEvent(msg)`; but `ui/events/win/events_win_utils.cc` ~L428–433:
  ```cpp
  // TODO(ananta)
  // Support retrieving the scroll offsets from the scroll event.
  if (native_event.message == WM_VSCROLL || native_event.message == WM_HSCROLL) return true;
  ```
  Offsets are never filled → zero-delta event ❌. (`WS_VSCROLL|WS_HSCROLL` are set in `OnSize` ~L756–768 only so trackpad drivers recognise the window.)
- `WM_MOUSEWHEEL` by coordinate: already recorded in issue #70 / [`cursor-free-scroll-injection.md`](cursor-free-scroll-injection.md) §5 — hover-routed, ❌ for targeting.

---

## 7. App channels — VS Code commands (and how to reach them)

**List/tree commands** — `src/vs/workbench/browser/actions/listCommands.ts`:

| Command | Default key | Acts on |
| --- | --- | --- |
| `list.focusPageDown` / `list.focusPageUp` | PageDown / PageUp | `IListService.lastFocusedList` — moves the **focus row** a page (`widget.focusNextPage`) |
| `list.focusFirst` / `list.focusLast` | Home / End | `lastFocusedList` — moves focus row |
| `list.scrollUp` / `list.scrollDown` | Ctrl+Up / Ctrl+Down | `lastFocusedList`, `focused.scrollTop += 10` — pure scroll, no focus-row move |
| `list.scrollLeft` / `list.scrollRight` | — | `lastFocusedList` |
| `list.focusDown` / `focusUp` | ↓/↑ | accepts a numeric count arg |
| `list.scrollPageUp/Down` | — | **do not exist** |

None takes a view/list id — **the target is always the last-focused list**. So: `<viewId>.focus` first (auto-generated for every view by `registerFocusViewAction` in `src/vs/workbench/services/views/browser/viewsService.ts`: `` `${viewDescriptor.id}.focus` ``; also `workbench.action.focusSideBar` in `sidebarActions.ts`, `workbench.files.action.focusFilesExplorer` in `contrib/files/browser/fileActions.ts`), then the list command. A *pure* page-scroll without moving the focus row = `list.scrollDown` × N (10 px each — coarse, many calls) — UNVERIFIED whether `when` clauses are bypassed via `executeCommand` (standard VS Code behaviour; only keybindings check `when`).

**Editor commands** — `src/vs/editor/browser/coreCommands.ts`: `editorScroll` (*"'to': 'up', 'down' · 'by': 'line', 'wrappedLine', 'page', 'halfPage', 'editor' · 'value' · 'revealCursor'"*), `revealLine` (*"'lineNumber' … 'at': 'top', 'center', 'bottom'"*), `scrollPageDown/Up` (Alt+PageDown on Windows), `cursorTop/Bottom` (Ctrl+Home/End). Targeting: `EditorCommand.runEditorCommand` (`src/vs/editor/browser/editorExtensions.ts`) uses `getFocusedCodeEditor() || getActiveCodeEditor()` → **works without editor focus** on the active editor. `editorScroll {to:'down', by:'page'}` = page without moving the caret; `revealLine {lineNumber:1}` / `{lineNumber:N, at:'bottom'}` = Home/End without moving the caret. Tab bar: no tab-strip scroll command found (UNVERIFIED that none exists); `workbench.action.firstEditorInGroup`/`lastEditorInGroup` change the active editor (tab bar reveals it — a state change).

**Reaching commands from outside:**
- Keybindings: foreground VS Code + focus-based.
- `code` CLI: `src/vs/platform/environment/node/argv.ts` has **no** `--command`/`--execute` option ❌. `vscode://` URIs go to extension URI handlers only (no built-in run-command — UNVERIFIED).
- **[pokey/command-server](https://github.com/pokey/command-server)** (the Talon precedent): client writes `request.json` (`commandId`, `args`, `waitForFinish`, `returnCommandOutput`, `uuid`) to `<tmp>/vscode-command-server-<user>`, then presses the extension's keybinding (default Ctrl+Shift+F17); the extension reads it, runs the command, writes `response.json` (single line + trailing newline). Refuses requests older than 3 s; `command-server.allowList`/`denyList` globs. Background-window protection is *"disabled by default"*. Talon's client: `talonhub/community` `core/command_client/command_client.py` (`actions.key("ctrl-shift-f17")`). **Caveats:** VS Code window must be foreground (it's a keypress); extension must be installed (bundled with the Talon/cursorless setup many target users already have); in-window focus doesn't matter (F17 binding is global).
- **Composite for a picked VS Code sidebar view:** foreground → `<viewId>.focus` → `list.focusPageDown` / `list.focusFirst` / `list.focusLast` → optionally `workbench.action.focusActiveEditorGroup` to hand focus back. Needs a UIA→viewId map (e.g. from the pane's UIA Name/AutomationId → known view ids — UNVERIFIED how stable).

---

## 8. Wheel (current lane) — limits only

Parked-cursor `SendInput` wheel is the only mechanism that reaches every pane in both apps today. Limits: a notch is a *line* quantum, not a page (Chromium's per-notch distance and VS Code's `mouseWheelScrollSensitivity`/`fastScrollSensitivity` vary; VS Code lists do have a `scrollByPage` option — `listView.ts` L438 — which turns a wheel notch into a page but is a VS Code-side setting, UNVERIFIED as user-exposed); no "to end" semantics (would need a long burst and an end-detector); cursor-move side effect (hidden per `cursor-hide-lock-scroll-mode.md`).

---

## 9. Chrome DevTools Protocol / remote debugging

- Protocol ([devtools-protocol](https://chromedevtools.github.io/devtools-protocol/)): `Runtime.evaluate` (`el.scrollBy({top: el.clientHeight*0.9})`, `el.scrollTop = el.scrollHeight`, `el.scrollIntoView({block:'end'})` per [CSSOM View](https://drafts.csswg.org/cssom-view/)); `DOM.scrollIntoViewIfNeeded` (*"Scrolls the specified rect of the given node into view if not already visible"*); `Input.dispatchMouseEvent` `type: mouseWheel` with `x/y` *"relative to the main frame's viewport in CSS pixels"* + `deltaY` — injected into the renderer at a page point, **independent of OS cursor and focus** (🟡 inferred from semantics); `Input.synthesizeScrollGesture` (experimental, touch-synthesised); `Input.dispatchKeyEvent` → focused element only.
- **Slack PWA ❌ in practice:** [Chrome 136+](https://developer.chrome.com/blog/remote-debugging-port) ignores `--remote-debugging-port`/`-pipe` *"if attempting to debug the default Chrome data directory"* (infostealer cookie-theft mitigation) — needs a separate `--user-data-dir` = separate profile + separate Slack login; the flag also applies to the whole browser process, which the PWA (`msedge_proxy.exe --app-id=…`) shares with the user's browser. Edge inheriting the 136 restriction: UNVERIFIED by Microsoft primary docs (very likely, same Chromium code).
- **VS Code 🟡 technically:** VS Code honours `remote-debugging-port` (`SUPPORTED_ELECTRON_SWITCHES` in `src/mainImpl.ts`; `argv.json` schema *"Specifies the port to use for remote debugging"*). With it, `Input.dispatchMouseEvent mouseWheel` at the pane point works on lists; `Runtime.evaluate` `scrollTop` does **not** (§1.5) — you'd have to reach VS Code's internal APIs. Electron's `nodeCliInspect` fuse governs `--inspect`, not `--remote-debugging-port` ([Electron fuses](https://www.electronjs.org/docs/latest/tutorial/fuses)).
- **Security:** an open CDP port gives any local process arbitrary JS in the renderer — for a browser, all cookies/sessions; for VS Code, the workbench and the filesystem. Not acceptable for an end-user accessibility tool's default path. **Dev/test harness only** (e.g. as a ground-truth oracle for scroll position).

---

## 10. Other "similar" primitives

- **`ITextRangeProvider::ScrollIntoView(alignToTop)`** ([MS Learn](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-scrollintoview): TRUE → flush with top, FALSE → flush with bottom). Chromium (`ax_platform_node_textrangeprovider_win.cc` ~L1132–1230): returns `S_OK` doing nothing if both endpoints are already onscreen; otherwise `kScrollToPoint` on the lowest common ancestor. For a natively-scrolling *document* this is a Home/End primitive (`DocumentRange` → `Move` to start/end → `ScrollIntoView`). Monaco renders only visible lines into the DOM, so ranges beyond the viewport likely don't exist (UNVERIFIED) — use `editorScroll`/`revealLine` or the editor's own ScrollPattern instead.
- **SetScrollPercent on an ancestor:** when the picked node has no ScrollPattern, walking *ancestors* (raw view) to the first `IsScrollPatternAvailable` node is legitimate in Chromium (scroll containers are kept unignored, §1.2) — but stop at the pane boundary so you don't scroll the document/app shell instead (the "wheel bubbles to parent" bug class seen in VS).
- **`Element.scrollIntoViewIfNeeded`** is non-standard ([MDN](https://developer.mozilla.org/en-US/docs/Web/API/Element/scrollIntoViewIfNeeded)) — only via CDP; nothing extra over §2.
- **Electron a11y:** `app.setAccessibilitySupportEnabled` ([Electron app](https://www.electronjs.org/docs/latest/api/app)) only toggles the tree; adds no scroll surface. VS Code passes `force-renderer-accessibility` only on Linux (`src/mainImpl.ts`).

---

## Open questions / what to prototype next

1. **Slack scroller hunt (highest value, cheap):** from the `c-virtual_list__scroll_container` element and from each `p-view_contents--*` pane, walk **`RawViewWalker`** ancestors *and* children (2 levels) and log `IsScrollPatternAvailable`, `VerticalViewSize`, `VerticalScrollPercent`, bounding rect, ControlType, LocalizedControlType, ClassName. Prediction from §1.2/§1.4: a viewport-height `Group` with ScrollPattern exists. If yes → wire rank 1 for Slack sidebar, Huddles list, and message list, and `Jump()`/`ScrollViaPattern` work unchanged.
2. **Fix `SetScrollPercent(NoScroll, …)` for Chromium:** pass the current horizontal percent (read first) — Chromium doesn't special-case −1 (§1.3). Verify Blink clamping empirically.
3. **IA2 `scrollToPoint` spike** (if 1 fails, or as a page primitive anyway): `AccessibleObjectFromPoint` at the last fully-visible row → `QueryService` → `IAccessible2::scrollToPoint(IA2_COORDTYPE_SCREEN_RELATIVE, paneLeft, paneTop)`. Measure: exact page? any focus/selection change? latency? AXMode/perf impact of touching IA2.
4. **Iterative ScrollIntoView End/Home** on Slack's realized rows with RuntimeId-settle; count hops for the ~3400 px sidebar and for a long channel history (where "End" may also trigger Slack's history loading — UNVERIFIED).
5. **VS Code:** decide between (a) command-server composite (`<viewId>.focus` + `list.focus{PageDown,First,Last}` + focus restore) — needs a UIA-pane → viewId map; (b) focus-then-key via UIA `SetFocus` on a row; (c) keep wheel and document the gap pending microsoft/vscode#336483. Consider proposing upstream that ListView translate a11y-driven native scroll into `setScrollTop` (the `scrollToActiveElement` path at `listView.ts` L459 already does exactly this translation) — that alone would make ranks 1–3 work on every VS Code list.
6. **Slack PWA F6 vs Ctrl+F6** and PageDown inside the sidebar Tree after `SetFocus` — empirical.
7. **Unverified:** who calls Chromium's `DisableActiveUiaProvider`; Edge-specific divergence; whether `accSelect(TAKEFOCUS)` scrolls into view; whether `overflow:hidden` boxes get `HasOverflow` scroll areas; `executeCommand` bypassing `when` clauses; Monaco text-range behaviour beyond the viewport.

## Primary sources

- Chromium (mirror `github.com/chromium/chromium`, `main` @ 2026-10-13; canonical [source.chromium.org](https://source.chromium.org/chromium/chromium/src)): [`ui/accessibility/platform/ax_platform_node_win.cc`](https://source.chromium.org/chromium/chromium/src/+/main:ui/accessibility/platform/ax_platform_node_win.cc) · [`ax_platform_node_base.cc`](https://source.chromium.org/chromium/chromium/src/+/main:ui/accessibility/platform/ax_platform_node_base.cc) · [`ax_platform_node_textrangeprovider_win.cc`](https://source.chromium.org/chromium/chromium/src/+/main:ui/accessibility/platform/ax_platform_node_textrangeprovider_win.cc) · [`ax_platform.h`](https://source.chromium.org/chromium/chromium/src/+/main:ui/accessibility/platform/ax_platform.h) · [`ui/accessibility/ax_enums.mojom`](https://source.chromium.org/chromium/chromium/src/+/main:ui/accessibility/ax_enums.mojom) · [`third_party/blink/renderer/modules/accessibility/ax_object.cc`](https://source.chromium.org/chromium/chromium/src/+/main:third_party/blink/renderer/modules/accessibility/ax_object.cc) · [`ax_node_object.cc`](https://source.chromium.org/chromium/chromium/src/+/main:third_party/blink/renderer/modules/accessibility/ax_node_object.cc) · [`content/renderer/accessibility/render_accessibility_impl.cc`](https://source.chromium.org/chromium/chromium/src/+/main:content/renderer/accessibility/render_accessibility_impl.cc) · [`content/browser/renderer_host/legacy_render_widget_host_win.cc`](https://source.chromium.org/chromium/chromium/src/+/main:content/browser/renderer_host/legacy_render_widget_host_win.cc) · [`ui/views/win/hwnd_message_handler.cc`](https://source.chromium.org/chromium/chromium/src/+/main:ui/views/win/hwnd_message_handler.cc) · [`ui/events/win/events_win_utils.cc`](https://source.chromium.org/chromium/chromium/src/+/main:ui/events/win/events_win_utils.cc)
- VS Code (`microsoft/vscode` `main`): [`src/vs/base/browser/ui/list/listView.ts`](https://github.com/microsoft/vscode/blob/main/src/vs/base/browser/ui/list/listView.ts) · [`src/vs/workbench/browser/actions/listCommands.ts`](https://github.com/microsoft/vscode/blob/main/src/vs/workbench/browser/actions/listCommands.ts) · [`src/vs/editor/browser/coreCommands.ts`](https://github.com/microsoft/vscode/blob/main/src/vs/editor/browser/coreCommands.ts) · [`src/vs/editor/browser/editorExtensions.ts`](https://github.com/microsoft/vscode/blob/main/src/vs/editor/browser/editorExtensions.ts) · [`src/vs/workbench/services/views/browser/viewsService.ts`](https://github.com/microsoft/vscode/blob/main/src/vs/workbench/services/views/browser/viewsService.ts) · [`src/vs/platform/environment/node/argv.ts`](https://github.com/microsoft/vscode/blob/main/src/vs/platform/environment/node/argv.ts) · [`src/main.ts`/`mainImpl.ts`](https://github.com/microsoft/vscode/tree/main/src) · [microsoft/vscode#336483](https://github.com/microsoft/vscode/issues/336483)
- Microsoft Learn: [ScrollAmount](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-scrollamount) · [IScrollProvider::Scroll](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-iscrollprovider-scroll) · [IScrollProvider::SetScrollPercent](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-iscrollprovider-setscrollpercent) · [Scroll control pattern](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingscroll) · [IScrollItemProvider::ScrollIntoView](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-iscrollitemprovider-scrollintoview) · [ITextRangeProvider::ScrollIntoView](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-scrollintoview)
- IAccessible2: [IAccessible2 interface](https://accessibility.linuxfoundation.org/a11yspecs/ia2/docs/html/interface_i_accessible2.html)
- Slack Help Center: [Slack keyboard shortcuts](https://slack.com/help/articles/201374536-Slack-keyboard-shortcuts) · [Navigate Slack with your keyboard](https://slack.com/help/articles/115003340723-Navigate-Slack-with-your-keyboard)
- Talon precedent: [pokey/command-server](https://github.com/pokey/command-server) · [talonhub/community `core/command_client`](https://github.com/talonhub/community/tree/main/core/command_client)
- CDP / web / Electron: [Chrome DevTools Protocol](https://chromedevtools.github.io/devtools-protocol/) · [Changes to remote debugging switches (Chrome 136)](https://developer.chrome.com/blog/remote-debugging-port) · [CSSOM View](https://drafts.csswg.org/cssom-view/) · [MDN scrollIntoViewIfNeeded](https://developer.mozilla.org/en-US/docs/Web/API/Element/scrollIntoViewIfNeeded) · [Electron command-line switches](https://www.electronjs.org/docs/latest/api/command-line-switches) · [Electron fuses](https://www.electronjs.org/docs/latest/tutorial/fuses) · [Electron app](https://www.electronjs.org/docs/latest/api/app)
