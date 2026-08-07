# Phase 04 — Scan pipeline: HintSource, stages, ScanPolicy

**Depends on:** 03
**Decisions:** [#31](https://github.com/knausj85/Windows-Hinting/issues/31)

## Goal

Restructure scanning into the one-pipeline model: `HintSource` implementations yield targets; pure
Core stages transform them; a `ScanPolicy` — resolved per (source × matched window rule) — selects
and parameterizes the stages. **Initial policies encode today's shipped behavior exactly**, so this
phase changes no behavior.

## Work

1. **`HintSource` seam.** Define the seam in Core; implement `ForegroundWindowSource` and
   `TaskbarSource` in `WindowsHinting.Interop.Uia` on top of the existing `UIAutomationService` /
   `UIAutomationWrapper` walking code. Each yields scan targets carrying `{Bounds, Name/metadata,
   ElementRef}`; COM stays behind the seam.
2. **Pure stages in Core.** Port the logic of `ElementDeduplicator`, `HintBoundsFilter`, and label
   assignment (`Labels`) into pure `IScanStage`-shaped functions operating on plain data. Delete
   the originals from the host/interop once ported.
3. **`ScanPolicy`.** A declarative description of which stages run and with what parameters. Grow
   `WindowRule` (Configuration/) from RootStrategy-only into the fuller policy carrier; resolution
   is per (source × matched rule) in pure Core code (`WindowRuleRegistry` logic moves/ports here).
   Initial built-in policies replicate the shipped configuration: **dedup taskbar-only,
   bounds-clamp off**. Re-enabling a stage later must be a policy edit, not a code change.
4. **`IScanStage` hatch.** One fixed insertion point for compiled-in, named, first-party stages
   (DI-registered) for app quirks that outgrow declarative rules. Ship it empty or with the
   minimal set needed to reproduce current behavior.
5. **Tests** in Core.Tests: each stage as pure data-in/data-out cases; `ScanPolicy` resolution
   (specificity, source × rule matrix); a table test proving the initial policies reproduce
   today's stage selection.

## Definition of done

- CI green; new Core tests cover stages and policy resolution.
- Full smoke test sections **1, 2, 4** pass; scanning behavior byte-for-byte comparable (same
  elements hinted in the same situations — spot-check File Explorer, a browser, and the taskbar
  against a pre-phase build).
- No COM type appears in any Core signature (the neutral TFM enforces this at compile time).

## Out of scope

- Changing which elements are hinted anywhere (that's a future policy edit, e.g. issues #10, #20,
  #23–#25 — deliberately not this refactor).
- The UIA smoke suite (phase 05).
