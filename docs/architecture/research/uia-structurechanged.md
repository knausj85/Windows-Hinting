# Research: role for UIA `StructureChanged` in surface detection

**Ticket:** [#48](https://github.com/knausj85/Windows-Hinting/issues/48) (wayfinder research, AFK).
**Parent map:** [#44](https://github.com/knausj85/Windows-Hinting/issues/44). Builds on Prototype A
([#45](https://github.com/knausj85/Windows-Hinting/issues/45), detection mechanism) and Research B
([#46](https://github.com/knausj85/Windows-Hinting/issues/46), integration seam).

**Question:** does UIA **`StructureChanged`**
(`IUIAutomation::AddStructureChangedEventHandler` / `AutomationElement.StructureChangedEvent`,
`StructureChangeType`) earn a place in C#-native auto-hint surface detection — as a **supplement or
alternative** to the settled primary mechanism (WinEvent `EVENT_SYSTEM_FOREGROUND` +
`EVENT_OBJECT_SHOW`, disambiguated by UIA `Window_WindowOpened` / `FocusChanged`)? Prototype A never
evaluated it.

**Guardrail:** this does **not** reopen #45's primary-trigger decision. `StructureChanged` is a
tree-edit signal *inside an already-known element* — it presupposes you already hold the surface root
(which only the settled primary trigger gives you), so by construction it can only ever be a
second-stage supplement, never the thing that first tells you a surface appeared. Findings feed the
eventual spec (ticket D); they are not a #47 catalog-schema input.

---

## VERDICT (TL;DR)

**Adopt `StructureChanged` as an optional *supplement* for one role only — a content-readiness /
settle gate on the already-classified surface root — and rule it out for the other two candidate
roles.**

- **Role (a) content-readiness — ADOPT (supplement, optimization).** A **narrowly scoped**
  `StructureChanged` handler on the classified surface root (`TreeScope.Children`, element-scoped,
  established *after* the primary trigger fires and torn down on close) is a semantic
  "children are now populated → scan now" signal. It is a principled replacement for the blind
  debounce/poll that Prototype A used to work around the ~800 ms `Window_WindowOpened` lag and the
  empty-scan retries (surfaces that fire FOREGROUND/SHOW/CREATE before their items exist). It **must
  keep a timeout backstop** because emission is provider-dependent and not guaranteed.
- **Role (c) lazily-populated in-app / menu surfaces — FOLD INTO (a).** Same mechanism, same
  scoped-readiness-gate. No independent value; primary detection for in-app/Electron menus stays UIA
  `FocusChanged` (settled in #45).
- **Role (b) close/teardown detection for the overloaded `Xaml_WindowedPopupClass`/`PopupHost` —
  RULE OUT.** Docs steer window-close to `WindowClosedEvent` and note the element is invalid once the
  window is gone, so `StructureChanged`'s sender/payload is unreliable there; keep the settled
  `EVENT_OBJECT_HIDE`/`OBJECT_DESTROY` close channel. The genuine PopupHost-overload need
  (is-this-the-real-surface-or-a-precursor) is a *positive classification* problem that role (a)'s
  readiness gate actually helps with — "expected children appeared" is evidence it is the real
  surface — not a close-detection problem.
- **Not a primary trigger, not a primary for any sub-case.** It is an in-adapter optimization the
  spec *may* include; it is gated behind, and never replaces, the primary trigger.

Net: one modest, contained, additive win (cut empty-scan retries / latency on lazily-populated
surfaces), carrying three "verify in a prototype" flags (below). It does **not** change the
architecture Research B settled — it is one more event source the `SurfaceWatch` adapter marshals
onto the UI thread, exactly like the WinEvent and `FocusChanged` callbacks it already owns.

---

## 1. What `StructureChanged` is (cited facts)

### 1.1 The event and its fingerprint

- It is "the event that is raised when the UI Automation tree structure is changed";
  managed identifier `AutomationElement.StructureChangedEvent` (an `AutomationEvent`), COM event id
  `UIA_StructureChangedEventId = 20002`.
  ([managed field](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.structurechangedevent),
  [COM event ids](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-event-ids))
- Category, verbatim: "Structure change — Raised when the structure of the UI Automation tree
  changes. The structure changes when new UI items become visible, hidden, or removed on the
  desktop." ([events overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-eventsoverview))
- Provider-side, it is raised via `UiaRaiseStructureChangedEvent`; the doc's own example of a trigger
  is "child elements being added to or removed from a list box, or being expanded or collapsed in a
  tree view."
  ([UiaRaiseStructureChangedEvent](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiaraisestructurechangedevent))

### 1.2 Subscription shape (element + scope + cache + handler)

- COM signature:
  `AddStructureChangedEventHandler([in] IUIAutomationElement *element, [in] TreeScope scope, [in] IUIAutomationCacheRequest *cacheRequest, [in] IUIAutomationStructureChangedEventHandler *handler)`.
  `element` is "the UI Automation element associated with the event handler"; `scope` is "whether
  they are on the element itself, or on its ancestors and descendants"; `cacheRequest` is "a pointer
  to a cache request, or NULL if no caching is wanted."
  ([AddStructureChangedEventHandler (COM)](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-addstructurechangedeventhandler))
- Managed signature is the same axes minus the cache request:
  `Automation.AddStructureChangedEventHandler(AutomationElement element, TreeScope scope, StructureChangedEventHandler eventHandler)`.
  Microsoft's own example subscribes with **`TreeScope.Children`** on a root element:
  `Automation.AddStructureChangedEventHandler(elementRoot, TreeScope.Children, new StructureChangedEventHandler(OnStructureChanged));`
  ([AddStructureChangedEventHandler (managed)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automation.addstructurechangedeventhandler))
- `TreeScope` valid for event subscriptions: `Element` (1), `Children` (2), `Descendants` (4),
  `Subtree` (7 = Element|Children|Descendants). `Parent` and `Ancestors` are documented **"Not
  supported."** Scope "is used to specify the scope in searching for elements, subscribing to events,
  and caching." ([TreeScope](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.treescope))
  This is the primary containment lever: `Children` on one root is bounded; `Subtree`/`Descendants`
  on a busy root is not.

### 1.3 `StructureChangeType` members (what fires)

Numeric declaration order (matters if switching on the int; **not** the order used in the ticket
prompt): `ChildAdded = 0`, `ChildRemoved = 1`, `ChildrenInvalidated = 2`, `ChildrenBulkAdded = 3`,
`ChildrenBulkRemoved = 4`, `ChildrenReordered = 5`.
([StructureChangeType (COM)](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-structurechangetype),
[StructureChangeType (managed)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.structurechangetype))

- `ChildAdded` — "A child element was added." **Uniquely, this event is associated with the added
  element itself**; every other structure-change type "is always associated with the container
  element that holds the children."
- `ChildRemoved` — "A child element was removed."
- `ChildrenInvalidated` — "Child elements were invalidated... This might mean that one or more child
  elements were added or removed, or a combination of both. This value can also indicate that one
  subtree in the UI was substituted for another. For example, the entire contents of a dialog box
  changed at once, or the view of a list changed because an Explorer-type application navigated to
  another location. The exact meaning depends on the UI Automation provider implementation."
- `ChildrenBulkAdded` / `ChildrenBulkRemoved` — children added/removed "in bulk."
- `ChildrenReordered` — "The order of child elements has changed... Child elements may or may not
  have been added or removed."

**Coalescing (load-bearing for the noise profile), verbatim:** "UI Automation defines no strict rule
governing when a provider must switch from sending individual `ChildAdded` or `ChildRemoved` events
to the bulk equivalent. However, the switch typically occurs when **two to five** child elements are
added or removed at once. The bulk events help to prevent clients from being flooded by individual
`ChildAdded` and `ChildRemoved` events."
([StructureChangeType (COM)](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-structurechangetype))

### 1.4 `runtimeId` payload — and a COM/managed divergence to be aware of

- **COM handler:** `HandleStructureChangedEvent([in] IUIAutomationElement *sender, [in] StructureChangeType changeType, [in] SAFEARRAY *runtimeId)`.
  The doc is explicit: `runtimeId` "is used only when *changeType* is `StructureChangeType_ChildRemoved`;
  it is NULL for all other structure-change events." The provider-raise API says the same.
  ([HandleStructureChangedEvent](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationstructurechangedeventhandler-handlestructurechangedevent),
  [UiaRaiseStructureChangedEvent](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiaraisestructurechangedevent))
- **Managed args:** `StructureChangedEventArgs.GetRuntimeId()` is documented more broadly — "The
  return value may be the identifier of the child that was added or removed or, in the case of many
  children being added, removed, or invalidated, the identifier of the parent," and "Custom controls
  might not provide a valid runtime identifier." The managed remarks table maps a
  sender/runtime-id meaning for `ChildAdded`, `ChildRemoved`, and the bulk/invalidated types (no
  `ChildrenReordered` row).
  ([GetRuntimeId](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.structurechangedeventargs.getruntimeid),
  [StructureChangedEventHandler remarks](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.structurechangedeventhandler))
- **Implication (reasoned inference):** the two API surfaces describe the payload differently — the
  raw COM event only carries a usable id for `ChildRemoved`, whereas the managed wrapper's
  `GetRuntimeId()` is documented to return the relevant element's id more generally. **For role (a)
  we do not need the payload at all** ("something under my root changed → (re)scan"), which sidesteps
  the divergence. Any future feature that *does* depend on the removed-child id should treat the COM
  contract (id only on `ChildRemoved`) as the reliable floor and verify managed behavior in a
  prototype rather than trusting the broader managed wording. Flagged, not asserted.

---

## 2. Per-role assessment

### (a) Content-readiness — ADOPT as a scoped supplement

**The problem it targets.** Prototype A found several v1 surfaces fire FOREGROUND/SHOW/CREATE
*before* their items exist: `Window_WindowOpened` lags ~800 ms, titles are empty at `OBJECT_CREATE`,
and blind scans came back empty and had to retry (#45; map #44 "Not yet specified" latency data).
The current mitigation is a timing guess — a fixed debounce (~50 ms) plus retry.

**Why `StructureChanged` fits.** `ChildAdded` / `ChildrenBulkAdded` on the surface root is a
*semantic* "the children now exist" signal rather than a timing guess. Subscribe element-scoped on
the classified root with `TreeScope.Children` (Microsoft's own example scope, §1.2), fire the scan on
the structure change, tear the handler down on close. This directly attacks the empty-scan-retry loop
and the latency it papers over: you scan when content is actually ready, not when a clock says it
might be. For virtualized/lazily-built surfaces this is a strict improvement over polling.

**Why it stays a supplement, not a dependency.**
- It presupposes the root, which only the primary trigger yields — so it is inherently second-stage
  (guardrail preserved).
- Emission is **not guaranteed**: "Do not assume that all possible events are raised by a Microsoft
  UI Automation provider... not all property changes cause events to be raised by the standard proxy
  providers for Windows Forms and Win32 controls."
  ([events for clients](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-events-for-clients))
  So the readiness gate **must** keep the existing debounce/timeout as a backstop: scan on the first
  structure change *or* on timeout, whichever comes first.
- A single `ChildAdded` means "more children now," not "fully populated." Incremental population could
  still trip an early empty-ish scan. Mitigation: a short settle window after the first change, or
  scan-then-re-arm-if-thin. **Prototype-flag P1.**

**Verdict (a):** adopt as an in-adapter optimization on the classified surface root. It replaces the
*blind* debounce with an *evidence-gated* one; it does not remove the timeout backstop.

### (b) Close/teardown detection for `Xaml_WindowedPopupClass`/`PopupHost` — RULE OUT

- The window closing is a window-lifetime event, not a tree edit. Docs route window-close to
  `WindowClosedEvent` and warn that once the window is gone "the Microsoft UI Automation element for
  the window is no longer valid, you cannot use the `sender` parameter... use `GetRuntimeId`
  instead"; the threading page adds that a marshaled element "can become invalidated when the
  originating apartment shuts down."
  ([events for clients](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-events-for-clients),
  [threading](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading))
  So `StructureChanged` on a closing surface is exactly where its sender/payload are least reliable.
- #45 already settled the close channel as `EVENT_OBJECT_HIDE`/`OBJECT_DESTROY` (or foreground-away),
  and already ruled UIA `WindowClosed` "too noisy." Nothing about `StructureChanged` improves on that
  for teardown.
- The real PopupHost pain is that the class is **overloaded** (multiple surfaces + a precursor to
  others) and needs a secondary discriminator. That is a *positive-classification* question
  ("is this the real surface yet?"), and role (a)'s readiness gate answers it constructively — the
  expected children appearing is evidence it is the genuine surface, not a bare precursor. So the
  PopupHost value routes into (a), not into close detection.

**Verdict (b):** rule out for close/teardown. Redirect the PopupHost-overload benefit to (a).

### (c) Lazily-populated in-app / menu surfaces — FOLD INTO (a)

- Mechanically identical to (a): a scoped `ChildAdded`/`ChildrenBulkAdded` readiness gate on the
  menu/surface root. No separate design.
- Primary *detection* for in-app menus stays UIA `FocusChanged` (focus landing on a `MenuItem`),
  which #45 settled and which reaches Electron/in-DOM menus that have no OS open event. A menu's
  items may not exist at focus time, so a readiness gate could still cut an empty scan — but that is
  role (a) applied to a menu root, nothing new.
- Reliability caveat that bites menus specifically, verbatim from Microsoft's own
  `AddStructureChangedEventHandler` example: "An exception can be thrown by the UI Automation core if
  the element disappears before it can be processed -- for example, if a menu item is only briefly
  visible. This exception cannot be caught here because it crosses native/managed boundaries."
  ([AddStructureChangedEventHandler (managed)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automation.addstructurechangedeventhandler))
  Transient menus are precisely the fragile case; the adapter must tolerate the surface vanishing
  mid-processing (it already must, for every UIA call on a transient surface).

**Verdict (c):** no independent adoption; covered by (a). Whether menu providers actually raise
`StructureChanged` on their roots is **Prototype-flag P2**.

---

## 3. Cost / noise profile and threading

**Is it firehose-class like `EVENT_OBJECT_DESTROY` / `EVENT_OBJECT_FOCUS`?**
No — *provided it is used as designed*. Those two are always-on, desktop-global WinEvent hooks that
Prototype A flagged as high-volume. `StructureChanged` here is the opposite shape: **element-scoped
on a single classified surface root, `TreeScope.Children`, established only after detection and torn
down on close.** Containment levers, all documented:

- **Scope.** `TreeScope.Children` on one root is bounded to that root's immediate children;
  `Subtree`/`Descendants` on a busy or virtualized root would be high-volume and should be avoided.
  ([TreeScope](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.treescope))
- **Provider coalescing.** Individual `ChildAdded`/`ChildRemoved` collapse into bulk events at
  ~2–5 children specifically "to prevent clients from being flooded."
  ([StructureChangeType](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-structurechangetype))
- **Cache request (COM).** Supplying an `IUIAutomationCacheRequest` at subscription lets the
  readiness callback arrive with the children's properties (bounds, control type) already cached,
  avoiding per-element cross-process round-trips — a natural fit with `AutoHintSource`'s subsequent
  scan.
  ([AddStructureChangedEventHandler (COM)](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-addstructurechangedeventhandler))
- **Residual risk:** virtualized surfaces (Start menu is virtualized per #45) can churn structure as
  the user scrolls, and a still-open readiness handler would see that churn. Mitigation: fire once,
  then unsubscribe (or ignore further changes) after the first successful scan. **Prototype-flag P3**
  — measure actual event volume per v1 surface with a `Children`-scoped handler.

**Threading / marshalling (load-bearing given the target model).** The host serializes all callbacks
on the UI thread and never calls the engine from another thread (map #44; Research B #46). UIA is
explicit and non-negotiable here:

- The handler "is always called on a **non-UI thread**"; UI Automation calls are safe inside it, but
  anything touching your own app's UI "can lead to very slow performance, or even cause the
  application to stop responding" if done on the UI thread.
  ([threading](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading))
- Subscribe/unsubscribe **on a non-UI MTA thread** (CoInitializeEx `COINIT_MULTITHREADED`), not the
  UI thread; use MTA for handler threads (STA "can... prevent clients from removing event handlers");
  and do not use multiple threads to add/remove handlers.
  ([threading](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading),
  [managed threading](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-threading-issues))
- **"Adjusting an event handler from within this method is not supported"** — do not add/remove the
  StructureChanged handler from inside its own callback.
  ([HandleStructureChangedEvent](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationstructurechangedeventhandler-handlestructurechangedevent))

**Consequence for the adapter (reasoned inference).** This adds *no new* threading burden: the
`SurfaceWatch` adapter already marshals its WinEvent and `FocusChanged` callbacks onto the UI thread
before entering the funnel (Research B #46). A `StructureChanged` readiness gate is one more source
marshalled the same way — the callback arrives on the UIA MTA thread, the adapter posts a
`ShowSurfaceHints`-triggering "ready" onto the UI thread, and the re-arm/unsubscribe happens off the
callback. It fits the settled model rather than perturbing it.

---

## 4. API choice (informative, for ticket D)

Both surfaces are documented for current Windows Desktop .NET (API pages carry
`windowsdesktop-11.0` monikers), but Microsoft's conceptual pages steer readers off the managed
`System.Windows.Automation` API toward the newer COM Windows Automation API on every page checked:
"For the latest information about UI Automation, see Windows Automation API: UI Automation."
([events for clients](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-events-for-clients),
[subscribe](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/subscribe-to-ui-automation-events),
[managed AddStructureChangedEventHandler](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automation.addstructurechangedeventhandler))

This aligns with the settled architecture: Research B keeps "COM behind the seam"
(`Interop.Uia` / `UIAutomationService`). **Recommendation:** if adopted, implement role (a) via the
**COM** `IUIAutomation.AddStructureChangedEventHandler` behind the existing `Interop.Uia` seam —
consistent with the rest of the auto-hint UIA usage, and it gives the cache-request lever the managed
overload lacks.

---

## 5. Prototype-flags (for the eventual spike, not decided here)

- **P1 — "ready enough?"** Does the first `ChildAdded`/`ChildrenBulkAdded` on a surface root mean
  fully populated, or do items arrive incrementally (needing a short settle window / re-arm)?
  Docs are silent; measure.
- **P2 — do the real surfaces raise it?** Confirm the v1 surfaces (Start/Search `SearchHost`,
  `Xaml_WindowedPopupClass`/`PopupHost`, `XamlExplorerHostIslandWindow` Task View/Snap Assist,
  in-app/Electron menus) actually raise `StructureChanged` on their roots, and the latency vs. the
  ~800 ms `Window_WindowOpened` lag. Managed docs explicitly warn providers may not raise; measure
  per surface.
- **P3 — actual volume.** With a `TreeScope.Children`-scoped, fire-once handler, is event volume
  genuinely low on animated/virtualized surfaces (Start menu)? Confirm it is not accidentally
  firehose-class in practice.

---

## Sources

Primary (Microsoft Learn), all fetched for this note:

- IUIAutomation::AddStructureChangedEventHandler — https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-addstructurechangedeventhandler
- IUIAutomationStructureChangedEventHandler::HandleStructureChangedEvent — https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationstructurechangedeventhandler-handlestructurechangedevent
- StructureChangeType enum (COM) — https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-structurechangetype
- UIA event ids (UIA_StructureChangedEventId) — https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-event-ids
- Understanding Threading Issues — https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading
- Subscribing to UI Automation Events / events overview — https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-eventsoverview
- UiaRaiseStructureChangedEvent — https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiaraisestructurechangedevent
- AutomationElement.StructureChangedEvent — https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.structurechangedevent
- Automation.AddStructureChangedEventHandler (managed) — https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automation.addstructurechangedeventhandler
- StructureChangedEventHandler delegate — https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.structurechangedeventhandler
- StructureChangedEventArgs / GetRuntimeId — https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.structurechangedeventargs and https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.structurechangedeventargs.getruntimeid
- StructureChangeType enum (managed) — https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.structurechangetype
- TreeScope enum — https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.treescope
- UI Automation Events for Clients — https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-events-for-clients
- Subscribe to UI Automation Events (managed) — https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/subscribe-to-ui-automation-events
- UI Automation Threading Issues (managed) — https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-threading-issues
- Automation.RemoveAllEventHandlers — https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automation.removealleventhandlers
