# Phase 09 — Logging and update module

**Depends on:** 03 (boundary logging points exist); parallel-safe with 04–08
**Decisions:** [#35](https://github.com/knausj85/Windows-Hinting/issues/35)

## Goal

Replace the custom logging layer with `Microsoft.Extensions.Logging` (host-only) and gather the
update machinery into a host update module. Behaviors preserved exactly; one deliberate UX change:
updates download in the background with a single restart prompt.

## Work

1. **Delete the custom logging layer:** `Logging/ILogger.cs`, `Logging/LogLevel.cs`,
   `Logging/LogMessageEventArgs.cs`, `Logging/DebugLogger.cs`. Core gets **no** logging dependency,
   not even `Abstractions` — if a Core type wants to log, that's a design error; return data
   instead.
2. **M.E.L in the host:** `ILogger<T>` via the service collection. Providers replicating
   `DebugLogger` behaviors:
   - File provider, opt-in via preferences, writing `%AppData%\Windows-Hinting\logs` (same
     location/rotation semantics as today).
   - In-memory event provider feeding `Forms/LogViewerForm.cs` (live view preserved).
   Preserve the tolerant log-level preference parsing (now a Core-validated field).
3. **Boundary logging:** the funnel logs intents in and effects/condition-deltas out; scan timing
   logs move to the scan adapter; update events log in the update module. Port existing useful log
   lines; drop noise.
4. **`NetSparkleLoggerAdapter`** rewritten against M.E.L.
5. **Update module:** relocate `UpdateService`, `PortableUpdateInstaller`, `DeploymentMode`, and
   appcast/channel wiring into a host `Updates` namespace/folder with no Core dependency. Enable
   NetSparkle **background download**: silent download, then one "update ready — restart to
   install?" prompt (replaces the front-loaded dialog). Installed-mode UAC prompt at install time
   remains; portable self-update flow unchanged.

## Definition of done

- CI green; solution contains no reference to the deleted custom logging types.
- `WindowsHinting.Core.csproj` still references no logging package (compile-time check).
- Smoke test sections **6, 8** pass; section **9** on a Release build against the beta channel
  (verify background download + single prompt, and portable self-update).
- Log viewer and opt-in file logging behave as before.

## Out of scope

- Signing changes ([#41](https://github.com/knausj85/Windows-Hinting/issues/41) tracks Azure
  Artifact Signing separately); winget; installer changes.
