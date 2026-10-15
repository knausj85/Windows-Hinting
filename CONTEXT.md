# Windows-Hinting

A keyboard/voice-driven hinting tool for Windows: it labels on-screen UI elements so the user can click or scroll them without a mouse.

## Language

### Per-app customization

**Window rule**:
A hand-authored declaration that adjusts hinting behavior for the windows it matches. It pairs a **matcher** (which windows it applies to) with **sections** of partial overrides — where to scan, which UI Automation elements to request for click hints, how to detect and actuate scroll regions. Rules can change *what is requested from UI Automation*, not only filter what comes back.
_Avoid_: profile, per-app profile, app profile (informal only — never as a term)

**Rule section**:
One independently-overridable concern inside a **window rule** — e.g. where to scan, which elements to request for click hints, how scroll regions are detected, how scrolling is actuated. Sections are the unit of precedence: each section of the **effective policy** comes from the most specific matching rule that has that section.
_Avoid_: facet, block, category

**App definition**:
A named description of one application across all the ways it can appear (a desktop exe, an installed web app, a browser tab on its site), given as alternative matchers, any of which identifies it. **Window rules** refer to an app by its name instead of repeating how to recognise it.
_Avoid_: app alias, app group (except for a definition that deliberately names several apps, such as all Chromium browsers)

**Exclusion**:
The part of a matcher that names windows a **window rule** or **app definition** must *not* apply to, even when everything else matches — e.g. an app's own Open/Save dialogs. Exclusions never make a rule more specific.
_Avoid_: negative match, blacklist

**Effective policy**:
What scanning and actuation actually consult for one window: for each **rule section**, the one from the most specific matching **window rule** that has it, layered over the built-in defaults. Rules are never combined *within* a section.
_Avoid_: resolved profile, active rule

### Scrolling

**Scroll region**:
A part of a window that scrolls independently — a list, tree, editor or pane — found by the detector or named by a **window rule**. Shown with a number badge `1`–`9`.
_Avoid_: scroll area, pane (informal only)

**Scroll target**:
What the scroll keys currently move: the target window (always number `0`) or one **scroll region**. Exactly one is active at a time.
_Avoid_: selected region, focus

**Scroll command**:
What the user asked for, independent of how it is done: an axis, a direction, and an amount (`Line`; later `Page`, `ToStart`, `ToEnd`).
_Avoid_: wheel event, scroll action

**Actuation mechanism**:
How a **scroll command** is carried out on a target — a UI Automation scroll call, or an aimed wheel event. Only the actuator knows mechanisms; the engine speaks commands.
_Avoid_: rung (except for one step of the tap ladder), method

**Tap / hold**:
A key released within the hold threshold is a **tap** (one step). A key held past it is a **hold**: a momentum fling that glides to a stop after release.

**Aiming**:
Moving the hidden pointer onto the **scroll target** so a wheel event reaches it. The pointer stays there until the target changes or the mode ends, then goes back where it was.
_Avoid_: warping, cursor borrow

### External tools

**Mode marker**:
The suffix on the overlay's window title that tells external tools (such as Talon) which mode is active: none when idle, `[Active]` while hints are shown, `[Scroll]` while scrolling. It is the only state the app advertises outward; external tools act by sending the same keystrokes a user would.
_Avoid_: status title, mode title

## Relationships

- Many **window rules** may match one window; per **rule section** the most specific wins, and together with the defaults they produce exactly one **effective policy** per window.
- A **window rule** has one matcher and one or more **rule sections**.
- A matcher may name an **app definition**; one **app definition** serves many **window rules**.
- A window in scroll mode has one target window (`0`) and up to nine **scroll regions**; one of them is the active **scroll target**.
- The **effective policy** decides which **scroll regions** are found and which **actuation mechanisms** may be used for them.

## Example dialogue

> **Dev:** "PowerPoint images aren't getting hints — should I patch the scanner?"
> **Domain expert:** "No — write a **window rule** for PowerPoint whose click section adds `Image` to the requested control types. The scanner reads the **effective policy**; it never knows PowerPoint by name."
