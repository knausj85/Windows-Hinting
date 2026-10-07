# Research: `BoundingRectangle` change-events + OS display-change signal for the taskbar sync probe

**Ticket:** [#82](https://github.com/knausj85/Windows-Hinting/issues/82) (wayfinder research, AFK, worktree-isolated).
**Parent map:** [#81](https://github.com/knausj85/Windows-Hinting/issues/81) — persistent numbered overlay for the Win11 taskbar + system tray, event-vs-poll viability.
**Supplements** [#48](https://github.com/knausj85/Windows-Hinting/issues/48)'s `StructureChanged` note
(`docs/architecture/research/uia-structurechanged.md`, branch `research/uia-structurechanged`). The
`StructureChanged` half (icon add/remove, gates A/C) is **deferred to #48** and not re-derived here;
this note answers only the two gaps #48 did not cover: (Q1) `BoundingRectangle` as a
property-changed event for the reflow case (gate B), and (Q2) the OS display-change signal for
gate E.

---

## VERDICT (TL;DR)

**Q1 — `BoundingRectangle` property-changed is usable for the reflow case, but it is
provider-dependent, has NO documented coalescing, and the parent does NOT raise it for sliding
children — so subscribe it `TreeScope.Subtree`/`Descendants` on the icon-row container (one
subscription catching every child's move), on an MTA/non-UI thread, with a debounce + timeout
backstop.** A *moved* child raises it, not only a resized one (the property is screen coordinates,
position included). Treat it exactly like `StructureChanged` per #48: not-guaranteed emission,
element-scoped root, same threading rules, backstop mandatory. Extra risk vs `StructureChanged`: the
bulk/coalescing rule that caps the `StructureChanged` firehose does **not** exist for
property-changed, so a centered-taskbar reflow (every icon moves at once) can emit one event per
child with no OS throttling — the debounce is load-bearing, not optional.

**Q2 — Use `Microsoft.Win32.SystemEvents.DisplaySettingsChanged` as the primary "re-measure now"
trigger for gate E.** It is the lowest-friction reliable signal for a .NET process because it hosts
its *own* hidden message-pump window thread — no HWND, no WndProc, no message loop of your own
(unlike `WM_DISPLAYCHANGE`/`WM_DPICHANGED`, which require a top-level window). It covers resolution,
monitor connect/disconnect, and arrangement/scale changes. Caveats: (a) it fires *before the new
layout has fully settled*, so debounce + re-measure after a short delay; (b) run the process as
**per-monitor-DPI-aware v2** so bounds are correct physical coordinates; (c) **taskbar auto-hide and
taskbar move-to-another-edge do NOT change display settings** and will not raise it (nor
`WM_DISPLAYCHANGE`) — that sub-case needs a WinEvent (`EVENT_OBJECT_LOCATIONCHANGE` on
`Shell_TrayWnd`) or a low-frequency backstop reconcile. If the overlay owns a top-level HWND, also
handle `WM_DPICHANGED` for the drag-window-across-DPI-monitors case, which is per-window and not
guaranteed to surface as a global `DisplaySettingsChanged`.

---

## Q1 — `BoundingRectangle` as a UIA property-changed event

### 1.1 What the property is

`UIA_BoundingRectanglePropertyId` (= **30001**) "specifies the coordinates of the rectangle that
completely encloses the automation element. The rectangle is expressed in **physical screen
coordinates**." Default value `[0,0,0,0]`; and, load-bearing for the overlay: "**This property is
NULL if the item is not currently displaying a UI.**"
([Automation Element Property Identifiers](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-automation-element-propids))

Because the rectangle encodes **both position and size**, a child that merely *moves* (a centered
taskbar reflow sliding every icon left/right, no size change) changes its `BoundingRectangle` value
— so a moved child is a legitimate property-change source, not just a resized one. (The managed
identifier is `AutomationElement.BoundingRectangleProperty`; the COM id is the 30001 above.)

### 1.2 Subscription shape

**COM** — carries a cache request:
```cpp
HRESULT AddPropertyChangedEventHandler(
  [in] IUIAutomationElement                     *element,
  [in] TreeScope                                scope,
  [in] IUIAutomationCacheRequest                *cacheRequest,   // NULL if no caching wanted
  [in] IUIAutomationPropertyChangedEventHandler *handler,
  [in] SAFEARRAY                                *propertyArray   // e.g. [UIA_BoundingRectanglePropertyId]
);
```
`element` = "the UI Automation element associated with the event handler"; `scope` = "whether they
are on the element itself, or on its ancestors and children"; `cacheRequest` = "a pointer to a cache
request, or **NULL** if no caching is wanted"; `propertyArray` = "the UI Automation properties of
interest."
([IUIAutomation::AddPropertyChangedEventHandler](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-addpropertychangedeventhandler))

**Managed** — note: **no cache-request parameter** (same gap #48 flagged for the managed
`StructureChanged` overload):
```csharp
public static void AddAutomationPropertyChangedEventHandler(
    AutomationElement element,
    TreeScope scope,
    AutomationPropertyChangedEventHandler eventHandler,
    params AutomationProperty[] properties);   // e.g. AutomationElement.BoundingRectangleProperty
```
([Automation.AddAutomationPropertyChangedEventHandler](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automation.addautomationpropertychangedeventhandler))

So if the probe wants child bounds pre-populated at event time, it must use the **COM** overload with
a cache request; the managed overload cannot attach one (you would re-read `.Current.BoundingRectangle`
in-handler, which crosses the apartment boundary and can throw — see 1.5).

### 1.3 Valid `TreeScope` — and the parent-vs-child question

`TreeScope` valid for event subscription: `Element` (1), `Children` (2), `Descendants` (4),
`Subtree` (7 = Element|Children|Descendants). **`Parent` and `Ancestors` are documented "Not
supported."**
([TreeScope](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.treescope) — same
citation #48 relied on.)

Microsoft's own property-changed example subscribes with **`TreeScope.Element`** on a single element
(watching `IsEnabledProperty`).
([example](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automation.addautomationpropertychangedeventhandler))

**Does the parent raise one when children reflow, or must you subscribe per-child?** The parent's
`BoundingRectangle` changes only when the *parent's own* rectangle changes. In a Win11 centered-
taskbar reflow the icon-row container typically keeps its bounds while each button slides inside it —
so the parent will **not** raise a `BoundingRectangle` change for the children's movement. The
property-change fires on each *child* that moved (the handler's `sender` / args identify the element
that changed; see 1.4). This matches the MSAA/WinEvent analog `EVENT_OBJECT_LOCATIONCHANGE`, whose
docs state it verbatim: "This event is generated in response to a change in the top-level object
within the object hierarchy; **it is not generated for any children** that the object might have. For
example, if the user resizes a window, the system sends this notification for the window, but not for
the menu bar, title bar, scroll bar, or other objects that have also changed."
([Event Constants](https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants))

**Design consequence:** do **not** rely on one subscription on the parent. Either (a) subscribe
`TreeScope.Subtree` (or `Descendants`) on the icon-row container so a single registration catches
every descendant child's `BoundingRectangle` change, or (b) subscribe per-child `TreeScope.Element`.
Option (a) is one registration and survives children being added/removed under it; option (b) must
be re-armed as children come and go. Probe 1 should try (a) first and measure volume.

### 1.4 Event args — identifying which element moved

`AutomationPropertyChangedEventArgs` exposes `Property` (which `AutomationProperty` changed),
`OldValue`, and `NewValue`. The element that raised it is the handler's `sender` argument
(`src as AutomationElement` in MS's example; "The element that raised the event can be identified by
its runtime ID property").
([AutomationPropertyChangedEventArgs](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationpropertychangedeventargs),
[example](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automation.addautomationpropertychangedeventhandler))
So unlike `StructureChanged`'s `ChildRemoved`-only runtimeId quirk (#48), property-changed always
hands you the moved element directly plus its old/new rectangle — no payload ambiguity for the glue
step.

### 1.5 Reliability, provider-dependence, noise

- **Not guaranteed / provider-selective.** Providers "raise events selectively, depending on whether
  any clients are subscribed."
  ([Events Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-eventsoverview))
  And explicitly for the managed stack: "**Do not assume that all possible events are raised** by a
  Microsoft UI Automation provider. For example, **not all property changes cause events to be raised
  by the standard proxy providers for Windows Forms and Win32 controls.**"
  ([UI Automation Events for Clients](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-events-for-clients))
  This is the same "emission is provider-dependent and NOT guaranteed" posture #48 found for
  `StructureChanged` — so the same conclusion applies: **a timeout/debounce backstop is mandatory**,
  and Probe 1 must empirically confirm the real Win11 `Shell_TrayWnd`/taskbar provider actually emits
  `BoundingRectangle` changes for sliding buttons (open question — the taskbar is WinUI/XAML, not a
  Win32 proxy).

- **Noise — fires even when nothing meaningfully changed.** `UIA_AutomationPropertyChangedEventId` is
  explicitly listed among events that "may be raised even when the state of the UI has not changed"
  ("depending on the property that has changed"); "it may be necessary for a client application to
  check whether anything has actually changed before taking action."
  ([Events Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-eventsoverview))
  Mitigation: compare `OldValue`/`NewValue` and ignore no-op rectangles.

- **No coalescing rule — the reflow firehose has no OS throttle.** `StructureChanged` has a documented
  bulk mechanism (providers switch to `ChildrenBulkAdded/Removed` "typically... when two to five child
  elements are added or removed at once... to prevent clients from being flooded"; see #48). **No
  equivalent coalescing is documented for property-changed events.** A centered-taskbar reflow where
  N icons all move simultaneously can therefore emit up to N separate `BoundingRectangle` changed
  events with nothing capping them. The client-side debounce/settle window is the only throttle — make
  it explicit in Probe 1 and measure the event volume per reflow (this is map #81's "actual volume"
  question P3 applied to property-changed).

### 1.6 Threading — same rules as `StructureChanged` (#48)

Confirmed identical to the `StructureChanged` constraints #48 settled:
- Make all UIA calls, and subscribe/unsubscribe, from a **separate thread that owns no windows and is
  a COM MTA thread** (`CoInitializeEx` with `COINIT_MULTITHREADED`). "Using the Single-threaded
  Apartment (STA) model can cause problems such as preventing clients from removing event handlers."
- "It is safe to make UI Automation calls in a UI Automation event handler, because the event handler
  is always called on a non-UI thread."
- "A UI Automation client should **not use multiple threads to add or remove event handlers.**"
  (Same sentence appears in the COM `AddPropertyChangedEventHandler` remarks.)
- 64-bit apartment affinity: a marshaled element "can become invalidated when the originating
  apartment shuts down... handle these issues gracefully by catching errors while using marshaled
  automation elements."

([Understanding Threading Issues](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading),
[COM AddPropertyChangedEventHandler remarks](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-addpropertychangedeventhandler),
[Events for Clients caution](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-events-for-clients))
Net: `BoundingRectangle` property-changed is one more event source the existing `SurfaceWatch`-style
adapter marshals onto the right thread, exactly like the WinEvent and `StructureChanged` callbacks —
no new threading model.

---

## Q2 — OS display-change signal for gate E

Candidates, compared for a .NET (`main`, WinForms-era) process:

### 2.1 `WM_DISPLAYCHANGE` (window message)
"Sent to all windows when the display **resolution** has changed." `wParam` = new bit depth,
`lParam` lo/hi word = horizontal/vertical resolution. "This message is **only sent to top-level
windows**." Requires an HWND with a `WindowProc` / message pump.
([WM_DISPLAYCHANGE](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-displaychange))
Fires on resolution changes; does not inherently cover DPI-only or taskbar-reposition changes.

### 2.2 `WM_DPICHANGED` (window message)
"Sent when the effective dots per inch (dpi) for a window has changed" — caused by "the window is
moved to a new monitor that has a different DPI" or "the DPI of the monitor hosting the window
changes." Carries a suggested RECT in `lParam`. **"This message is only relevant for
PROCESS_PER_MONITOR_DPI_AWARE applications or DPI_AWARENESS_PER_MONITOR_AWARE threads"** — a DPI-
unaware or system-DPI-aware process can ignore it. Win8.1+. Requires an HWND.
([WM_DPICHANGED](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged))
This is **per-window**, not global — it is the right signal for "the overlay window was dragged onto
a different-DPI monitor," which a global display-settings event may not surface.

### 2.3 `Microsoft.Win32.SystemEvents.DisplaySettingsChanged` / `DisplaySettingsChanging` (managed) — RECOMMENDED PRIMARY
`DisplaySettingsChanged`: "Occurs when the user changes the display settings." `DisplaySettingsChanging`:
"Occurs when the display settings are changing" (the *before* pre-event; `DisplaySettingsChanged` is
the *after*). Both are `static event EventHandler`.
([DisplaySettingsChanged](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.systemevents.displaysettingschanged),
[DisplaySettingsChanging](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.systemevents.displaysettingschanging))

Why it is the best fit for this process:
- **It self-hosts the message pump.** The documented exceptions reveal the mechanism:
  `ExternalException` — "The attempt to create a **system events window thread** did not succeed" —
  i.e. `SystemEvents` creates and owns a hidden window on its own thread and listens for the broadcast
  for you. **The caller needs no HWND, no WndProc, and no message loop of its own.** That is the
  decisive advantage over 2.1/2.2 for a probe/overlay that may not have a reliable top-level window on
  a pumped thread.
- Covers resolution, monitor attach/detach, and arrangement/scale changes (it wraps the same
  `WM_DISPLAYCHANGE`/settings-change broadcast at the system level).

Gotchas:
- **Static event → must detach** on dispose "or memory leaks will result."
  ([DisplaySettingsChanged remarks](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.systemevents.displaysettingschanged))
- May throw `InvalidOperationException` where "system event notifications are not supported under the
  current context" (e.g. some server/session-0 contexts) — guard it.
- The handler fires on the `SystemEvents` listener thread, not the UI thread — marshal to the overlay
  thread as needed.

### 2.4 WinEvents (`EVENT_SYSTEM_*` / `EVENT_OBJECT_*`) — no dedicated display/taskbar signal
There is **no dedicated WinEvent for "display changed" or "taskbar repositioned / auto-hidden."** The
closest is `EVENT_OBJECT_LOCATIONCHANGE` (0x800B): "An object has changed **location, shape, or
size**." Hooked (via `SetWinEventHook`) on the taskbar top-level window (`Shell_TrayWnd`) it fires
when the taskbar itself moves/resizes/auto-hides — the one path that *does* catch the auto-hide and
move-to-another-edge cases that `DisplaySettingsChanged` misses (those change no display setting). But
it is top-level-only ("not generated for any children") and noisy. `EVENT_OBJECT_REORDER`,
`EVENT_SYSTEM_MOVESIZESTART/END`, and `EVENT_SYSTEM_DESKTOPSWITCH` exist but none is a display-layout
signal.
([Event Constants](https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants))
No UIA event specifically signals taskbar reposition/auto-hide either.

### 2.5 What the Talon prototype's `screen_change` most likely maps to (inference — not an MS primary source)
Talon is not a Microsoft source, so this is reasoned inference, flagged low-confidence: Talon's
`screen_change` / screen-update callback corresponds to the **display-settings / monitor-topology
change class** — i.e. the same `WM_DISPLAYCHANGE` / `DisplaySettingsChanged` family (resolution,
monitor add/remove, arrangement), **not** taskbar auto-hide. It is a coarse "the screens changed"
notification, which is exactly why the Talon prototype still leans on its 500ms content poll for the
finer within-taskbar changes. Probe 1 should treat `DisplaySettingsChanged` as the C# equivalent of
`screen_change` and not expect it to cover auto-hide.

### 2.6 Recommendation for gate E
Primary: **`SystemEvents.DisplaySettingsChanged`** (no HWND, covers resolution/monitor/arrangement).
Run the process **per-monitor-DPI-aware v2** so `BoundingRectangle`'s physical coordinates are
correct and stable. If the overlay owns a top-level HWND, additionally handle **`WM_DPICHANGED`** for
the per-window cross-DPI-monitor drag. For **taskbar auto-hide / move-to-edge** (no display-settings
change), add either a `SetWinEventHook` `EVENT_OBJECT_LOCATIONCHANGE` on `Shell_TrayWnd` or fold it
into the low-frequency backstop reconcile. **Debounce every display signal and re-measure after a
short settle** — these events fire before the taskbar has finished relaying out (the "fires-before-
layout-settles" gotcha; `DisplaySettingsChanging` is strictly pre-change, and even
`DisplaySettingsChanged` can precede the shell's own reflow).

---

## Prototype-flags for Probe 1

- **PF-1 (does the real taskbar even emit it?).** Empirically confirm the Win11 WinUI/XAML
  `Shell_TrayWnd` taskbar provider raises `BoundingRectangle` property-changed for *sliding* buttons
  during a centered reflow (gate B). Emission is provider-dependent and not guaranteed (1.5); the
  taskbar is not a Win32 proxy control, so this cannot be assumed. If it does not fire, gate B falls
  to a backstop reconcile (and that is a fast partial NO-GO for pure-event reflow).
- **PF-2 (scope + volume).** Subscribe `TreeScope.Subtree`/`Descendants` on the icon-row container
  (one registration) vs per-child `TreeScope.Element`; measure events-per-reflow. There is **no OS
  coalescing** for property-changed (1.5) — quantify the firehose and size the debounce/settle window.
- **PF-3 (moved vs resized, no-op filter).** Verify a pure position change (not a resize) raises it,
  and filter no-op `OldValue==NewValue` rectangles (noise, 1.5).
- **PF-4 (cache vs re-read).** Managed overload has no cache request (1.2). Decide between the COM
  overload + `IUIAutomationCacheRequest` (child bounds pre-populated in-handler) vs managed +
  in-handler `.Current.BoundingRectangle` re-read (crosses apartment, can throw per 1.6 — wrap in
  try/catch).
- **PF-5 (threading).** Subscribe/handle on an MTA, window-less, single owner thread; never add/remove
  handlers from multiple threads or from inside a callback (1.6, and #48's same rule).
- **PF-6 (gate E signal).** Wire `SystemEvents.DisplaySettingsChanged` (no HWND needed); run
  per-monitor-v2 DPI-aware; debounce + re-measure-after-settle; add `Shell_TrayWnd`
  `EVENT_OBJECT_LOCATIONCHANGE` (or backstop) for auto-hide/move, which display-settings misses.

---

## Sources (every URL fetched)

- Automation Element Property Identifiers (`UIA_BoundingRectanglePropertyId` = 30001): https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-automation-element-propids
- IUIAutomation::AddPropertyChangedEventHandler (COM signature, cacheRequest, threading remark): https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-addpropertychangedeventhandler
- Automation.AddAutomationPropertyChangedEventHandler (managed signature, `TreeScope.Element` example): https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automation.addautomationpropertychangedeventhandler
- AutomationPropertyChangedEventArgs (Property/OldValue/NewValue): https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationpropertychangedeventargs
- TreeScope (Element/Children/Descendants/Subtree; Parent/Ancestors Not supported): https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.treescope
- UI Automation Events Overview (selective emission; prop-changed may fire with no real change): https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-eventsoverview
- UI Automation Events for Clients (not all property changes raise events; UI-thread caution): https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-events-for-clients
- Understanding Threading Issues (MTA, window-less thread, STA removal problem, 64-bit affinity): https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading
- WM_DISPLAYCHANGE (top-level windows only; resolution): https://learn.microsoft.com/en-us/windows/win32/gdi/wm-displaychange
- WM_DPICHANGED (per-monitor-aware only; per-window; suggested rect): https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged
- SystemEvents.DisplaySettingsChanged (self-hosted system-events window thread; static-event leak caution): https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.systemevents.displaysettingschanged
- SystemEvents.DisplaySettingsChanging (pre-change event): https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.systemevents.displaysettingschanging
- Event Constants (`EVENT_OBJECT_LOCATIONCHANGE` top-level-only; no dedicated display/taskbar WinEvent): https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants
