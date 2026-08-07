# Phase 00 — Dead code deletion and test hygiene

**Depends on:** nothing
**Decisions:** [#33](https://github.com/knausj85/Windows-Hinting/issues/33) (pipe deletion),
[#36](https://github.com/knausj85/Windows-Hinting/issues/36) (test-reference cleanup)

## Goal

Remove code that is already dead in the shipped build and the packaging hacks that exist only
because of it, so later phases start from a clean baseline. No behavior change.

## Work

1. **Delete the named-pipe surface.** The pipe is dead code: `NamedPipeService` is never registered
   (DI line commented out in `ServiceCollectionExtensions.cs`), the controller wiring is commented
   out in `HintController.cs`, and the README's Talon integration uses keystrokes. Remove:
   - `Windows-Hinting/Services/NamedPipeService.cs`
   - `Windows-Hinting/NamedPipeClient/HintOverlayClient.cs` (and the `NamedPipeClient` folder)
   - `Windows-Hinting/Examples/HintOverlayClientExamples.cs` (and the `Examples` folder)
   - The `NamedPipeCommand` / `CommandType` types (in `Windows-Hinting/Models/Models.cs`)
   - The commented-out registration and wiring lines that referenced any of the above
2. **Remove orphaned test references.** Delete the `xunit.assert` / `xunit.v3.extensibility.core`
   package references from `Windows-Hinting/Windows-Hinting.csproj` and the "Clean Debug output"
   step in `.github/workflows/build.yml` that scrubs `xunit.*` from the Debug output — it exists
   only because of those references.
3. **Sweep for stragglers:** grep for `NamedPipe`, `HintOverlayClient`, `CommandType` and remove
   remaining dead references (usings, comments describing the pipe protocol, docs). Do **not**
   touch the README's keystroke-based Talon section — that remains the documented integration.

## Definition of done

- Solution builds (Debug, full MSBuild path) with no references to the deleted types.
- CI (lint + Debug + Release jobs) is green.
- Smoke test sections **1, 4, 6** pass (hint workflow, taskbar mode, tray/lifecycle).
- No behavior change.

## Out of scope

- Any replacement control surface (ruled out by #33).
- Project splitting (phase 01), logging changes (phase 09).
