# Prior-art: is a live, timestamped UIA/WinEvent monitor with latency / Δ-cadence already a solved wheel?

Research ticket #52 (parent map #51). Question: does an existing Windows UI Automation (UIA)
tool already provide a **live, timestamped event monitor with latency / inter-event Δ-cadence**
across **WinEvent + UIA** channels — or is that a genuine gap that justifies a new forked dev tool?

**Date:** 2026-09-09. All claims traced to primary sources (Microsoft Learn docs, the
`microsoft/accessibility-insights-windows` + `microsoft/axe-windows` source, FlaUI docs).

---

## Bottom line (TL;DR)

**It is a genuine gap.** No existing tool gives you a *live event stream with computed latency /
inter-event Δ-cadence across both WinEvent and UIA channels.* The two closest tools each miss half:

- **AccEvent** (SDK) is the only tool that watches **both** UIA events **and** WinEvents live — but
  it displays **no timing at all** (no timestamp, no latency, no delta).
- **Accessibility Insights for Windows** is the modern, Microsoft-blessed successor — but its Events
  mode is **UIA-only** and its single timing artifact is a **wall-clock time-of-day string**
  (`DateTime.Now.ToString("HH:mm:ss.fff")`). No latency, no inter-event delta, no cadence — and not
  even a monotonic clock (the string wraps at midnight and would need parsing to diff).

So "live cross-channel stream + latency/cadence analytics" is **not** an off-the-shelf wheel. The
increment a new tool would add is concrete: over Accessibility Insights, add **WinEvents + computed
Δ/latency**; over AccEvent, add **any timing at all** and a modern UI.

---

## The single most decisive fact

The Events grid in Accessibility Insights for Windows has exactly **three columns — "Time Stamp",
"Event Name", "Sender"** ([`EventRecordControl.xaml`][evx], [`Resources.resx`][resx]) — and the
timestamp is produced by the engine as a formatted **wall-clock string**, not a latency or a delta:

```csharp
// microsoft/axe-windows — src/Desktop/UIAutomation/EventHandlers/EventMessage.cs
TimeStamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
```

Confirmed by the checked-in sample recording (`WildlifeManagerTest.a11yevent`): each event is
`{ "EventId", "TimeStamp": "09:58:37.859", "Properties", "Element" }` — a time-of-day string and no
delta/latency field anywhere. ([source][a11y])

---

## Overlap map

| Tool | Live event monitor? | Channels (UIA / WinEvent) | Latency or Δ-cadence timing? | Filtering / Export |
|---|---|---|---|---|
| **Accessibility Insights for Windows** | **Yes** — "Events" mode records events from a selected element in real time ([docs][aidocs]) | **UIA only** (handlers live in `axe-windows` Desktop/UIAutomation; no WinEvent path) | **No** — per-event **wall-clock** `HH:mm:ss.fff` string only; **no latency, no inter-event delta, no cadence**, no monotonic clock ([EventMessage.cs][em]) | **Yes** — filter by event type / scope / "My Events" ([docs][aidocs]); export/load `.a11yevent` JSON ([sample][a11y]) |
| **AccEvent** (Accessible Event Watcher, SDK) | **Yes** — main window streams events as raised ([Learn][acce]) | **Both** — "UIA Events" **and** "WinEvents (In/Out of Context)" modes ([Learn][acce]) | **No** — Data view shows event ID + selected element properties; **no timestamp column, no timing** ([Learn][acce]) | **Yes** — rich UIA/WinEvent settings, scope, include/exclude by HWND; "Start Logging to File" (text) + copy selected ([Learn][acce]) |
| **Inspect.exe** (SDK) | **No** — not an event log. "Watch Focus/Caret/Cursor/Tooltips" just re-reads the focused element's *properties* (~1s refresh) ([Learn][insp]) | n/a (property inspector, UIA + MSAA) | **No** | Property display config; "Copy All" of data view. No event stream/export |
| **FlaUInspect** (FlaUI, OSS) | **No** — Hover / Selection / Focus-tracking are *selection* modes for static inspection; no event log ([repo][fla]) | n/a (UIA2/UIA3 inspector) | **No** | Find by AutomationId/Name/XPath; property grid. No event stream/export |
| **UISpy** (legacy) | Limited — deprecated; superseded by Inspect. Tree/property viewer with some event viewing, **no timing** | UIA | **No** | n/a (deprecated, not shipped in current SDK) |

---

## Detail & citations

### 1. Accessibility Insights for Windows (PRIMARY)

