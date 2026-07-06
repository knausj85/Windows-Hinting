# Product Requirements Document: Keyboard Accessibility Scrolling Mode

**Version:** 1.0  
**Date:** 2025-01-16  
**Status:** Draft

---

## 1. Overview

This document defines requirements for a new **keyboard-driven scrolling mode** in Windows-Hinting, enabling users to discover and control scrollable UI elements through keyboard accessibility patterns. This mode operates independently from the existing regular hinting and taskbar hinting modes.

---

## 2. Problem Statement

### Current Limitations
- Windows-Hinting currently provides keyboard-driven activation of clickable elements (buttons, links, tabs) but does not expose scrollable regions for keyboard control.
- Users relying on keyboard navigation cannot efficiently scroll content within applications, nested panels, or list views without mouse interaction or manual tab-navigation to scrollbars.
- UI Automation provides `ScrollPattern` and `RangeValuePattern` interfaces that remain unexploited in the current implementation.

### User Need
Users require a persistent, keyboard-driven mode to:
1. Discover all scrollable regions in the foreground window
2. Select a target scrollable region by hint label
3. Control the selected region with Arrow/Page/Home/End keys and precise positioning commands
4. Maintain control until explicitly exiting the mode

---

## 3. Goals

### In-Scope
- **Dedicated scrolling mode** activated via a configurable global hotkey, separate from regular hinting
- **Discovery of scroll targets** via UI Automation `ScrollPattern` and `ScrollBar` control types
- **Two-phase interaction:**
  1. Target selection via hint labels (A-Z filtering)
  2. Persistent keyboard control of the selected target until explicit exit
- **Scroll operations:** line/page movements, absolute jumps (top/middle/bottom), and percent-based positioning
- **Visual feedback:** mode-aware overlay titles, highlighted selected target, and dimmed non-selected hints
- **Configuration:** dedicated hotkey settings in preferences

### Out-of-Scope
- Automated scroll-to-element behavior (future enhancement)
- Multi-target simultaneous scrolling
- Gesture or mouse-wheel passthrough while in scrolling mode
- Automated tests (manual verification per repository conventions)

---

## 4. User Workflow

### Activation
1. User presses the configured **scroll mode hotkey** (default: Ctrl+Alt+S, configurable)
2. System scans the foreground window for scrollable elements
3. Overlay displays hints on all discovered scroll targets
4. Overlay title updates to `Windows Scrolling Overlay [Active]`

### Target Selection
1. User types hint label characters (A-Z) to filter targets
2. Non-matching hints dim to reduced opacity but remain visible
3. User presses Space or Enter when exact match is found
4. Selected target becomes highlighted
5. Other hints remain visible at reduced opacity (e.g., 30%)
6. Overlay title updates to `Windows Scrolling Overlay [Controlling: {ElementName}]`

### Scrolling Control
While a target is selected:
- **Arrow Up/Down**: Scroll by line (vertical)
- **Arrow Left/Right**: Scroll by line (horizontal)
- **Page Up/Down**: Scroll by page (vertical)
- **Home**: Scroll to top/left
- **End**: Scroll to bottom/right
- **M** (Middle): Scroll to 50%
- **0-9**: Enter numeric percent (e.g., "25" + Enter → scroll to 25%)
- **Escape**: Return to target selection (hints remain visible) OR exit mode if no filter active

### Exit
- Press Escape when in target-selection phase (no target locked) → deactivate mode
- Press Escape when target is locked → unlock target, return to selection phase
- Press Escape again → exit scrolling mode entirely
- Press scroll mode hotkey again → toggle off (exit mode)

---

## 5. Feature Specification

### 5.1 Feature Modes

Windows-Hinting must distinguish three operational feature modes:

| Mode | Description | Entry Point |
|------|-------------|-------------|
| **Regular Hinting** | Discover and activate clickable elements | Ctrl+Alt+H (configurable) |
| **Taskbar Hinting** | Discover clickable elements in taskbar windows | Ctrl+Alt+T (configurable) |
| **Scrolling** | Discover scrollable elements, select and control via keyboard | Ctrl+Alt+S (configurable, new) |

These modes are mutually exclusive: activating one deactivates the others.

