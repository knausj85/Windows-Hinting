# Slice 05 — Rule diagnostics and the click section

**Depends on:** 03
**Design:** [window-rules.md](../window-rules.md) §3.1, §5

## Goal

Users can see what a window looks like to the rule matcher and why a rule did or didn't apply, and
rules can change which control types get click hints.

## Work

1. **Last active window tracker.** `EVENT_SYSTEM_FOREGROUND` hook that remembers the most recent
   foreground window that isn't this app's and isn't the taskbar or tray (`Shell_TrayWnd`,
   `Shell_SecondaryTrayWnd`, the notification overflow).
2. **Copy window identity** tray item: the matcher JSON (URL reduced to scheme, host and path) on
   the clipboard, plus a balloon naming the window.
3. **Explain window rules** tray item: the report in window-rules §5 written to
   `%AppData%\Windows-Hinting\diagnostics\` and opened.
4. **`click` section.** `UIAutomationService` builds its requested control types from the
   default 17 plus the effective `click.controlTypes` add/remove.
5. **README.** Document window rules for hand-editing: fields, string syntax, `url` patterns, app
   definitions, exclusions, precedence, selectors, sections, and the two diagnostics. Document the
   scroll keymap and the `[Scroll]` title for Talon users.

## Definition of done

- Copy window identity on Edge with Gmail open yields a matcher whose `url` is
  `https://mail.google.com/mail/u/0/` (no fragment); on Notepad, no `url` field.
- Explain window rules on Slack shows the Slack app alternative that matched, the winning rule per
  section, and the tie-break used.
- **PowerPoint** (verify, it was not available to the probe): a user rule adding `Image` and
  `HeaderItem` to `click.controlTypes` makes slide images and header items hintable and clickable.
  If it works, add it as a built-in rule.
- Click hinting with no matching click rule is unchanged.

## Out of scope

- A rules editor UI.
- Any hotkey for the diagnostics.
