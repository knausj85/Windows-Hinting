# Probe 1 findings — do the Win11 taskbar/tray parents raise StructureChanged / BoundingRectangle? (#83)

**Map:** #81 (persistent taskbar/tray numbered overlay — event-vs-poll viability).
**Ticket:** #83 (Probe 1, prototype/telemetry).
**Branch:** `prototype/taskbar-overlay-probe` (throwaway, off `main`).
**Probe:** `Windows-Hinting/TaskbarOverlayProbe.cs`, run via `Windows-Hinting.exe --probe-taskbar`.
**Host:** Win11 26200, single primary monitor, centered taskbar, per-monitor-DPI-v2.
**Raw evidence:** two runs logged to `bin/Debug/net10.0-windows/taskbar-probe-*.log`
(run 1 = ~368 s driving A/B/C + drag/right-click; run 2 = ~30 s driving gate E).
Logs are under gitignored `bin/`; the decisive lines are quoted below.

---

## Parent map (the "half the result" — what to subscribe on)

Walking `Shell_TrayWnd` (ControlView) on Win11 26200:

```
Pane  Shell_TrayWnd  "Taskbar"                                    rid=42.65756   <- ROOT
  Pane  Windows.UI.Input.InputSite.WindowClass
    Pane  Taskbar.TaskbarFrameAutomationPeer  autoId=TaskbarFrame rid=42.66074.4.1  <- APP-ROW (WinUI/XAML)
      Button ToggleButton "Start" / "Search" / "Task View"
      Button Taskbar.TaskListButtonAutomationPeer  "<app> pinned/running"   <- app buttons
      Button SystemTray.NormalButton "Show Hidden Icons" (the chevron)
      Button SystemTray.NormalButton/AccentButton/OmniButton* "<tray item>"  <- tray icons
  Pane  TrayNotifyWnd                                             rid=42.65764   <- TRAY container
  Pane  MSTaskSwWClass  "Running applications"                    rid=42.65776   <- APP-ROW (Win32 proxy)
```

Two things matter here:
- The **content** (app buttons, tray icons, Start/Search/TaskView) lives under the WinUI
  **`Taskbar.TaskbarFrameAutomationPeer`** (`rid=42.66074.*`). This is the provider that
  raises **StructureChanged**.
- There is a **separate Win32-proxy sibling `MSTaskSwWClass` "Running applications"** whose
  **BoundingRectangle** (width) is what changes on reflow. `TrayNotifyWnd` is the tray's
  equivalent container.

So the app row has *two* UIA representations and the signals split across them: structure on
the XAML peer, geometry on the Win32 pane.

---

## Per-gate × per-signal results

| Gate | Scenario | StructureChanged | BoundingRectangle | Verdict |
|------|----------|------------------|-------------------|---------|
| **A** | app launch/close | ✅ ChildAdded/Removed on TaskbarFrame + ChildrenInvalidated on root; **runtimeId usable on ChildRemoved** | ✅ but only **container** `MSTaskSwWClass`+`Shell_TrayWnd` *width* changes | present & usable |
| **B** | centered reflow (buttons slide) | ✅ only as the A/C add/remove that caused it | ❌ **zero per-button fires** — see below | **signal ABSENT at element level** |
| **C** | tray app start/exit | ✅ ChildAdded/Removed on `SystemTray.NormalButton` | ✅ container `TrayNotifyWnd` only | present & usable |
| **E** | resolution/scale change | ✅ ChildrenInvalidated on root (settled, ~400–600 ms late) | ✅ `Shell_TrayWnd`/`TrayNotifyWnd` re-read with correct new bounds | present & usable |

### The PF-1 answer: per-element reflow does NOT raise BoundingRectangle

Across the full ~368 s run, **BoundingRectangle never fired on a single
`Taskbar.TaskListButtonAutomationPeer` or `SystemTray.*Button`.** Sender-class
distribution of all 476 BRECT fires:

```
413  DragVisualWnd            (transient drag-visual overlay window)
 23  Xaml_WindowedPopupClass  (context-menu / flyout popups)
 22  MSTaskSwWClass           (app-row container — width changes)
 11  Shell_TrayWnd            (root taskbar pane — width changes)
  6  TrayNotifyWnd            (tray container)
  1  ?                        (uncached)
```

When an app is added/removed the only geometry signal is the **container pane's width**
changing (e.g. `MSTaskSwWClass` `w=528 -> 572 -> 590` as buttons appear; `546` on removal).
The individual buttons that slide to new x-positions emit **nothing**.

This also explains the hand-driven observations:
- **Dragging a taskbar icon → no events** (the reorder is invisible until drop, when it
  surfaces as `ChildrenInvalidated`). **Right-clicking a taskbar icon → no events.**