**Is it the official successor to Inspect / AccScope / AccEvent / AccChecker?** Yes — and the
statement lives on Microsoft's own docs for each legacy tool, not on the AI marketing site. Each
Microsoft Learn page carries an identical banner (pages dated 2025-07-14):

- Inspect: *"Inspect is a legacy tool. We recommend Accessibility Insights instead."* ([Learn][insp])
- AccScope: *"AccScope is a legacy tool. We recommend using Accessibility Insights instead."* ([Learn][accs])
- AccEvent: *"AccEvent is a legacy tool. We recommend using Accessibility Insights instead."* ([Learn][acce])
- AccChecker (UI Accessibility Checker): *"AccChecker is a legacy tool. We recommend using Accessibility Insights instead."* ([Learn][accc])

Microsoft's Engineering blog frames it as *"draws inspiration from legacy Windows accessibility
testing tools from Microsoft, building on their features … wrapped up in a modern user interface"*
([devblog][blog]). So: authoritative successor, yes.

**What the Events tab actually captures.** "Events" mode lets you *"monitor and inspect the same UI
Automation events that assistive technologies observe"* ([docs][aidocs]). The view is an Events
table + Details/Configuration tabs; you pick which UIA event types to record (defaults per control
type, or add any UIA event via "Edit My Events"). Crucially:

- **UIA events only.** The recording engine is `axe-windows` (`src/Desktop/UIAutomation/EventHandlers`);
  there is no WinEvent/MSAA hook path. This is the sharp contrast with AccEvent.
- **Grid columns = Time Stamp, Event Name, Sender** ([EventRecordControl.xaml][evx],
  [Resources.resx][resx]). No latency column, no delta column.
- **Timestamp is a wall-clock string** `HH:mm:ss.fff` via `DateTime.Now.ToString(...)`
  ([EventMessage.cs][em]). It is not monotonic, carries no date, and the tool never subtracts
  consecutive timestamps to show cadence or latency.
- **Filtering + export exist**: event-type/scope/My-Events filtering; recordings save/load as
  `.a11yevent` JSON (arrays of `{EventId, TimeStamp, Properties, Element}` — [sample][a11y]).

**Does its accessibility-testing orientation constrain it as a raw timing instrument?** Yes. The
product's center of gravity is a11y *testing* — FastPass, axe-based automated rules, tab-stop and
contrast checks — and Events mode is built to answer *"did the expected event fire for this control?"*,
not *"how fast / how often did events fire?"* The data model reflects that: it stores a human-readable
clock string, not a high-resolution or relative time base, so latency/cadence analysis was never a
design goal.

### 2. Inspect.exe (SECONDARY)

The **current** Inspect docs describe *no event-log / "Watch Events" mode*. Inspect is a per-element
property/tree inspector. Its "Watch Focus / Watch Caret / Watch Cursor / Watch Tooltips" options
just re-point Inspect at whatever element gains focus/etc. and refresh the property view (docs note
Watch Focus "causes Inspect to refresh its properties in about one second") — it is not a scrolling
event stream and has no timestamps ([Learn][insp]). For live event watching, the SDK's tool is
**AccEvent**, not Inspect. Lightweight vs. Accessibility Insights: yes (single native exe in the
SDK), but it simply doesn't do event streaming.

### 3. Quick scan of OSS UIA viewers

- **FlaUInspect** (`FlaUI/FlaUInspect`): Hover / Selection / Focus-tracking modes drive *static*
  inspection of the element under cursor/focus; property grid + find (AutomationId/Name/XPath). No
  event log, no timing ([repo][fla]). (FlaUI's *library* can subscribe to UIA events
  programmatically, but the Inspect GUI exposes no live timed event stream.)
- **UISpy**: legacy, deprecated, superseded by Inspect; not shipped in the current SDK; no timing.
- No notable OSS UIA tool surfaced that exposes a **live, timed** event stream with latency/cadence.

---

## Hands-on verify checklist (feeds sibling ticket #53 — Accessibility Insights)

Use this to confirm the doc/source findings against the running app:

1. **Install** the latest Accessibility Insights for Windows from https://accessibilityinsights.io
   (or `winget install AccessibilityInsights` if available). Note the version.
2. **Enter Events mode**: launch → select a target element (e.g. a menu/button in Notepad or
   GitHub Desktop) → switch to the **Events** view.
3. **Confirm the columns.** Verify the live grid shows exactly **Time Stamp / Event Name / Sender**.
   Confirm there is **no** Latency, Δ, Elapsed, or Inter-event column, and no per-event duration.
