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

**Effective policy**:
What scanning and actuation actually consult for one window: for each **rule section**, the one from the most specific matching **window rule** that has it, layered over the built-in defaults. Rules are never combined *within* a section.
_Avoid_: resolved profile, active rule

## Relationships

- Many **window rules** may match one window; per **rule section** the most specific wins, and together with the defaults they produce exactly one **effective policy** per window.
- A **window rule** has one matcher and one or more **rule sections**.

## Example dialogue

> **Dev:** "PowerPoint images aren't getting hints — should I patch the scanner?"
> **Domain expert:** "No — write a **window rule** for PowerPoint whose click section adds `Image` to the requested control types. The scanner reads the **effective policy**; it never knows PowerPoint by name."
