# Phase 08 — Preferences UI restructure

**Depends on:** 07
**Decisions:** [#40](https://github.com/knausj85/Windows-Hinting/issues/40)

## Goal

Rebuild the settings window as a modern-WinForms, singleton, non-modal, instant-apply window with
section pages. This phase **changes user-visible behavior deliberately**: no OK/Cancel, the Window
Rules tab disappears, and "Start with Windows" becomes file-backed.

## Work

1. **Window shell:** singleton non-modal window (re-open focuses the existing instance); owns only
   navigation and the debounced save through the phase-07 `PreferencesStore`. Sidebar vs tabs is
   cosmetic — pick what reads best.
2. **Section contract + registry:** each section is a self-contained control that loads from the
   typed options and raises `Changed` with valid values. A registry list drives navigation.
   Sections: **General, Hotkeys, Click actions, Updates**. Adding a setting touches one section;
   adding a section is one class + one registry line.
3. **Instant apply:** every valid change saves `preferences.json` (debounced); the change applies
   via the watcher → `OptionsApplied` path — live preview *is* the applied change. No OK/Cancel;
   add reset-to-default affordances (per section or per field). Half-complete input (mid-recording
   hotkey, partial values) is never written. Hotkey capture keeps exactly one host interaction:
   suspend global hotkey registration while recording (`HotkeyRecordingStarted`/`Stopped` survive
   in that role only; `Controls/HotkeyRecorderControl.cs` adapts).
4. **Remove the Window Rules tab** entirely — no grid, no file manager, no "open rules folder"
   link. Document the `rules/` file format in the repo README instead.
5. **"Start with Windows" as a field:** the checkbox writes the `preferences.json` field; the host
   reconciles the registry run-key on `OptionsApplied` (`StartupService` becomes that
   reconciler). Delete the direct registry write from the dialog (the old
   `PreferencesDialog.cs` OK-path). The file is authoritative.
6. **Delete `Preferences/PreferencesDialog.cs`** and its modal plumbing once the new window covers
   its sections.

## Definition of done

- CI green.
- Smoke test sections **1, 6, 7** pass; section 7.1 now verifies *instant* apply (no OK button
  exists).
- The settings UI performs no I/O other than saving preference files (no registry access, no
  direct service calls — grep `StartupService` usages).
- README documents the `rules/` format and the removal of the rules UI.

## Out of scope

- New settings; any rules/`ScanPolicy` editor (out of scope for the effort).
