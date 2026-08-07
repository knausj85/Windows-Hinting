# Phase 10 — CsWin32 interop rewrite

**Depends on:** 01 (interop leaf project) — otherwise order-independent; execute whenever the gate
lifts.
**Gate (hard, no fallback):** a **stable `Microsoft.Windows.CsWin32` NuGet release containing
[PR #1746](https://github.com/microsoft/CsWin32/pull/1746)** (merged 2026-07-28; v0.3.296 of
2026-06-15 predates it). If the release hasn't shipped, this phase waits — do not start.
**Decisions:** [#32](https://github.com/knausj85/Windows-Hinting/issues/32)

## Goal

Replace the tlbimp `<COMReference>` UIA interop with CsWin32 in `GeneratedComInterface`
source-generator mode, entirely inside `WindowsHinting.Interop.Uia`. After this phase the whole
solution builds with `dotnet build` — the VS Build Tools requirement dies.

## Work

1. Verify the gate: check the CsWin32 release notes/NuGet for a stable version containing PR #1746.
   Record the version in the PR description.
2. Add `Microsoft.Windows.CsWin32` to `WindowsHinting.Interop.Uia`; generate the UIA interfaces in
   `GeneratedComInterface` mode (`NativeMethods.txt`). Remove the `<COMReference>`.
3. Rewrite call sites for the new type shapes: interface inheritance is real (no more casting
   dance), variants via `ComVariant`.
4. **Lifetime rewrite:** all ~30 `Marshal.ReleaseComObject` / `Marshal.IsComObject` sites go —
   ComWrappers-based lifetime replaces manual release. Audit the `ElementRef` table's release
   semantics accordingly (refs must still not leak COM objects past their scan generation).
5. **CI simplification:** Debug/Release jobs drop the VS Build Tools + MSBuild path where it existed
   only for the COMReference (`build/build-complete.ps1`, portable publish steps in
   `.github/workflows/build.yml` can move to `dotnet build`/`dotnet publish`; keep MSBuild only if
   WiX still needs it for the installer job).
6. Update `.github/copilot-instructions.md`: the gate language goes away; the rule becomes "UIA
   interop is CsWin32-generated in `WindowsHinting.Interop.Uia`; don't reintroduce tlbimp."

## Definition of done

- Full solution (minus installer, if WiX-bound) builds with `dotnet build` on a machine without VS
  Build Tools.
- CI green, including the UIA smoke suite (phase 05) — the suite is the seam-contract proof that
  the rewrite preserved semantics.
- No `Marshal.ReleaseComObject`/`IsComObject` remains in the solution.
- Full smoke test sections **1, 2, 4, 5** pass (scan + every activator pattern).

## Out of scope

- Behavior changes to scanning/activation; NativeAOT (enabled-by, not done-by, this phase).
