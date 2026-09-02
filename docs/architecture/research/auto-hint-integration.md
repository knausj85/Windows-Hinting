# Research: SurfaceWatch adapter + AutoHintSource integration into the modernize intent funnel

**Ticket:** [#46](https://github.com/knausj85/Windows-Hinting/issues/46) (wayfinder research, AFK).
**Question:** exactly how does a C#-native auto-hint capability attach to the modernized
architecture's intent funnel and `HintSource` seam, and how much (if any) of the Core engine must
change?

This is an **integration blueprint** for the downstream spec (ticket D) to build on. It cites
primary sources: the architecture docs on `origin/modernize`, the ratified engine prototype on
`origin/prototype/hint-engine-intent-effect`, and the shipped code on this repo. Detection is
already settled by Prototype A (#45) and is **not** re-derived here.

---

## TL;DR

A `SurfaceWatch` host adapter classifies the foregrounded surface and posts one **new** push
intent, `ShowSurfaceHints`, distinguished in the engine by one **new** source discriminant,
`AutoSurface`. Everything downstream of that — the scan, the overlay, dismissal, activation, and the
hotkey take-over — is **reused, not added**: it flows through the existing `BeginScan` effect, the
`HintSource` scanner seam (a new `AutoHintSource` implementation in `Interop.Uia`), the existing
`ForegroundChanged` / `AutoHideElapsed` / `KeyPressed` dismissal paths, and the existing
source-switch branch of `OnToggle`. **The five one-shot effects and the reconciliation model change
by zero. The two Core closed sets (`Intent`, source enum) take two additive variants plus a small
guard.** "Zero engine changes" therefore does **not** strictly hold — but the delta is minimal,
additive, and explicitly anticipated by the design ("future sources" is already a named enum member
in the scanner seam).

---

## 1. Intents: what must be added

### 1.1 The closed sets as they stand

The ratified engine (prototype `HintEngine.cs`, `origin/prototype/hint-engine-intent-effect:
prototypes/HintEngine.Prototype/HintEngine.cs`) defines:

- **`Intent`** (closed): `ToggleHints`, `ToggleTaskbarHints`, `KeyPressed`, `ScanCompleted`,
  `ScanFailed`, `ForegroundChanged`, `AutoHideElapsed`, `DisplayChanged`, `OptionsApplied`.
- **`Effect`** — the five one-shots named in `target-architecture.md` § "The engine and the intent
  funnel": `BeginScan`, `ActivateElement`, `Notify`, `PlayErrorBeep`, `SuppressKey`. (The prototype
  also carries `Set*/Show/Hide` effects, but target-arch and #38 collapse those into
  `DesiredConditions` — "everything continuous is derived".)
- **Source discriminant** — the prototype's `enum HintSource { ForegroundWindow, Taskbar }`, carried
  in `BeginScan(int Token, HintSource Source)` and in the `Scanning` / `Active` mode states.
  `target-architecture.md` § "The scanner seam" describes this axis as
  "each mode (ForegroundWindow, Taskbar, **future sources**)". (Note the name collision: this engine
  **enum** selects *which* source; the scanner **seam** interface of the same conceptual name is the
  *implementation*. This doc calls the enum the "source discriminant".)

### 1.2 What auto-hint needs

A `SurfaceWatch` adapter must be able to say **"show hints for THIS classified surface, now"**. That
is a **push**, not a toggle, and it must be distinguishable from a manual foreground session. Two
additive Core changes:

**(a) New source discriminant `AutoSurface`.** This is not bookkeeping — it is *load-bearing* for
the hotkey take-over (§4.2). Add it to the engine's source enum; add nothing to the five effects.

**(b) New intent `ShowSurfaceHints(long AtMs)`.** A dedicated push intent, timestamp-stamped at the
funnel like the toggles (the engine never reads a clock — `target-architecture.md` § "The engine and
the intent funnel": "The host stamps toggle intents with a timestamp at the funnel"). Its handler
`OnShowSurface` differs from `OnToggle` and is why it cannot reuse `ToggleHints`:

- **It must not toggle-off.** `OnToggle` deactivates when you re-request the source already shown
  (prototype `OnToggle`: `if (current == requested) return Deactivate();`). A second surface event
  must never blank the overlay.
- **It must not interrupt a manual session** (auto-hint is *additive, opt-in* — map #44 given).
  Guard: act only from `Inactive`, or from an `Active`/`Scanning` session whose source is already
  `AutoSurface` (re-target to the newer surface via the existing switch teardown). If the user is in
  a manual `ForegroundWindow`/`Taskbar` session, `ShowSurfaceHints` is a no-op.
- Otherwise it mirrors `StartScan`: bump the scan token, enter `Scanning(AutoSurface, token)`, emit
  `BeginScan(token, AutoSurface)`. From there the flow is identical to a manual scan.

### 1.3 Does "zero engine changes" hold?

**No, not strictly — and it cannot, for a good reason.** The truly-zero option (leave the engine at
`Source = ForegroundWindow`, and let the host's `BeginScan` adapter silently scan the classified
surface instead of the plain foreground window when `SurfaceWatch` has something latched) **fails two
of the four required behaviors**:

- The hotkey **hide-then-take-over in a single press** (§4.2) relies on the engine seeing the auto
  session as a *different* source from `ForegroundWindow`, so `ToggleHints` routes through the
  source-**switch** branch rather than the toggle-**off** branch. With one shared source it can only
  *hide*, never *take over* in one press.
- "Additive / do not interrupt a manual session" cannot be expressed if the engine can't tell an
  auto session from a manual one.

So one new source value is *necessary*. Given that, a dedicated `ShowSurfaceHints` intent is the
clean carrier (§1.2). The honest framing for the spec:

> The **effect set and the reconcile-`DesiredConditions` model need zero changes.** The engine's
> existing branches (source-switch, foreground-change dismissal, key-commit, auto-hide) are
> **reused unmodified in spirit**. The two Core *closed sets* take **two additive variants**
> (`AutoSurface`, `ShowSurfaceHints`) plus a small guard in `OnShowSurface` and a two-line widening
> of `OnForegroundChanged` / `DesiredConditions` (§4.1). This is exactly the "future sources"
> extension the scanner seam already names, not a re-architecture.

This is consistent with the #33 "zero engine changes" promise, which was scoped to a *control
surface* re-expressing existing commands (`target-architecture.md` § "External surfaces": a Talon RPC
adapter "that parses messages and posts intents — zero engine changes"). Auto-hint is a genuinely
*new capability* (a new source), so it legitimately adds a source variant — the same way the seam
already reserves room for "future sources".

---

## 2. The `AutoHintSource` contract

### 2.1 The seam, and where auto-hint plugs in

`target-architecture.md` § "The scanner seam" and `phase-04-scan-pipeline.md` § Work define it:

- `HintSource` is the seam; each source is an implementation "yielding scan targets" carrying
  `{Bounds, Name/metadata, ElementRef}`; **COM stays behind the seam** (implementations live in
  `WindowsHinting.Interop.Uia`).
- Pure Core stages (clamp, dedup, filter, label) transform the targets, "selected and parameterized
  by a `ScanPolicy` resolved per (source × matched window rule)".
- `phase-04` grows `WindowRule` "from RootStrategy-only into the fuller policy carrier" and moves
  `WindowRuleRegistry` resolution into pure Core.

`AutoHintSource` is a **third `HintSource` implementation** in `Interop.Uia`, beside
`ForegroundWindowSource` and `TaskbarSource`. It sits directly on the code that already exists:
`UIAutomationService.FindClickableElements` already resolves a `RootStrategy` per window
(`WindowRuleRegistry.ResolveStrategy(exe, class, title)` — `WindowRuleRegistry.cs:83`, called at
`UIAutomationService.cs:323`) and walks from the strategy-chosen root (`ApplyStrategy`,
`UIAutomationService.cs:543`; `SearchHostCustomStrategy` already fully implemented,
`UIAutomationService.cs:335` / `:505`). The default `SearchHost` rule already ships
(`WindowRuleRegistry.cs:130-134`). So auto-hint reuses the existing per-surface root-scoping wholesale.

### 2.2 How the runtime-classified surface reaches the source

The subtle part: **the engine's `BeginScan(token, AutoSurface)` carries no surface payload** — by
design, because `ElementRef` opacity and the "no COM/Win32 in Core" rule
(`target-architecture.md` § Projects, § "The scanner seam") forbid a surface descriptor (hwnd, COM
root) from living on a Core intent/effect. The surface identity therefore travels **host-side, out
of band**, bound to the scan token:

```
SurfaceWatch (host)                    Funnel (host, UI thread)         BeginScan adapter (host)
─────────────────                      ────────────────────────         ───────────────────────
classify foregrounded hwnd
  → ClassifiedSurface {                post ShowSurfaceHints(AtMs)
      Hwnd, Exe, Class, Title,   ───▶  + hand ClassifiedSurface   ───▶  engine returns
      RootStrategy, ScanPolicy }         to the funnel side-channel        BeginScan(token, AutoSurface)
                                       engine: Scanning(AutoSurface,       funnel binds token → the
                                         token)                            snapshotted ClassifiedSurface
                                                                          → invoke AutoHintSource(surface)
                                                                          → walk from RootStrategy root
                                                                          → yield {Bounds,meta,ElementRef}
                                                                          → Core stages by ScanPolicy
                                                                          → post ScanCompleted(token,
                                                                              surface.Title, hints)
```

**Contract shape (proposed for the spec):**

- Host value `ClassifiedSurface` (host layer, may hold hwnd / COM-adjacent handles — never crosses
  into Core): `{ IntPtr Hwnd, string Exe, string Class, string Title, RootStrategy Root,
  ScanPolicy Policy }`. `RootStrategy`/`ScanPolicy` are the Core-resident policy types phase-04
  produces; the hwnd is host-only.
- `AutoHintSource : HintSource` (in `Interop.Uia`) is **parameterized per-scan by a
  `ClassifiedSurface`**, not by ambient "current foreground". It walks from the surface's
  `RootStrategy` root (reusing `ApplyStrategy`) and yields the seam's `{Bounds, Name/metadata,
  ElementRef}` targets. COM never escapes.
- **Token binding is the contract's crux.** The funnel must snapshot the `ClassifiedSurface` that
  accompanied a given `ShowSurfaceHints` and bind it to the `token` the engine mints, so a *newer*
  surface event that arrives mid-scan does not redirect the in-flight scan. The engine's existing
  scan-token staleness guard (`ScanCompleted` with a stale token is dropped — prototype
  `OnScanCompleted`; `target-architecture.md` § "The engine and the intent funnel": "Scan-token
  staleness guard") then discards the losing scan for free. The host side of this binding
  (side-channel snapshot keyed by token) is the one genuinely new piece of host plumbing.
- `ScanCompleted` carries `Window = surface.Title` (or a stable surface id) so the auto session's
  `Active.Window` is populated for foreground-change dismissal (§4.1).

The result: from `ScanCompleted` onward, an auto session is **byte-for-byte the same** as a manual
one — same `Active` state shape, same overlay display-model slice of `DesiredConditions`, same
`ActivateElement` path.

---

## 3. Adapter placement & threading

### 3.1 Placement — host layer, inbound adapter

`SurfaceWatch` lives in the **host** (`Windows-Hinting`, the composition root / adapters project),
exactly like the other inbound adapters phase-03 enumerates (hotkey, tray, foreground watch, timers,
scan completion — `phase-03-host-cutover.md` § Work "Inbound adapters"). It cannot live in Core
(`net10.0` neutral: "a WinForms/Win32/COM leak into Core is a compile error" —
`target-architecture.md` § Projects) and does not belong in `Interop.Uia` (which owns UIA *scanning*
adapters, not event hooks). It is registered in `ServiceCollectionExtensions.cs` beside the other
host singletons (cf. `ServiceCollectionExtensions.cs:37-53`).

Its inputs are Prototype A's (#45) mechanism: `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` primary +
UIA `FocusChanged`, class→title→host classification, per-hwnd idempotency + ~50 ms debounce.

### 3.2 Threading — one funnel, marshal onto the UI thread

The governing rule (`target-architecture.md` § "Threading model", from #31/#38):

> "The host serializes **all** intents onto the UI thread. The engine is synchronous and makes no
> threading assumptions. Async work (scans, deferred activation) completes by posting new intents —
> **never by calling back into the engine from another thread**."

WinEvent-hook and UIA `FocusChanged` callbacks arrive on **their own threads** (the WinEvent hook
thread / a UIA worker). `SurfaceWatch` must therefore **marshal each classified event onto the UI
thread and enter through the single funnel** as a `ShowSurfaceHints` intent — the same posting
discipline every other adapter uses (`phase-03` § Work: "each maps events → intents; no decision
logic"). Concretely: `BeginInvoke` to the UI-thread funnel, never touch engine state from the
callback thread.

The **classification and debounce/idempotency are input hygiene in the adapter, not engine logic** —
directly analogous to `target-architecture.md` § "Threading model": "Key-repeat suppression is input
hygiene in the hook adapter, not engine logic." The engine sees only clean, already-classified
"show this surface" intents; raw WinEvent noise never reaches it.

### 3.3 One WinEvent source, two engine roles

There is a genuine subtlety worth stating: the modernized architecture already has a
**foreground-watch adapter** (`ForegroundWindowHookService` → `ForegroundChanged` intents,
`phase-03` § Work), and it is **reconciled on/off** by `DesiredConditions.ForegroundWatch` — i.e.,
it runs **only during an `Active` session, for dismissal** (`target-architecture.md`
§ "The engine and the intent funnel": "Foreground watch starts at scan completion"; prototype
`OnScanCompleted` sets watch on only for `ForegroundWindow`). `SurfaceWatch` is different: it is
**always-on while auto-hint is enabled**, and drives session *creation*.

These are two engine *roles* (dismiss vs. show) fed from the **same underlying WinEvent
`EVENT_SYSTEM_FOREGROUND` stream**. The recommended placement: `SurfaceWatch` owns the always-on
hook and, per foreground event, posts (in order, both onto the UI thread):

1. `ForegroundChanged(window)` — lets the engine dismiss any session anchored to the *previous*
   window (§4.1);
2. then, iff the new window classifies to an opt-in surface, `ShowSurfaceHints(AtMs)` — starts the
   new auto session.

Because intents are serialized on the UI thread, that ordering is deterministic: tear down old, then
stand up new. Whether `SurfaceWatch` **subsumes** the phase-03 `ForegroundWindowHookService` (one
adapter, one hook, two intent kinds) or **sits beside** it (two adapters sharing a hook) is a
placement decision for the spec — see Open Questions.

---

## 4. Lifecycle interplay (dismissal + hotkey take-over) as intents

Everything below is expressed in **existing** intents/handlers except where marked NEW. This is the
core payoff: auto-hint's lifecycle is almost entirely *already implemented* by the ratified engine.

### 4.1 Dismissal

| Lifecycle event | Intent | Engine handling | Change |
|---|---|---|---|
| Surface closed / activation moved to another window | `ForegroundChanged(window)` (existing) | Deactivate the `Active` session when `window != Active.Window` | **Widen** `OnForegroundChanged` + `DesiredConditions` to treat `AutoSurface` like `ForegroundWindow` |
| `Esc` | `KeyPressed(VK_ESCAPE)` (existing) | clear filter, else Deactivate (prototype `OnKey`) | none |
| Auto-hide timeout (backstop) | `AutoHideElapsed` (existing) | Deactivate if `Active` (prototype) | none — `DesiredConditions` arms the timer for `AutoSurface` too |
| User selects a hint | `KeyPressed` commit (Space/Enter, existing) | teardown + `ActivateElement` (prototype `OnKey`) | none |

The single **widening**: today `OnForegroundChanged` deactivates only for
`a.Source == HintSource.ForegroundWindow` (prototype), and `OnScanCompleted` arms `SetForegroundWatch`
only for `ForegroundWindow`. Treat `AutoSurface` identically:

- `DesiredConditions`: `ForegroundWatch = a.Source is ForegroundWindow or AutoSurface`; `AutoHideMs`
  already set for any `Active`.
- `OnForegroundChanged`: deactivate when `a.Source is ForegroundWindow or AutoSurface && f.Window !=
  a.Window`.

That one change makes **surface-close and activation-moved-away collapse into the same existing
foreground-watch mechanism** — no dedicated `SurfaceClosed` intent needed, because both manifest as
"the foreground moved off the surface window", which the watch already reports. (If a surface can be
*destroyed while still foreground* — rare — a dedicated `EVENT_OBJECT_DESTROY`-driven
`ForegroundChanged`-to-null suffices; still no new intent.)

### 4.2 Hotkey hide-then-take-over — free from the existing switch branch

Map #44 (given): "hotkey while an auto-overlay is up = **hide-then-take-over** (single press →
foreground-window hints)." This falls out of the **existing** `OnToggle` source-switch branch with
**no new engine code** — *because* the auto session's source is the distinct `AutoSurface` (§1.3):

Prototype `OnToggle`: with `current = AutoSurface` and `requested = ForegroundWindow`,
`current != requested`, so it takes the **switch** path — tears down the visible auto session
(`HideOverlay`, clear filter, `SetForegroundWatch(false)`, reset tray action) **and** `StartScan(
ForegroundWindow)`. That is precisely *hide the auto overlay, then take over with foreground-window
hints, in one press*. Had auto reused `ForegroundWindow`, this same press would hit the toggle-**off**
branch (`current == requested → Deactivate`) — hide only, no take-over. This is the concrete reason
`AutoSurface` must be a distinct discriminant.

The taskbar hotkey (`ToggleTaskbarHints`) while an auto overlay is up behaves analogously: switch
`AutoSurface → Taskbar`. Also free.

### 4.3 Interaction with the existing foreground-watch — no double-dismiss

Because §3.3 routes both `ForegroundChanged` (dismiss) and `ShowSurfaceHints` (show) through one
serialized funnel, and because the engine dismisses on a foreground change *away from the anchored
window*, the sequence "user alt-tabs from surface A to surface B (also opt-in)" is deterministic:
`ForegroundChanged(B)` deactivates the A auto session; then `ShowSurfaceHints` (carrying B) starts a
fresh scan. No race, no double overlay — the scan-token guard (§2.2) plus UI-thread serialization
guarantee it. The one thing the spec must nail is **ordering** (dismiss-before-show) and the
**token↔surface binding**, both host-side.

---

## Open questions surfaced for the spec (ticket D) and the catalog schema (#47)

1. **Token↔surface binding (host plumbing).** The engine's `BeginScan` deliberately carries no
   surface payload, so the host must bind the snapshotted `ClassifiedSurface` to the engine-minted
   scan token via a funnel side-channel. Exact mechanism (a `Dictionary<token, ClassifiedSurface>`
   populated when the funnel observes `BeginScan(_, AutoSurface)`, vs. threading the surface through
   the `BeginScan` outbound-adapter call) is unspecified here — **spec (D) must pin it.**

2. **One adapter or two for the foreground hook (§3.3).** Does `SurfaceWatch` subsume the phase-03
   `ForegroundWindowHookService`, or sit beside it sharing the WinEvent source? Affects phase-03's
   adapter list. **Spec (D).**

3. **`ScanPolicy` reuse for auto surfaces (#47 catalog schema).** phase-04 resolves `ScanPolicy` per
   *(source × matched window rule)*. Auto surfaces need policy too. Does the **surface catalog**
   (#47) reuse `WindowRule` rows (grown to carry `ScanPolicy` per phase-04) with an added
   "auto-hint: on" flag and `RootStrategy`, or is it a *parallel* catalog keyed by classification?
   The map (#44) says "reuse/extend `WindowRule`/`RootStrategy`" — #47 must decide the concrete
   schema (one table with an opt-in column vs. two). This research assumes the reuse path.

4. **Per-surface auto-hide vs. global timeout.** §4.1 uses the existing global `AutoHideMs` as the
   backstop. If auto surfaces want their own (e.g. longer/none) timeout, that is a per-surface
   `ScanPolicy`/catalog field feeding `DesiredConditions` — a #47 schema field + a tiny
   `DesiredConditions` parameterization. Not required for v1; flagged.

5. **Opt-in gate location.** "Additive, opt-in per surface" is enforced at *classification* time in
   `SurfaceWatch` (only opt-in surfaces produce `ShowSurfaceHints`) — so the engine guard in
   `OnShowSurface` is a *safety* net, not the primary gate. Confirm the master on/off ("auto-hint
   enabled") lives as a `preferences.json` scalar reconciled like any other option (it turns the
   `SurfaceWatch` hook on/off), consistent with phase-07's one-propagation-path. **Spec (D)/#47.**

6. **Destroyed-while-foreground surfaces.** §4.1 notes the rare case; confirm whether an
   `EVENT_OBJECT_DESTROY`-driven dismissal is in scope for v1 or deferred.

---

*Sources: `docs/architecture/target-architecture.md` (§§ Projects, "The engine and the intent
funnel", "Threading model", "The scanner seam", "External surfaces"),
`docs/architecture/phases/phase-02-hint-engine.md`, `phase-03-host-cutover.md`,
`phase-04-scan-pipeline.md`, `docs/architecture/README.md`; ratified engine prototype
`origin/prototype/hint-engine-intent-effect:prototypes/HintEngine.Prototype/HintEngine.cs`; shipped
code `Windows-Hinting/Configuration/{RootStrategy,WindowRule,WindowRuleRegistry}.cs`,
`Windows-Hinting/Services/UIAutomationService.cs`, `Windows-Hinting/HintController.cs`,
`Windows-Hinting/ServiceCollectionExtensions.cs`. Detection settled by Prototype A (#45).*