**Mode Transitions:**
- Activating scrolling mode while regular/taskbar hints are active → dismiss existing hints, enter scrolling mode
- Activating regular/taskbar mode while scrolling mode is active → exit scrolling, enter requested mode
- Mode state is independent of `HintSource` (foreground window vs taskbar)

**State Management:**
- Extend `HintStateManager` or introduce `ScrollModeStateManager` to track:
  - Current feature mode (regular hinting, taskbar hinting, scrolling)
  - Selected scroll target (when locked)
  - Numeric input buffer (for percent entry)

### 5.2 Scroll Target Discovery

#### Discovery Rules
The system must discover and hint **all** of the following elements:

1. **Elements with `ScrollPattern` available** (`UIA_IsScrollPatternAvailablePropertyId == true`)
2. **ScrollBar control types** (`UIA_ControlTypePropertyId == UIA_ScrollBarControlTypeId`)

**No deduplication:** If both a scrollable container and its scrollbar are discovered, both receive hints. Users may want direct scrollbar manipulation vs container scrolling.

#### Cached Properties
For each discovered scroll target, cache via `IUIAutomationCacheRequest`:

| Property/Pattern | Constant | Purpose |
|------------------|----------|---------|
| Bounding rectangle | `UIA_BoundingRectanglePropertyId` | Hint positioning, per-monitor filtering |
| Name | `UIA_NamePropertyId` | Overlay title when target selected |
| Control type | `UIA_ControlTypePropertyId` | Distinguish scrollbar vs container |
| ScrollPattern available | `UIA_IsScrollPatternAvailablePropertyId` | Capability check |
| RangeValuePattern available | `UIA_RangeValuePatternAvailablePropertyId` | Capability check for percent operations |
| ScrollPattern | `UIA_ScrollPatternId` | Cached pattern instance |
| RangeValuePattern | `UIA_RangeValuePatternId` | Cached pattern instance (when available) |

**Additional scroll-specific cached properties:**
- `UIA_ScrollHorizontalScrollPercentPropertyId`
- `UIA_ScrollVerticalScrollPercentPropertyId`
- `UIA_ScrollHorizontallyScrollablePropertyId`
- `UIA_ScrollVerticallyScrollablePropertyId`
- `UIA_RangeValueValuePropertyId` (for scrollbars)
- `UIA_RangeValueMinimumPropertyId`
- `UIA_RangeValueMaximumPropertyId`

#### Search Implementation
- **New method:** `IUIAutomationService.FindScrollableElements(IntPtr windowHandle)` 
- Returns `IReadOnlyList<ScrollableElement>` where `ScrollableElement` includes:
  - `IUIAutomationElement Element`
  - `Rectangle Bounds`
  - `ScrollCapabilities Capabilities` (flags for horizontal/vertical scrolling, range-value support)

### 5.3 Selection and Control Behavior

#### Phase 1: Target Selection
Reuse existing hint filtering behavior from `HintInputHandler`:
- A-Z keys append to filter text
- Backspace removes last character
- Non-matching hints fade to `TargetOpacity = 0.3f` (new: do not hide completely)
- Space/Enter commits when exactly one hint matches filter

**New behavior on commit:**
- Lock the selected scroll target
- Set `TargetOpacity = 1.0f` for selected hint
- Set `TargetOpacity = 0.3f` for all other hints (do not dismiss)
- Clear filter text
- Transition to Phase 2 (control mode)

#### Phase 2: Keyboard Control
When a target is locked, keyboard input maps to scroll operations:

| Key | Action |
|-----|--------|
| **Arrow Up** | `ScrollPattern.Scroll(ScrollAmount_SmallDecrement)` vertical |
| **Arrow Down** | `ScrollPattern.Scroll(ScrollAmount_SmallIncrement)` vertical |
| **Arrow Left** | `ScrollPattern.Scroll(ScrollAmount_SmallDecrement)` horizontal |
| **Arrow Right** | `ScrollPattern.Scroll(ScrollAmount_SmallIncrement)` horizontal |
| **Page Up** | `ScrollPattern.Scroll(ScrollAmount_LargeDecrement)` vertical |
| **Page Down** | `ScrollPattern.Scroll(ScrollAmount_LargeIncrement)` vertical |
| **Home** | `ScrollPattern.SetScrollPercent(-1, 0)` (horizontal no-op, vertical top) |
| **End** | `ScrollPattern.SetScrollPercent(-1, 100)` (vertical bottom) |
| **M** | Scroll to middle: `SetScrollPercent(-1, 50)` |
| **T** | Scroll to top: `SetScrollPercent(-1, 0)` |
| **B** | Scroll to bottom: `SetScrollPercent(-1, 100)` |
| **0-9** | Accumulate numeric input for percent entry |
| **Enter** (after numeric input) | `SetScrollPercent(-1, parsedPercent)` |
| **Escape** | Unlock target, return to Phase 1 (selection mode) |
| **Escape** (in Phase 1, no filter) | Exit scrolling mode entirely |

