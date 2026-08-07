# Architecture modernization spec

This directory is the hand-off document for modernizing Windows-Hinting's architecture. It was
assembled from the decisions of the wayfinder map
[Modernize the Windows-Hinting architecture (#26)](https://github.com/knausj85/Windows-Hinting/issues/26);
every design choice here traces back to a closed decision ticket linked in context.

**The one inviolable behavior:** the user-facing hint workflow — hotkey → labels appear → type a
label → element activates. Every phase must leave that workflow working exactly as before unless the
phase explicitly says otherwise.

## Documents

- [`target-architecture.md`](target-architecture.md) — the destination: modules, dependency rules,
  data flow, external surfaces, and standing constraints.
- [`smoke-test.md`](smoke-test.md) — the committed manual smoke script. Running the sections named
  in a phase's definition of done is part of completing that phase
  ([testing decision #36](https://github.com/knausj85/Windows-Hinting/issues/36)).
- [`phases/`](phases/) — the migration phases, one file each, sized for a single coding-agent
  session. Each is independently buildable and manually verifiable with explicit done-criteria.

## Development branch

The effort is developed on the long-lived **`modernize`** branch, which also carries this spec.
Each phase is its own PR **targeting `modernize`**, not `main` — the PR gate set (lint + Core
tests + Debug build/smoke suite) runs on every branch, so gating is identical. `main` stays
shippable throughout; the Release + MSI job auto-runs only on `main`, and testable mid-effort
builds publish to the rolling **beta** channel via `workflow_dispatch` on `modernize`.

Merge `modernize` → `main` at stability points rather than big-bang at the end — natural
candidates are after phase 03 (cutover, full smoke test passed) and after phase 05 (smoke suite
gating). When a phase file says a check "gates merge", the merge target is `modernize` until the
effort lands on `main`.

## How to execute a phase

1. Read this README, `target-architecture.md`, and the phase file. Read the decision tickets the
   phase links only if you need the rationale behind a rule.
2. Check the phase's **Depends on** line — all listed phases must be merged first. If the phase has
   a **Gate**, verify the gate condition before starting; if it hasn't lifted, stop.
3. Do the work on a branch off `modernize`; keep the phase's **Out of scope** list out of the diff.
4. Satisfy every item in **Definition of done**, including the named smoke-test sections, before
   opening a PR against `modernize`. A phase that changes user-visible behavior says so explicitly;
   otherwise "no behavior change" is part of done.

## Phase order

| Phase | Title | Depends on | Notes |
|---|---|---|---|
| [00](phases/phase-00-dead-code-and-test-hygiene.md) | Dead code deletion and test hygiene | — | |
| [01](phases/phase-01-solution-split.md) | Solution split: Core + Interop.Uia | 00 | Unblocks `dotnet build`/`test` for Core |
| [02](phases/phase-02-hint-engine.md) | HintEngine in Core | 01 | App untouched; engine + tests only |
| [03](phases/phase-03-host-cutover.md) | Host cutover to the intent funnel | 02 | The largest phase |
| [04](phases/phase-04-scan-pipeline.md) | Scan pipeline: HintSource, stages, ScanPolicy | 03 | |
| [05](phases/phase-05-uia-smoke-suite.md) | UIA smoke suite and fixture app | 04 | Adds a merge gate |
| [06](phases/phase-06-overlay-composer-presenter.md) | Overlay composer/presenter split | 03 | Can run parallel to 04–05 |
| [07](phases/phase-07-preferences-infrastructure.md) | Preferences infrastructure v1 | 03, 04 | New on-disk format |
| [08](phases/phase-08-preferences-ui.md) | Preferences UI restructure | 07 | Removes Window Rules tab |
| [09](phases/phase-09-logging-and-updates.md) | Logging and update module | 03 | Can run parallel to 04–08 |
| [10](phases/phase-10-cswin32-interop.md) | CsWin32 interop rewrite | 01 + **gate** | Gated on CsWin32 release containing PR #1746 |
| [11](phases/phase-11-per-pixel-alpha.md) | Per-pixel alpha presenter | 06 | Optional, detachable |

### Ordering rationale

- **Risk first-down:** 00–01 are mechanical and low-risk, and 01 immediately buys the fast
  `dotnet build`/`dotnet test` loop for Core that every later phase leans on
  ([UIA interop decision #32](https://github.com/knausj85/Windows-Hinting/issues/32)).
- **Engine before its consumers:** 02 builds and fully tests the engine with the app untouched, so
  03 — the risky cutover — is rewiring against an already-verified core, not a big-bang rewrite.
  The overlay display model (06), the `OptionsApplied` intent (07), and boundary logging (09) all
  consume shapes 03 establishes.
- **Gate the seam early:** 05 lands the merge-gating UIA smoke suite as soon as the scanner seam is
  final (after 04), so phases 06–11 run under it.
- **Floating phases:** 06 and 09 depend only on 03 and may execute in any order relative to 04–08.
  10 depends only on 01 plus its external gate — it executes whenever the gate lifts, regardless of
  where the sequence otherwise stands. 11 is optional; skipping it forever is an acceptable outcome
  ([overlay decision #34](https://github.com/knausj85/Windows-Hinting/issues/34)).
- **New preferences format late:** 07–08 change the on-disk format and settings UI. They come after
  the engine/pipeline phases because rules carry `ScanPolicy` (defined in 04) and apply through the
  `OptionsApplied` intent (defined in 02–03).

## Decision record

| Ticket | Decision |
|---|---|
| [#31](https://github.com/knausj85/Windows-Hinting/issues/31) | Core/host decomposition, intent funnel, `ElementRef` seam, scan pipeline, threading model |
| [#38](https://github.com/knausj85/Windows-Hinting/issues/38) | Prototype ratified the model; derived-conditions refinement (five one-shot effects + `DesiredConditions`) |
| [#32](https://github.com/knausj85/Windows-Hinting/issues/32) | CsWin32 `GeneratedComInterface` target, hard-gated; early interop extraction |
| [#33](https://github.com/knausj85/Windows-Hinting/issues/33) | No external control surface; pipe deleted; intent funnel is the future attachment point |
| [#34](https://github.com/knausj85/Windows-Hinting/issues/34) | WinForms overlay stays; composer/presenter split; ULW alpha as optional late phase |
| [#35](https://github.com/knausj85/Windows-Hinting/issues/35) | NetSparkle+WiX stays; background download; M.E.L logging host-only |
| [#36](https://github.com/knausj85/Windows-Hinting/issues/36) | Three-tier testing; neutral-TFM Core; committed manual smoke script; PR gate set |
| [#39](https://github.com/knausj85/Windows-Hinting/issues/39) | Versioned `preferences.json` + `rules/`; preserve+notify; one propagation path |
| [#40](https://github.com/knausj85/Windows-Hinting/issues/40) | Modern WinForms settings; instant apply; no rules UI; startup as a preferences field |