- **Dragging a tray icon → a flood** of `DragVisualWnd` BoundingRectangle fires (a throwaway
  drag overlay, 100+ fires at 13–50 ms, no coalescing — confirms #82) — useless for sync.
- **Right-click empty taskbar / Start → `PopupHost`/`MenuFlyout` ChildAdded** (transient,
  out of scope — those surfaces belong to maps #44/#72).

### Gate E detail (run 2)

```
+19089ms [DISPLAY] SystemEvents.DisplaySettingsChanged  (primary now 1877x1012)   <- scale change
+19275ms [BRECT] TrayNotifyWnd   bounds y 944->964
+19691ms [BRECT] Shell_TrayWnd   new full-width bounds w=1877   (~600 ms AFTER the DISPLAY signal)
+19748ms [STRUCT] ChildrenInvalidated on Shell_TrayWnd
+26649ms [DISPLAY] SystemEvents.DisplaySettingsChanged  (primary now 1857x992)    <- revert
+26892ms [BRECT] TrayNotifyWnd / Shell_TrayWnd re-read with reverted bounds
```

`SystemEvents.DisplaySettingsChanged` works with no HWND/WndProc (self-hosted pump, per #82).
It is an **early trigger** — the taskbar's own settled geometry (`Shell_TrayWnd` BRECT +
`ChildrenInvalidated`) lands a few hundred ms later. Both paths drive a re-read; the parents
re-read cleanly with correct new geometry.

### Noise / volume (P3) and dedup

- **Baseline is silent** at rest (first fire only on first interaction). No phantom events.
- In-scope content changes are modest but **duplicated**: each real add fires `ChildAdded`
  2–4× for the same element/runtimeId (e.g. one "Word" launch → `#35–#38` all `rid=3.91`).
  → **dedup/reconcile by RuntimeId is mandatory** (same conclusion as chained-hinting #74).
- The large raw counts (613 STRUCT / 476 BRECT) are dominated by **out-of-scope transient
  surfaces** (drag visuals, context menus, tooltips), not taskbar content.

### Transient HOVER surfaces also fire — must be filtered by identity, not just debounced

Merely moving the mouse over the taskbar/tray generates structure events on the *same* parents
we subscribe to, with **no content change at all**:
- **Hover a tray icon → a `ToolTip` appears/disappears**: `ChildAdded`/`ChildRemoved`,
  controlType `50022` (`UIA_ToolTipControlTypeId`), on the `TaskbarFrameAutomationPeer`
  (20 such fires in run 1). Start/Search/Task View hover tooltips look the same.
- **Hover a taskbar app button → the window-preview thumbnail** appears as an
  `Xaml_WindowedPopupClass` / `PopupHost` / `PopupRoot` windowed popup — the **same class
  family** as the right-click context-menu flyout, so it can't be told apart by *where* it
  fires, only by *what* it is.

Implication for Probe 2a: a timing debounce alone is not enough — these fire on bare hover and
would trigger needless re-reads (and worse, the overlay itself may sit over the taskbar and
provoke hovers). The reconcile must **filter structure/geometry events by sender identity**:
react only when the sender is a real content element — `Taskbar.TaskListButtonAutomationPeer`
(app row) or a `SystemTray.*Button` (tray) — or one of the container panes
(`MSTaskSwWClass` / `TrayNotifyWnd` / `Shell_TrayWnd`). **Ignore** senders of controlType
ToolTip (50022), Menu/MenuItem (50009/50011), and class `Xaml_WindowedPopupClass` / `PopupRoot`
/ `PopupHost` / `DragVisualWnd` — those are transient overlays owned by maps #44/#72, never
persistent-overlay content.

---

## Verdict / off-ramp call

**NOT the fast NO-GO** ("signals absent on both parents"). Events reliably tell you **when**
the taskbar changed — `StructureChanged` ChildAdded/Removed/Invalidated on the content parents
(gates A, C), container-width `BoundingRectangle` on reflow, and the display signal (gate E).
That is enough to **retire the Talon 500 ms hot poll**: scan on change, not on a timer.

**But there is no per-element move event** (gate B absent at element level), so events can
never *follow* a button to its new position. Every change event must trigger a **full re-read
of all child bounds** to reposition overlays.

So:
- ❌ **NO-GO on "pure per-element event tracking."**
- ✅ **Conditional GO on "event-triggered full re-read reconcile."** → proceed to Probe 2a.

### Steer for Probe 2a (#84) — architecture is now fixed

1. **Trigger on**: `StructureChanged` (any ChildAdded/Removed/Invalidated) on the content
   parent, **and** `BoundingRectangle` on the container panes (`MSTaskSwWClass` /
   `TrayNotifyWnd` / `Shell_TrayWnd`), **and** `SystemEvents.DisplaySettingsChanged`.
2. **React with**: a debounced **full re-enumerate + re-read of child bounds**, then reconcile
   overlays **by RuntimeId** (reuse, move, add, remove). Not an event-follow.
3. **Filter by sender identity, then debounce** (see "Transient HOVER surfaces" above):
   react only to real content senders (`TaskListButtonAutomationPeer`, `SystemTray.*Button`)
   or the container panes; **ignore** ToolTip (50022), Menu/MenuItem (50009/50011), and
   `Xaml_WindowedPopupClass`/`PopupRoot`/`PopupHost`/`DragVisualWnd` — bare hover alone raises
   ToolTip + window-preview events on the subscribed parents. Then dedup the 2–4× duplicate
   structure fires, swallow the `DragVisualWnd` BRECT flood, and wait out the ~400–600 ms
   display-settle lag before the gate-E re-read.
4. Subscribe `Subtree` on `Shell_TrayWnd` once per signal (blankets both parents cleanly);
   MTA thread + cache request (per #48/#82) as this probe did.

### Residual risk (for #86 / Probe 2b)

A pure reposition with **no** add/remove and **no** container-width change — e.g. taskbar
"labels/combine" mode where a running window's title change resizes one button and slides its
neighbours — may move elements with **no** event at all. Not exercised here (gate F, rare/manual).
If it proves real, the reconcile needs a low-frequency backstop re-read (the "event+backstop"
option b) for that case only — still not the hot poll.