**Capability Fallback:**
- If `SetScrollPercent` is unavailable (no `ScrollPattern` support for absolute positioning), log warning and silently skip percent-based commands
- If only `RangeValuePattern` available (scrollbar elements), use `SetValue()` with proportional calculation

**Horizontal Scrolling:**
- Support horizontal scroll via Left/Right arrows when `ScrollHorizontallyScrollable == true`
- For percent-based commands, support horizontal overrides (future: syntax like "H25" for horizontal 25%)

### 5.4 Overlay Behavior

#### Mode-Aware Titles
`OverlayForm.Text` must reflect the current operational mode:

| State | Title |
|-------|-------|
| Inactive | `Windows Hinting Overlay` |
| Regular hinting active | `Windows Hinting Overlay [Active]` |
| Taskbar hinting active | `Windows Hinting Overlay [Taskbar]` (new) |
| Scrolling mode, no target | `Windows Scrolling Overlay [Active]` (new) |
| Scrolling mode, target locked | `Windows Scrolling Overlay [Controlling: {ElementName}]` (new) |

**Implementation:**
- Extend `OverlayForm.SetActiveState(bool active)` → `SetModeState(FeatureMode mode, string? detail)`
- Add `OverlayManager.SetModeState(...)` to propagate to all overlays

#### Selected-Target Highlighting
When a scroll target is locked:
- **Selected hint:** `CurrentOpacity = 1.0f`, render with distinct highlight color (e.g., cyan border if `ShowRectangles` is enabled, or brighter label background)
- **Other hints:** `CurrentOpacity = 0.3f` (30% opacity, not hidden)

**Current implementation gap:**
- `OverlayForm.OnPaint()` skips rendering when `TargetOpacity == 0f`
- Must update to render hints with `TargetOpacity >= 0.3f` but at reduced alpha

**Selection state:**
- Add `HintItem.IsSelected` property (or track via `HintStateManager.SelectedHint`)
- Overlay renderer checks `IsSelected` to apply highlight styling

### 5.5 Scroll Operations

#### Supported Operations

| Operation | Method | Notes |
|-----------|--------|-------|
| **Line scroll** | `IUIAutomationScrollPattern.Scroll(amount, direction)` | `amount` = SmallIncrement/SmallDecrement |
| **Page scroll** | `IUIAutomationScrollPattern.Scroll(amount, direction)` | `amount` = LargeIncrement/LargeDecrement |
| **Absolute position** | `IUIAutomationScrollPattern.SetScrollPercent(horizontal, vertical)` | Use `-1` for no-change axis |
| **Scrollbar positioning** | `IUIAutomationRangeValuePattern.SetValue(value)` | Fallback for ScrollBar elements without ScrollPattern |

#### Percent-Based Positioning
**Preset jumps:**
- **Top:** `SetScrollPercent(-1, 0)` or `SetScrollPercent(0, -1)` depending on orientation
- **Middle:** `SetScrollPercent(-1, 50)` or `SetScrollPercent(50, -1)`
- **Bottom:** `SetScrollPercent(-1, 100)` or `SetScrollPercent(100, -1)`

**Exact numeric entry:**
- User types digits (0-9) to accumulate percent value
- Display accumulated input in overlay (e.g., at top-right corner: `Jump to: 25%`)
- Press Enter to execute `SetScrollPercent(-1, enteredValue)` for vertical scroll
- Press Escape to clear numeric input buffer without scrolling

**Range validation:**
- Clamp entered percent to [0, 100]
- If user enters >100, clamp to 100 and log warning

