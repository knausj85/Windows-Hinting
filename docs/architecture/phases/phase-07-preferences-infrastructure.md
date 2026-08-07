# Phase 07 — Preferences infrastructure v1

**Depends on:** 03 (`OptionsApplied` intent), 04 (`ScanPolicy` carried by rules)
**Decisions:** [#39](https://github.com/knausj85/Windows-Hinting/issues/39)

## Goal

Replace the preferences plumbing with the versioned, watched, preserve-and-notify model. This phase
**does** change on-disk format (v0 → v1) — user-visible only in the file layout, not in behavior.

## Work

1. **Core (pure):** typed options model; strict key-syntax parser (`"Ctrl+Alt+H"`, unknown token →
   per-field preserve+notify); validation; rule matching/merging (built-in embedded rules,
   `Name`-based override, specificity-then-file-order resolution — lifting today's merge
   semantics); migration chain with today's unversioned format as **version 0** (including Win32
   int-pair hotkeys → key syntax). All unit-tested, including migration golden files.
2. **Format:** `preferences.json` with explicit `"version"`, plus `rules/` directory — each file a
   **list** of rules carrying `ScanPolicy`. Built-in defaults ship embedded, never materialized to
   disk. A newer-version file takes the preserve+notify path.
3. **Host `PreferencesStore` adapter:** load/save, `.bak` rename on parse failure, tray
   notification reporting what was dropped, debounced `FileSystemWatcher` over `preferences.json` +
   `rules/` that contains rename-swap/partial-write quirks.
4. **One propagation path:** every change (UI save, hand edit, dropped-in rule file) flows
   watcher → reload → validate → `OptionsApplied` intent into the engine, plus host-side
   application (hotkey re-registration). Remove any code path that applies settings without going
   through this pipeline. `PreferencesService`/`IPreferencesService` are replaced by the
   store + funnel.
5. **Startup migration:** first run after upgrade migrates the existing file in place (via the
   chain) and writes v1.

## Definition of done

- CI green; migration, parser, matching, and validation covered in Core.Tests.
- Smoke test sections **1, 7** pass — including the hand-edit-while-running and
  corrupt-file/`.bak`/notification cases (7.2, 7.3 now apply).
- An existing v0 preferences file from a released build upgrades losslessly (verify with a real
  file).
- Grep-check: exactly one call site constructs `OptionsApplied` (the watcher path).

## Out of scope

- Settings UI restructure (phase 08) — the existing dialog may keep working by writing files
  through the store.
- Any rules editor UI (out of scope for the whole effort).
