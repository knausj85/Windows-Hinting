# Phase 01 — Solution split: Core + Interop.Uia

**Depends on:** 00
**Decisions:** [#32](https://github.com/knausj85/Windows-Hinting/issues/32) (early extraction),
[#31](https://github.com/knausj85/Windows-Hinting/issues/31) (decomposition),
[#36](https://github.com/knausj85/Windows-Hinting/issues/36) (test harness, neutral TFM, CI)

## Goal

Create the target project structure so `WindowsHinting.Core` gets the fast `dotnet build` /
`dotnet test` loop immediately, and the tlbimp `<COMReference>` is quarantined in a leaf project.
This phase is mechanical code movement — no redesign, no behavior change.

## Work

1. **Create `WindowsHinting.Core`** (`net10.0` neutral TFM, `TreatWarningsAsErrors`). Move only
   what is already pure or trivially purifiable, e.g. `Labels.cs` and plain data types from
   `Models/Models.cs` that reference no WinForms/Win32/COM (e.g. `ClickAction`). If a type is
   almost-pure (e.g. uses `System.Drawing.Rectangle`, which is fine on neutral TFM via
   `System.Drawing.Primitives`), move it; if it drags UI types, leave it for phase 02/03.
2. **Create `WindowsHinting.Interop.Uia`** (`net10.0-windows`). Move the tlbimp `<COMReference>`
   from `Windows-Hinting.csproj` plus all UIA-touching code:
   `NativeInterop/UIAutomationWrapper.cs`, `Services/UIAutomationService.cs`,
   `Services/IUIAutomationService.cs`, `UIAutomationConstants.cs`,
   `Services/ElementActivators/*`, `Services/ElementActivatorChain.cs`,
   `Services/IElementActivator.cs`. The host references this project; namespaces may stay as-is to
   keep the diff mechanical.
3. **Create `WindowsHinting.Core.Tests`** (xUnit v3, plain `dotnet test`). Seed it with real tests
   for what Core now contains (e.g. label-generation properties) so the pipeline is proven.
4. **Update the solution and CI** (`.github/workflows/build.yml`):
   - New job `core-tests` on `ubuntu-latest`: .NET SDK only, `dotnet test` on
     `WindowsHinting.Core.Tests`. It becomes part of the PR gate alongside lint and Debug build.
   - Existing Debug/Release jobs keep the full-MSBuild path (the interop project still needs it
     until phase 10).
5. **Rewrite the UIA rule in `.github/copilot-instructions.md`:** replace "do not regenerate UIA
   via CsWin32" with the target state — CsWin32 `GeneratedComInterface` is the destination, gated
   on a stable CsWin32 release containing
   [PR #1746](https://github.com/microsoft/CsWin32/pull/1746); until then the tlbimp
   `<COMReference>` lives in `WindowsHinting.Interop.Uia` and full-app builds require MSBuild.

## Definition of done

- `dotnet build` and `dotnet test` succeed for `WindowsHinting.Core` / `WindowsHinting.Core.Tests`
  on a machine without VS Build Tools (CI ubuntu job proves this).
- Full solution builds via MSBuild; CI green including the new `core-tests` job.
- `WindowsHinting.Core.csproj` contains no reference to WinForms, CsWin32, COM, or any logging
  package.
- Smoke test sections **1, 2, 4** pass (workflow, multi-monitor, taskbar).
- No behavior change.

## Out of scope

- Any CsWin32 work (phase 10, gated).
- Engine extraction (phase 02) — do not move `HintController` logic here.