#### Error Handling
- **Pattern unavailable:** If `ScrollPattern` not available, log error and return to selection mode
- **COM exception:** Catch `COMException` during scroll operations, log, and optionally notify user via tray icon
- **Stale element:** If element becomes invalid (window closed, element destroyed), clear selection and return to target-selection phase

---

## 6. Configuration

### Hotkey Settings
Add to `HintOverlayOptions`:

```csharp
public HotkeyConfiguration ScrollModeHotkey { get; set; } = new()
{
    Enabled = true,
    Modifiers = 0x0003, // MOD_CONTROL | MOD_ALT
    VirtualKey = 0x53   // S key
};
```

### Preferences Dialog
Add third hotkey group in `PreferencesDialog.cs`:
- **Label:** "Scrolling Mode Hotkey"
- **Checkbox:** Enable/disable
- **Recorder control:** Capture hotkey combination

### Auto-Hide Override
Scrolling mode must **opt out** of auto-hide timer:
- When `HintStateManager.CurrentMode == HintMode.Scrolling` (or equivalent), do not start `_autoHideTimer`
- User must explicitly exit via Escape or hotkey toggle

---

## 7. Edge Cases and Constraints

### Nested/Overlapping Scrollable Regions
- **Behavior:** Show hints for all discovered elements (no deduplication)
- **User experience:** Users see multiple hints in nested scenarios (e.g., outer panel + inner list + scrollbar)
- **Rationale:** User may want fine-grained control over which scrollable region to target

### Provider-Specific Limitations
- Some applications implement custom scroll controls that do not expose `ScrollPattern`
- Some scrollbars may only expose `RangeValuePattern` (e.g., legacy Win32 scrollbars wrapped by `LegacyIAccessiblePattern`)
- **Mitigation:** Cache both patterns; use `RangeValuePattern.SetValue()` as fallback for ScrollBar control types

### Multi-Monitor Scenarios
- Maintain existing per-monitor overlay architecture
- Scroll hints are filtered to intersecting screen bounds (existing `OverlayForm.SetHints` behavior)
- Selected target's element name appears in overlay title on all screens

### Stale Elements
- UI Automation elements can become stale if the underlying control is destroyed
- Before each scroll operation, validate element via `CurrentBoundingRectangle` or handle `ElementNotAvailableException`
- On failure, deselect target and return to selection mode

### Accessibility API Coverage
- Not all applications properly expose `ScrollPattern` (e.g., Chromium-based apps, legacy Win32 apps)
- **Known limitation:** Document in user-facing help that coverage depends on application's accessibility implementation

---

## 8. Acceptance Criteria

### Manual Test Scenarios

#### Scenario 1: Mode Activation
1. Open File Explorer with a scrollable folder list
2. Press Ctrl+Alt+S (scroll mode hotkey)
3. **Expected:** Overlay appears with hints on scrollable regions, title reads `Windows Scrolling Overlay [Active]`

#### Scenario 2: Target Selection
1. Activate scrolling mode in a complex window (e.g., Visual Studio with Solution Explorer, code editor, output panel)
2. Type hint label characters to filter
3. Press Space/Enter when exact match
4. **Expected:** 
   - Selected hint highlights
   - Other hints dim to 30% opacity but remain visible
   - Title updates to `Windows Scrolling Overlay [Controlling: {ElementName}]`

#### Scenario 3: Keyboard Scrolling
1. Select a scrollable target (e.g., code editor in VS)
2. Press Arrow Down 5 times
3. Press Page Down
4. Press Home
5. Press End
6. **Expected:** Editor scrolls in response to each keypress

#### Scenario 4: Percent Jump
1. Select a scrollable target
2. Type "50" then press Enter
3. **Expected:** Target scrolls to 50% position

#### Scenario 5: Multi-Monitor
1. Extend display to secondary monitor
2. Open a scrollable window spanning both monitors
3. Activate scrolling mode
4. **Expected:** Hints appear on both overlays, filtered by screen bounds

#### Scenario 6: Nested Scrollables
1. Open a browser with a page containing nested scrollable divs
2. Activate scrolling mode
3. **Expected:** Hints appear for both outer scroll container and inner scrollable regions

#### Scenario 7: Exit Behavior
1. Activate scrolling mode, select a target
2. Press Escape
3. **Expected:** Target unlocked, hints remain, user can select a different target
4. Press Escape again
5. **Expected:** Scrolling mode exits entirely, overlays dismiss hints

