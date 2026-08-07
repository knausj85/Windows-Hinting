# Target architecture

The destination state after all migration phases. Decision rationale lives in the linked tickets;
this document states the rules.

## Projects

| Project | TFM | Role |
|---|---|---|
| `WindowsHinting.Core` | `net10.0` (neutral) | All decision logic, pure. No WinForms, Win32, COM, or logging dependency (not even `Microsoft.Extensions.Logging.Abstractions`). `TreatWarningsAsErrors` on. |
| `WindowsHinting.Interop.Uia` | `net10.0-windows` | UIA interop and the scanner/activator adapters. The only project that sees COM types. tlbimp `<COMReference>` until the CsWin32 gate lifts (phase 10), then CsWin32 `GeneratedComInterface`. |
| `Windows-Hinting` (host) | `net10.0-windows` | WinForms tray app: composition root, all adapters, all I/O, all threading. |
| `WindowsHinting.Core.Tests` | `net10.0` | xUnit v3; comprehensive unit tests; runs on `ubuntu-latest` with only the .NET SDK. |
| `WindowsHinting.Interop.Uia.Tests` | `net10.0-windows` | xUnit v3; ~a-dozen-test smoke suite against the committed WinForms fixture app; gates merge. |
| `WindowsHinting.UiaFixture` | `net10.0-windows` | Purpose-built WinForms fixture app with known buttons/links/off-screen/overlapping elements. |
| `Windows-Hinting.Installer` | WiX | Unchanged. |

**Dependency rule:** `Core` references nothing in the solution. `Interop.Uia` references `Core`
only. The host references both. Test projects reference their tested assembly. Nothing references
the host.

