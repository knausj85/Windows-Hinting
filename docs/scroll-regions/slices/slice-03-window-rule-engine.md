# Slice 03 — Window-rule engine

**Depends on:** — (runs in parallel with 01–02)
**Design:** [window-rules.md](../window-rules.md) §1–4
**Absorbed later by:** modernize phase 04 (policy resolution in Core) and phase 07 (`rules/` files)

## Goal

Grow `WindowRule` into the full matcher + sections model and replace first-match resolution with
the per-section effective policy, on today's settings file. No hinting behavior changes in this
slice: only the `root` section has a consumer yet.

## Work

1. **Test project.** If slice 01 hasn't added it yet, add `Windows-Hinting.Tests` (see slice 01).
2. **Pure matching** (static, unit-tested, no Win32): the string syntax (plain / `/re/flags`, 100 ms
   timeout); the `url` pattern parser and matcher with its specificity ranking; `not` exclusions;
   app definitions (OR of alternatives, same-name additions, unknown name = error); specificity;
   the three tie-breaks; same-name override of built-ins; per-section resolution with defaults.
   Input is a `WindowIdentity` value with a lazy `url` callback, so tests can assert the URL is
   read only when needed.
3. **Window identity reader** (Win32/UIA): exe, class, title, `appId` (property store, then
   `GetApplicationUserModelId`), and the lazy `url` read gated on web-host classes, memoized per
   scan, never logged.
4. **Sections model**: `root`, `click`, `scrollDetection`, `scrollActuation` with the fields in the
   design docs, control types by programmatic name, add/remove deltas. Unknown sections and fields
   are kept; `_`-prefixed fields ignored.
5. **Storage bridge**: new matcher fields and sections on `WindowRule`, the `WindowApps` list, the
   `TitleMatchMode.Contains` → `/text/i` load conversion, built-in app definitions and rules in
   `WindowRuleRegistry` (only `Windows Search` in this slice; slice 04 adds the rest). The
   Preferences Window Rules grid must round-trip every field it doesn't show.
6. **Resolution**: `ResolveStrategy` becomes `ResolveEffectivePolicy(window) → EffectivePolicy`;
   the click scanner reads `root.strategy` from it.
7. **Load errors**: bad rules dropped and reported in one tray notification; the rest load.

## Definition of done

- Unit tests cover: string syntax incl. regex timeout; every `url` example in window-rules §2.2
  plus the pitfalls (`mail.google.com.evil.com`, `mail.google.com@evil.com`, `evilgoogle.com`,
  trailing dot, IDN, default port); app definitions incl. a URL alternative; `not`; specificity and
  each tie-break; per-section winners from different rules; same-name override.
- An existing settings file with user window rules (including a `Contains` title rule) loads and
  saves without losing anything; editing a rule in the Preferences grid keeps hand-added sections.
- Click hinting behaves exactly as before in File Explorer, Windows Search and a browser.

## Out of scope

- Using `click`, `scrollDetection` and `scrollActuation` (slices 04–05).
- Diagnostics (slice 05).
- `rules/` files and the watcher (modernize phase 07).