#### Scenario 8: Mode Switching
1. Activate regular hinting mode (Ctrl+Alt+H)
2. Press Ctrl+Alt+S (scroll mode hotkey)
3. **Expected:** Regular hints dismiss, scrolling mode activates
4. Press Ctrl+Alt+T (taskbar hotkey)
5. **Expected:** Scrolling mode exits, taskbar hints activate

#### Scenario 9: Title Persistence
1. Activate scrolling mode, select target with name "MainDocumentWell"
2. Verify overlay title on primary monitor reads `Windows Scrolling Overlay [Controlling: MainDocumentWell]`
3. Move focus to another monitor
4. **Expected:** Title remains consistent on all overlays

#### Scenario 10: Error Handling
1. Select a scrollable target
2. Close the underlying window while in control mode
3. Attempt to scroll
4. **Expected:** System logs error, deselects target, returns to selection mode

---

## 9. Implementation Notes

### Recommended File Changes

#### New Files
- `Services/ScrollModeStateManager.cs` — Tracks scroll mode state, selected target, numeric input buffer
- `Services/IUIAutomationService.cs` — Add `FindScrollableElements()` method
- `Services/ScrollController.cs` — Encapsulates scroll operation logic (pattern fallback, error handling)
- `Models/ScrollableElement.cs` — Data class for discovered scroll targets

#### Modified Files
- `HintController.cs` — Add scroll mode hotkey handler, mode transition logic
- `Services/HotkeyWindow.cs` — Register third hotkey (`SCROLL_HOTKEY_ID`)
- `Models/HintOverlayOptions.cs` — Add `ScrollModeHotkey` property
- `Services/HintState.cs` — Add feature mode tracking (or create separate state manager)
- `Forms/OverlayForm.cs` — Add `SetModeState()`, update `OnPaint()` to render dimmed hints
- `Forms/OverlayManager.cs` — Add `SetModeState()` propagation
- `Preferences/PreferencesDialog.cs` — Add scroll mode hotkey configuration UI
- `Services/UIAutomationService.cs` — Implement scroll-target discovery with extended caching

### Architecture Considerations
- **State management:** Consider separating regular hinting and scrolling workflows via distinct state managers to avoid conditional complexity
- **Pattern abstraction:** Create `IScrollController` interface with implementations for `ScrollPattern` and `RangeValuePattern` fallback paths
- **Overlay rendering:** Refactor `OverlayForm.OnPaint()` to support highlight modes (selected vs dimmed) via a rendering strategy pattern

---

## 10. Open Questions

1. **Horizontal scroll hotkeys:** Should we support dedicated H+percent syntax for horizontal positioning, or default all percent commands to vertical?
   - **Recommendation:** Default to vertical; add horizontal support in future iteration based on user feedback

2. **Taskbar compatibility:** Should scrolling mode work with `HintSource.Taskbar`?
   - **Recommendation:** Initially restrict to `ForegroundWindow` source; taskbar windows rarely contain scrollable regions worth targeting

3. **Numeric input UI:** Where should the "Jump to: X%" display appear?
   - **Recommendation:** Top-right corner of overlay, or as overlay title suffix: `[Controlling: Editor] Jump: 25`

4. **Scroll animation:** Should we honor application's native scroll animation, or force instant jumps?
   - **Recommendation:** Respect native behavior (let `ScrollPattern` implementation handle animation)

---

## 11. References

### Codebase Files
- `Windows-Hinting/HintController.cs` — Main controller for mode orchestration
- `Windows-Hinting/Services/HintState.cs` — Current state management
- `Windows-Hinting/Services/UIAutomationService.cs` — UIA scan implementation
- `Windows-Hinting/Forms/OverlayForm.cs` — Per-monitor overlay rendering
- `Windows-Hinting/Models/HintOverlayOptions.cs` — Configuration model

### UI Automation Resources
- [IUIAutomationScrollPattern Interface](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nn-uiautomationclient-iuiautomationscrollpattern)
- [IUIAutomationRangeValuePattern Interface](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nn-uiautomationclient-iuiautomationrangevaluepattern)
- [UI Automation Control Type IDs](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controltypes)

---

## 12. Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-01-16 | AI Assistant | Initial draft based on user requirements and codebase analysis |