The neutral `net10.0` TFM on Core is the purity enforcement mechanism: a WinForms/Win32/COM leak
into Core is a compile error, not a review comment
([#36](https://github.com/knausj85/Windows-Hinting/issues/36)).

## The engine and the intent funnel

([#31](https://github.com/knausj85/Windows-Hinting/issues/31), refined by
[#38](https://github.com/knausj85/Windows-Hinting/issues/38); prototype on
[`prototype/hint-engine-intent-effect`](https://github.com/knausj85/Windows-Hinting/tree/prototype/hint-engine-intent-effect))

`HintEngine` is one deep module in Core:

- **Pure transition core:** `Transition(State, Intent) → (State, Effects)` — state is a value; the
  function is total over the closed intent set.
- **Thin stateful wrapper:** `HintEngine.Submit(Intent) → IReadOnlyList<Effect>` holds the current
  state, calls `Transition`, and exposes the new state.
- **Effects are five one-shots only:** `BeginScan`, `ActivateElement`, `Notify`, `PlayErrorBeep`,
  `SuppressKey`. Nothing continuous is an effect.
- **Everything continuous is derived:** a sibling pure function `DesiredConditions(state)` describes
  overlay visibility/hints/filter, input capture, foreground watch, auto-hide timer, tray status,
  and tray click action. After each `Submit`, the host reconciles reality against it and applies
  only deltas. The overlay slice of `DesiredConditions` **is** the display model the overlay
  composer consumes.
- **Modes:** `Inactive` / `Scanning` / `Active`. (The legacy `Selecting` mode is dropped — shipped
  code never entered it.)
- **Scan-token staleness guard:** every toggle/switch bumps a token; `ScanCompleted`/`ScanFailed`
  intents carrying a stale token are ignored structurally.
- **The engine never reads a clock.** The host stamps toggle intents with a timestamp at the funnel.
- **Foreground watch starts at scan completion** (when the engine learns the window), not scan
  start — an accepted ~100 ms behavior delta versus the legacy code.

**One intent funnel.** Hotkey, tray, keyboard hook, timers, scan completions — and any future
control surface — enter the system only as intents. Adapters execute effects and reconcile
conditions. The preferences window, updater, and startup registration live in the composition root,
outside the engine.

**Intent-set requirement** ([#33](https://github.com/knausj85/Windows-Hinting/issues/33)): the
intent set must be able to express everything the old named-pipe protocol could say —
show/hide/toggle, taskbar mode, select-label-with-explicit-`ClickAction`, deactivate. In
particular, the activation intent carries an optional `ClickAction` rather than inferring it from
held modifiers.

## Threading model

([#31](https://github.com/knausj85/Windows-Hinting/issues/31),
[#38](https://github.com/knausj85/Windows-Hinting/issues/38))

- The host serializes **all** intents onto the UI thread. The engine is synchronous and makes no
  threading assumptions.
- Async work (scans, deferred activation) completes by posting new intents — never by calling back
  into the engine from another thread.
- The keyboard-hook adapter reads `SuppressKey` synchronously from the batch `Submit` returns;
  `ActivateElement` is executed by posting to the message loop. This makes the
  `BeginInvoke`-inside-keyboard-hook deadlock class structurally impossible.
- Key-repeat suppression is input hygiene in the hook adapter, not engine logic.

## The scanner seam

([#31](https://github.com/knausj85/Windows-Hinting/issues/31),
[#32](https://github.com/knausj85/Windows-Hinting/issues/32))

- Elements cross into Core as opaque `ElementRef`s. Core's hint item is
  `{Bounds, Label, ElementRef}`; COM types and their lifetime never leave `Interop.Uia`.
- `HintSource` is the seam: each mode (ForegroundWindow, Taskbar, future sources) is an
  implementation yielding scan targets. Implementations that touch UIA live in `Interop.Uia`.
- Scan stages (clamp, dedup, filtering, labeling) are pure functions in Core, selected and
  parameterized by a `ScanPolicy` resolved per (source × matched window rule).
- A compiled-in, named `IScanStage` hatch (first-party only, DI-registered, one fixed insertion
  point) absorbs app quirks that outgrow declarative rules.
- **Initial policies encode today's shipped behavior exactly** — dedup taskbar-only, bounds-clamp
  off — so the restructure changes no behavior; re-enabling stages later is a policy edit.
- View state (`CurrentOpacity`/`TargetOpacity`) belongs to the renderer, not the element model.

## Overlay

([#34](https://github.com/knausj85/Windows-Hinting/issues/34))

WinForms, one form per screen (PerMonitorV2), restructured inside the host as a bounded `Overlay`
namespace — no separate assembly:

- **Composer:** display model in → `Bitmap` out (GDI+). Unit-testable (pixels-in-pixels-out); the
  bitmap replaces the existing `OptimizedDoubleBuffer` back-buffer rather than adding one.
- **Presenter:** puts the bitmap on screen. Initially a blit in `OnPaint` under today's
  `TransparencyKey` styles; phase 11 may swap in an `UpdateLayeredWindow` presenter for per-pixel
  alpha (DirectComposition stays on record as the escalation if ULW text quality disappoints).

Standing overlay constraints:

- **The overlay renders state and never handles input.** `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`
  click-through is inviolable. Interactive features are display-model states drawn as pixels, with
  the choice arriving as keystrokes through the intent funnel.
- **Redraw only on state change, never on a timer.**
- **Draw hints as pixels, never as UI elements** — the overlay's UIA footprint stays zero.

## Preferences

([#39](https://github.com/knausj85/Windows-Hinting/issues/39),
[#40](https://github.com/knausj85/Windows-Hinting/issues/40))

**Format:** `preferences.json` (scalars, explicit `"version": N`) plus a `rules/` directory of
per-app files, each holding a **list** of rules. Built-in default rules ship embedded in the binary;
a user rule sharing a built-in's `Name` overrides it. Overlaps resolve by match specificity, then
file order. Rules carry `ScanPolicy`. Key bindings use human-readable syntax (`"Ctrl+Alt+H"`)
parsed by a strict little parser in Core. Today's unversioned format is retroactively **version 0**;
a pure migration chain (N→N+1) upgrades old documents.

**Load failure = preserve + notify:** an unparseable file is renamed to `.bak`, the app runs on
defaults, and a tray notification says so. Per-field tolerance keeps what parses, defaults what
doesn't, and reports what was dropped. A file from a newer version takes the same path.

**One propagation path:** the host watches `preferences.json` + `rules/` (debounced). Every change —
settings-UI save, hand edit, dropped-in rule file — flows reload → validate → `OptionsApplied`
intent, plus host-side application (hotkey re-registration, startup-registry reconciliation). The
settings UI has **no privileged write path**; it just saves files.

**Placement:** types, key parser, validation, migrations, rule matching/merging are pure Core code.
File I/O, watcher, backup, and notifications live in a host adapter behind a small
`PreferencesStore` port.

**Settings UI:** modern WinForms; singleton non-modal window; instant apply (no OK/Cancel;
reset-to-default affordances). Section pages (General, Hotkeys, Click actions, Updates) behind a
tiny contract + registry. **Window rules have no UI surface at all** — `rules/` files are
hand-edited and documented in the README. "Start with Windows" is a `preferences.json` field; the
host reconciles the registry run-key on `OptionsApplied` and the file is authoritative.

## External surfaces

([#33](https://github.com/knausj85/Windows-Hinting/issues/33))

**None.** The named pipe is deleted, not replaced. Talon integration stays keystroke-based (as the
README documents). When Talon ships RPC, a control surface returns as a fresh effort: an adapter
that parses messages and posts intents — zero engine changes — over the hardened-pipe transport
blueprint recorded in the ticket.

## Logging

([#35](https://github.com/knausj85/Windows-Hinting/issues/35))

`Microsoft.Extensions.Logging` in the host; **Core does not log at all**. The host logs at the
boundaries: intents in, effects out, scan timings, update events. The custom `ILogger`/`LogLevel`/
`LogMessageEventArgs` layer is deleted; `DebugLogger`'s behaviors become M.E.L providers — an
opt-in file provider (`%AppData%\Windows-Hinting\logs`) and an in-memory event provider feeding the
log viewer. `NetSparkleLoggerAdapter` is rewritten against M.E.L. Behaviors are preserved exactly.

## Distribution and updates

([#35](https://github.com/knausj85/Windows-Hinting/issues/35))

- **UIAccess is inviolable for installed mode** → per-machine, signed, Program Files MSI stays.
  Silent no-UAC updates are off the table by Windows design.
- **NetSparkle + WiX stay.** The WiX installer project survives unchanged; NetSparkle plumbing and
  `PortableUpdateInstaller` relocate into a host-side update module with no Core dependency.
- **Update UX:** NetSparkle background download; a single "update ready — restart to install?"
  prompt replaces the front-loaded dialog. The UAC prompt at install time remains.
- **Portable mode kept as-is** (x64/x86 self-contained, signed, no UIAccess, self-updating).
- **No winget listing.** Signing stays on SSL.com eSigner
  ([#41](https://github.com/knausj85/Windows-Hinting/issues/41) tracks a possible move to Azure
  Artifact Signing).

## Testing and CI

([#36](https://github.com/knausj85/Windows-Hinting/issues/36))

Three tiers:

1. **Core: comprehensive xUnit v3 unit tests** — engine intent→effect(+conditions), scan stages,
   `ScanPolicy` matching, preferences migrations. Build-engine → submit intents → assert; **no
   mocks ever** (a test that needs a mock means the seam is wrong; no mocking library enters the
   dependency tree). Runs via `dotnet test` on `ubuntu-latest`.
2. **Interop.Uia: thin smoke suite** (~a dozen tests) asserting the seam contract against the
   committed fixture app; activation asserted by an observable fixture reaction. Rides the Debug
   build job on `windows-latest` and **gates merge**. Flake hygiene: small suite, generous
   timeouts, one automatic retry, fix-or-delete (never `[Skip]` and abandon); escape hatch is
   demotion to non-gating-but-reported.
3. **Rendering/tray/hooks/updater: manual only**, via [`smoke-test.md`](smoke-test.md). Running the
   named sections is part of every phase's definition of done and the pre-release ritual. No
   automated whole-app E2E — global hotkeys, low-level hooks, and topmost overlays are exactly what
   misbehaves on CI runners.

**PR gate set:** lint (pre-commit) + Core tests (ubuntu) + Debug build with smoke suite. The
Release + MSI job runs on `main` pushes and `workflow_dispatch` only. **No coverage threshold.**
`TreatWarningsAsErrors` on for Core only. Full-app CI stays MSBuild-bound until the CsWin32 gate
lifts; only the Core build/test jobs use the dotnet-only path.
