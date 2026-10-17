# Slice 04 — Rule-driven scroll and the built-in rules

**Depends on:** 02, 03
**Design:** [design.md](../design.md) §3.4–3.5, §4.4–4.5; [window-rules.md](../window-rules.md)
§3.3, §6; [builtin-rules.json](../builtin-rules.json)

## Goal

Scroll detection and actuation read the effective policy, so the apps the generic detector misses
(Slack, Teams, VS Code sidebar, Gmail folder list) get regions, Chromium-family windows trust the
keypress over UIA's axis claims, and Explorer scans only its active tab.

## Work

1. **Detection policy from rules.** Build `ScrollDetectionPolicy` from the effective
   `scrollDetection` section (defaults where absent).
2. **`expectedRegions` harvest.** Turn selectors into UIA conditions where possible
   (`automationId`, `controlType`; `name` when plain) and filter the rest client-side (`className`
   token, regex `name`, `not`). Handle `region: parent` (one per parent). Candidates skip the Gate,
   are clipped to the window, then go through Dedup and Number with the generic ones.
3. **Root scoping.** The scroll harvest uses `root.strategy`, like click. Revive
   `FileExplorerCustomStrategy` (its built-in rule is commented out on `main`), re-test it for
   click, and confirm it removes Explorer's inactive-tab phantom regions.
4. **Actuation policy from rules.** `axisPolicy`, `rungOrder`, `forceWheel`, `parkDebounceMs`,
   `velocity`, `tapWheelDelta` from the effective `scrollActuation` section.
5. **Built-in rules.** Load [builtin-rules.json](../builtin-rules.json) as the embedded built-ins
   (drop the `_` notes or let the loader ignore them).

## Definition of done

- Manual, bare metal, mouse resting elsewhere:
  - **Slack** (desktop and the Edge web app): sidebar and message pane are regions and scroll;
    check whether the thread pane (`p-view_contents--secondary`) is found and fix its selector.
  - **Teams**: chat list, message list and calendar grid are regions and scroll. Find and record
    what "mostly works" in the probe missed.
  - **VS Code**: Explorer, SCM, Extensions and Debug Console lists are regions; breadcrumbs are not.
  - **Gmail** in Edge: the folder list scrolls vertically (the axis lie is overridden).
  - **File Explorer** with several tabs: only the active tab's panes are regions.
- A user rule in the settings file that adds `expectedRegions` for some other app takes effect
  after a restart.
- Unverified probe items checked and recorded on the PR: Firefox URL read (does the outermost
  Document carry the page URL? how long does the read take?); InPrivate.

## Out of scope

- `click` section (slice 05).
- Live reload of rules (modernize phase 07).
