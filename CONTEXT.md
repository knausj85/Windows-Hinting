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

## Relationships

- Many **window rules** may match one window; per **rule section** the most specific wins, and together with the defaults they produce exactly one **effective policy** per window.
- A **window rule** has one matcher and one or more **rule sections**.
- A matcher may name an **app definition**; one **app definition** serves many **window rules**.

## Example dialogue

> **Dev:** "PowerPoint images aren't getting hints — should I patch the scanner?"
> **Domain expert:** "No — write a **window rule** for PowerPoint whose click section adds `Image` to the requested control types. The scanner reads the **effective policy**; it never knows PowerPoint by name."
