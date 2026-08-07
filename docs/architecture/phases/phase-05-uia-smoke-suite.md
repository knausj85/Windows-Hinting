# Phase 05 — UIA smoke suite and fixture app

**Depends on:** 04 (the scanner seam it asserts must be final)
**Decisions:** [#36](https://github.com/knausj85/Windows-Hinting/issues/36)

## Goal

Land the second testing tier: a thin integration smoke suite (~a dozen tests) proving the
`WindowsHinting.Interop.Uia` seam contract against a purpose-built fixture app, riding the Debug CI
job and gating merge.

## Work

1. **`WindowsHinting.UiaFixture`** — a small committed WinForms app with deterministic content:
   known buttons and links, an off-screen element, overlapping elements, and elements exercising
   each activator pattern (Invoke, Toggle, ExpandCollapse, SelectionItem, SetFocus, mouse-click
   fallback). Activation must cause an **observable reaction** the test can assert (e.g. the
   fixture writes a line to stdout or a file: `clicked:button-a`).
2. **`WindowsHinting.Interop.Uia.Tests`** (xUnit v3): launch the fixture, then assert the seam
   contract through `HintSource` + the activator chain — not through the full app. Roughly:
   scan finds the known elements with sane bounds; off-screen/overlap cases behave per policy;
   each activator pattern activates its element (asserted via the fixture reaction); `ElementRef`
   resolution fails cleanly for a stale ref (fixture window closed). No mocks.
3. **Flake hygiene, mandated:** generous timeouts, one automatic retry, small suite. Rule:
   fix-or-delete — never `[Skip]` and abandon. (Escape hatch if chronically flaky: demote the CI
   step to non-gating-but-reported, by explicit decision, not silently.)
4. **CI:** run the suite inside the existing `build-debug` job on `windows-latest` (it already pays
   the VS Build Tools tax the interop project needs). The job — and therefore the suite — **gates
   merge**.

## Definition of done

- Suite green locally and in CI on three consecutive runs (flake check).
- The Debug job fails if the suite fails (verified by a deliberately broken run or `-Explicit`
  check).
- Fixture app builds with `dotnet build` and is excluded from release packaging.
- Smoke test: none beyond section 1 sanity — this phase adds tests, not behavior.

## Out of scope

- Whole-app E2E automation (explicitly rejected by #36).
- Testing rendering/tray/hooks (manual-only tier).