4. **Confirm the timestamp granularity/type.** Trigger several rapid events; verify the Time Stamp
   is a wall-clock `HH:mm:ss.fff` (time-of-day, ms) — not a relative/monotonic value and not a
   computed gap. Sub-millisecond cadence is therefore unmeasurable and midnight would wrap it.
5. **Confirm channel scope = UIA only.** Trigger something that raises MSAA/WinEvents but few/no UIA
   events; confirm those WinEvents do **not** appear (cross-check the same scenario in **AccEvent**
   with "WinEvents (Out of Context)" to see the events AI misses).
6. **Filtering.** Use Configuration / "Edit My Events" to include/exclude specific UIA event types
   and set scope (element + descendants); confirm it filters the live stream.
7. **Export/replay.** Save the recording; confirm it is a `.a11yevent` **JSON** file whose entries
   are `{EventId, TimeStamp, Properties, Element}` with the same time-of-day string (open in a text
   editor). Confirm no latency/delta is persisted; confirm you can load it back.
8. **Verdict for #53:** decide whether "UIA-only + wall-clock timestamp, no computed cadence" is
   sufficient for the auto-hinting latency work — or whether the gap (WinEvents + Δ/latency) warrants
   the forked tool.

---

## Sources (primary)

- Accessibility Insights — Event monitoring docs: https://accessibilityinsights.io/docs/windows/getstarted/eventmonitoring/
- Accessibility Insights — Overview: https://accessibilityinsights.io/docs/windows/overview/
- Engineering@Microsoft blog: https://devblogs.microsoft.com/engineering-at-microsoft/accessibility-insights-for-windows/
- `axe-windows` EventMessage.cs (TimeStamp = DateTime.Now.ToString("HH:mm:ss.fff")): https://github.com/microsoft/axe-windows/blob/main/src/Desktop/UIAutomation/EventHandlers/EventMessage.cs
- `accessibility-insights-windows` EventRecordControl.xaml (grid columns): https://github.com/microsoft/accessibility-insights-windows/blob/main/src/AccessibilityInsights.SharedUx/Controls/EventRecordControl.xaml
- `accessibility-insights-windows` Resources.resx (column header strings): https://github.com/microsoft/accessibility-insights-windows/blob/main/src/AccessibilityInsights.SharedUx/Properties/Resources.resx
- `.a11yevent` sample recording: https://github.com/microsoft/accessibility-insights-windows/blob/main/src/UITests/TestFiles/WildlifeManagerTest.a11yevent
- Inspect (Learn, "legacy … recommend Accessibility Insights"): https://learn.microsoft.com/en-us/windows/win32/winauto/inspect-objects
- AccEvent (Learn, UIA + WinEvents, "legacy"): https://learn.microsoft.com/en-us/windows/win32/winauto/accessible-event-watcher
- AccScope (Learn, "legacy"): https://learn.microsoft.com/en-us/windows/win32/winauto/accscope
- AccChecker / UI Accessibility Checker (Learn, "legacy"): https://learn.microsoft.com/en-us/windows/win32/winauto/ui-accessibility-checker
- FlaUInspect: https://github.com/FlaUI/FlaUInspect

[aidocs]: https://accessibilityinsights.io/docs/windows/getstarted/eventmonitoring/
[blog]: https://devblogs.microsoft.com/engineering-at-microsoft/accessibility-insights-for-windows/
[em]: https://github.com/microsoft/axe-windows/blob/main/src/Desktop/UIAutomation/EventHandlers/EventMessage.cs
[evx]: https://github.com/microsoft/accessibility-insights-windows/blob/main/src/AccessibilityInsights.SharedUx/Controls/EventRecordControl.xaml
[resx]: https://github.com/microsoft/accessibility-insights-windows/blob/main/src/AccessibilityInsights.SharedUx/Properties/Resources.resx
[a11y]: https://github.com/microsoft/accessibility-insights-windows/blob/main/src/UITests/TestFiles/WildlifeManagerTest.a11yevent
[insp]: https://learn.microsoft.com/en-us/windows/win32/winauto/inspect-objects
[acce]: https://learn.microsoft.com/en-us/windows/win32/winauto/accessible-event-watcher
[accs]: https://learn.microsoft.com/en-us/windows/win32/winauto/accscope
[accc]: https://learn.microsoft.com/en-us/windows/win32/winauto/ui-accessibility-checker
[fla]: https://github.com/FlaUI/FlaUInspect
