# Research: challenging the modernize plan before execution

**Date:** 2026-10-15. **Reviewer stance:** skeptical senior architect. Every finding cites a file,
commit or issue. **Scope:** the spec in `docs/architecture/` on `modernize` (README,
`target-architecture.md`, `smoke-test.md`, phases 00–11, `research/winui-preferences-migration.md`,
`CONTEXT.md`), measured against what has happened since it merged on 2026-08-07: the scroll-regions
spec (`docs/scroll-regions/` on `spec/scroll-regions`, PR #105), the wayfinder maps #44, #51, #54/#62,
#72, #81, #94 and #103, the unmerged branches, and current `main` (`e331d0d`).

**Tags:** **[change-now]** = amend the spec before phases 01–04/06 start, because those phases
would bake the wrong shape in. **[change-later]** = real, but can wait for the phase that touches
it. **[keep]** = challenged and still right. **[opportunity]** = unplanned, newly cheap.

Facts were re-checked on 2026-10-15 against NuGet, GitHub, the .NET release index and Microsoft
Learn (sources in §3).

---

## Executive summary

1. **The gate on phase 10 lifted six weeks ago.** CsWin32 PR #1746 shipped in **v0.3.333
   (2026-09-03)**; the latest is v0.3.363 (2026-10-14). The spec still says "do not start". Phase 10
   can be scheduled now, and should come **early**: it deletes the MSBuild/VS Build Tools tax that
   every scroll slice and every agent session pays today.
2. **`modernize` is not "main + docs".** It carries `2030b29` (an always-on foreground WinEvent hook,
   `ForegroundWindowHookService`) that `main` lacks. A competing implementation,
   `HintDismissalWatcher` (`a324706`), sits on a **local-only** branch that the scroll spec depends
   on. They conflict textually and semantically. Phase 03 names the one that will not survive.
3. **The engine's core ideas still hold** (pure `Transition`, one-shot effects, derived
   `DesiredConditions`, scan tokens, ElementRef seam). Scroll (#63) and auto-hint (#46) both fitted
   additively. **[keep]**
4. **The engine's state shape does not hold.** "Three modes, one overlay shown only in a mode" can't
   express a persistent taskbar overlay (#81), an armed auto-hint watch (#44), chained re-hinting
   (#72) or scroll (#62) at the same time. Phase 02 must model **orthogonal regions** (one
   interactive session, plus ambient layers) and a **layered display model**, or each feature will
   rewrite the engine's core switch.
5. **There is no shared watch subsystem, and five features each bring their own.** Dismissal,
   auto-hint SurfaceWatch, chained menu events, taskbar UIA/MSAA/registry triggers and the
   diagnostics "last active window" tracker each install their own WinEvent hooks, UIA handlers,
   timers and threads (see §1.4). One `WatchHub` (a dedicated thread with demux, dedup and debounce)
   is the highest-value new component.
6. **The threading model puts the low-level keyboard hook on the busiest thread.** Synchronous
   `SuppressKey` means the hook runs `Submit` on the UI thread. That thread also paints every
   overlay, runs activation COM calls and hosts settings. Windows silently removes a hook that
   exceeds the timeout (cap 1000 ms). Move hooks to a dedicated input thread; decide suppression
   from a published capture snapshot.
7. **Window rules have outgrown phases 04 and 07.** The scroll spec's `window-rules.md` (sections,
   app definitions, `url`/`appId`, exclusions, most-specific-wins per section) supersedes
   `ScanPolicy` and the phase-07 rule format. Phases 04 and 07 must adopt it, not re-invent it. The
   "no rules UI, not even an open-folder link" rule is now too austere.
8. **Sequencing:** stop treating `modernize` as a long-lived branch. Run trunk-based on `main`:
   reconcile the dismissal fork, merge phase 00, **phase 01 next (before scroll slices 01 and 03)**,
   then phase 10. Scroll slices land leaves in `Core`/`Interop.Uia` with thin throwaway glue. Phases
   02+03 then absorb scroll glue once, during a short feature freeze.
9. **CI and testing underweight the hard parts.** UIAccess is never exercised by Debug builds. The
   scroll actuator's cursor-independence claim is still unproven on bare metal (G #69). The fixture
   app has no scroll regions, menus or Chromium content. Split phase 05 and move its first half
   forward as a characterization net.
10. **Top opportunities, ranked by value/cost:** the WatchHub; a single `UiaWorker` (MTA) that owns
    COM, ElementRefs and UIA events; serializable intents plus a state snapshot (cheap insurance for
    the G2 external-channel map); a JSON Schema for rule files; fixture-app expansion; and a reusable
    probe harness.

---

## 1. Does the plan still fit?

### 1.1 [keep] The pure engine, effects-as-one-shots and derived conditions

Two independent features have now been fitted onto the #31/#38 model without breaking it:

- Scroll (`docs/scroll-regions/design.md` §2, decided in A #63) adds a `Scrolling` mode, seven
  intents, one effect (`ScrollElement`) and one condition (`ScrollDrive`). The velocity loop stays in
  the actuator, so "nothing continuous is an effect" survives.
- Auto-hint (`docs/architecture/research/auto-hint-integration.md` on `research/auto-hint-integration`,
  #46) adds one source discriminant (`AutoSurface`) and one intent (`ShowSurfaceHints`). Effects and
  the reconciliation model are unchanged.

That is strong evidence the *mechanism* is right. Keep the transition/effect/condition split, the
scan token, the no-clock rule and the ElementRef seam.

### 1.2 [change-now] Model the state as orthogonal regions, not one mode enum

`target-architecture.md` fixes `Inactive / Scanning / Active`, and phase 02 builds `State` around a
single `mode`. The features already decided or prototyped need several things to be true **at the
same time**:

| Feature | What it needs alongside a click session | Evidence |
|---|---|---|
| Scroll | A fourth *session* kind, mutually exclusive with click | design.md §2 "Entering it from a click mode supersedes that mode" |
| Persistent taskbar overlay | Numbers shown **with no session at all**, kept current by events | #81 verdict GO; `prototype/taskbar-overlay-probe` `be4329d`, `1b533bf` |
| Auto-hint | A watch **armed while Inactive**, which may start a session by itself | #44, #46 blueprint §1.2 |
| Chained hinting | After `ActivateElement`, the session **re-enters** Scanning instead of ending, until Esc | #72; `prototype/chained-hinting` (local only) |
| Menu-bar auto-hint | Its own source plus a menu-scoped watch | `28698ff` (#79), `HintSource.MenuBar` |

Recommended shape for phase 02:

```
State = {
  Session: Inactive | Scanning(source, token) | Active(...) | Scrolling(...),
  Chain:   Off | On(source)          // what Active does after an activation
  Ambient: { TaskbarBadges: Off|On(snapshotToken), AutoHint: Off|Armed }
  Options
}
Transition = per-region reducers composed in one place
DesiredConditions(state) = union of per-region condition slices
```

The rest of the design is unchanged: one `Transition`, one funnel, effects stay one-shot. What
changes is that each feature adds a *region reducer and a condition slice* instead of editing a
central `switch`. The closed `Intent`/`Effect` sets should be described as "closed per release,
extended additively". The spec's current wording ("exactly five") is already contradicted by the
scroll spec's sixth effect.

### 1.3 [change-now] The overlay display model must be layered

`target-architecture.md` § Overlay and phase 06 assume one display model, "overlay visibility +
hints + filter", visible only in a mode. Three different visual products now ride the overlay:

- click hints (session);
- scroll badges and the active outline (session, `design.md` §6);
- persistent taskbar/tray numbers (ambient, #81).

Auto-hint popups and chained re-hints reuse the click layer. Phase 06's composer should take
`DisplayModel { Layers: [SessionLayer?, AmbientTaskbarLayer?] }` and compose them in a fixed z-order.
Two follow-on constraints belong in the spec now:

- **The mode marker reads only the session layer.** The `[Active]`/`[Scroll]` title is what Talon
  matches (`community/apps/windows_hinting`, substring `"[Active]"`; CONTEXT.md "Mode marker"). A
  persistent ambient layer must never set it. If ambient numbers live on separate windows (for
  example taskbar-sized windows, so the full-screen per-monitor overlays can stay hidden), those
  windows need a **different title** so Talon's app/title scope doesn't flicker.
- **"Redraw only on state change"** still holds. Ambient updates are state changes that arrive from
  the WatchHub (§1.4). They are not timers.

### 1.4 [change-now] Add a shared watch subsystem (WatchHub)

The spec has exactly one watcher: "foreground watch, started at scan completion". Inventory of what
is already built or decided:

| Consumer | Signals | Lifetime | Where today |
|---|---|---|---|
| Click/scroll dismissal | `WH_MOUSE_LL` button-down, `EVENT_SYSTEM_FOREGROUND`, target `EVENT_OBJECT_LOCATIONCHANGE` | while a session is up | `HintDismissalWatcher` (`a324706`, local only); design.md §7 |
| Foreground auto-hide (competing) | `EVENT_SYSTEM_FOREGROUND` | always-on | `2030b29` on `modernize` |
| Rule diagnostics | `EVENT_SYSTEM_FOREGROUND` (last non-self, non-taskbar window) | **always-on** | window-rules.md §5, slice 05 |
| Auto-hint SurfaceWatch | menu/popup WinEvents, classification | **always-on** when enabled | #44, #45, #46 |
| Chained / menu-aware | UIA `MenuOpened`/`MenuClosed`, `StructureChanged`, 150 ms settle polls by RuntimeId | per chain | `prototype/chained-hinting` `55f50f6`, `b6976b0` |
| Taskbar overlay | UIA `StructureChanged` + container `BoundingRectangle`, MSAA `REORDER`/`STATECHANGE` on the explorer pid, `RegNotifyChangeKeyValue`, display change + 400–600 ms settle | **always-on** when enabled | `1b533bf`; #81 verdict |

Six consumers. Three are always-on, and four bring their own thread or timer. Built separately,
each re-solves the same problems: out-of-context hook threads, self-process filtering, injected-event
filtering, RuntimeId dedup, debounce/coalesce, sender-identity filtering (#81 build caveat) and
shutdown ordering.

Recommendation: a **WatchHub** in the host (WinEvent and LL-mouse hooks) plus its UIA-event
counterpart in `Interop.Uia` (§1.6). It should own:

- one dedicated thread with its own message loop for `SetWinEventHook(OUTOFCONTEXT)` and
  `WH_MOUSE_LL`;
- reference-counted subscriptions, driven by `DesiredConditions` slices (`DismissalWatch`,
  `SurfaceWatch`, `TaskbarWatch`, `LastActiveWindowTracking`);
- coalescing into **intents** (`ForegroundChanged`, `TargetMoved`, `SurfaceAppeared`,
  `TaskbarDirty`), posted to the funnel.

It also answers the question #46 left open for ticket D: does `SurfaceWatch` "subsume or sit
beside" phase 03's foreground adapter? It does neither. Both are subscribers to the hub. #46 already
recommends "one WinEvent source feeding two intent kinds".

This also settles the "foreground watch only after scan" delta (phase 03). An always-on foreground
subscription exists anyway for diagnostics, so the engine simply ignores `ForegroundChanged` when it
doesn't care. The ~100 ms accepted delta disappears.

### 1.5 [change-now] Take the low-level hooks off the UI thread

Phase 03 says: "The keyboard-hook adapter reads `SuppressKey` synchronously from the batch `Submit`
returns". A `WH_KEYBOARD_LL` callback runs on the installing thread. On `main` that is the STA UI
thread (`NativeInterop/KeyboardHook.cs`, installed by `KeyboardHookService.Start` from
`HintController.OnModeChanged`). The design therefore requires the engine, the hook and the UI to
share one thread. After this effort, that thread also:

- composes and paints up to N per-monitor bitmaps (phase 06, plus scroll badges and ambient layers);
- executes `ActivateElement`. On `main`, activation already runs UIA pattern calls on the UI thread
  (`OnSelectionCommitted` → `_overlay.BeginInvoke`). A slow `Invoke` on a hung target stalls every
  later key;
- hosts the settings window (phase 08) and the log viewer.

Windows silently removes a low-level hook whose callback exceeds `LowLevelHooksTimeout`. The cap is
1000 ms on Windows 10 1709 and later, and the app is not told
([LowLevelKeyboardProc](https://learn.microsoft.com/windows/win32/winmsg/lowlevelkeyboardproc)).
Microsoft's guidance is a dedicated hook thread that returns immediately. The scroll spec already
learned this the hard way for actuation (design.md §4.1 principle 3).

Recommendation:

- Install the keyboard and mouse hooks on a **dedicated input thread**.
- Phase 02 publishes the `InputCapture` slice of `DesiredConditions` as an immutable snapshot
  (mode, which keys pass through, whether `HJKL` key-ups are eaten).
- The hook thread calls a pure Core function, `CaptureDecision(snapshot, keyEvent) → Suppress|Pass`,
  synchronously, and posts the key intent to the funnel.

`SuppressKey` then stops being an effect: it becomes a pure function over a condition. That is more
faithful to #38's own principle. It also removes the only reason `Submit` must be callable inside a
hook.

The race to name explicitly is a key arriving after a toggle but before the snapshot is republished.
It already exists on `main` (hook install happens after the mode change), so it is not a regression.

Related, also **[change-now]**, because it shapes the hook adapter written in phase 03:

- **Tag our own injection** with a `dwExtraInfo` cookie on every `SendInput`, and skip only tagged
  events.
- **Never drop all `LLKHF_INJECTED`/`LLMHF_INJECTED` events.** Talon's keys and clicks are injected.
  Design.md §7 currently says dismissal skips "injected events", so a Talon-issued click would *not*
  dismiss scroll mode. The scroll actuator's parked-cursor moves and wheel events, chained hover
  (`MouseClickService.Hover`) and the mouse-click activator all inject, so a shared, cookie-tagging
  `InputInjector` is needed anyway (Neru lesson 6 in the gap-register addendum on
  `research/neru-architecture`).

### 1.6 [change-now] One UIA worker owns COM, ElementRefs and UIA events

Today:

- Scans run on thread-pool MTA threads, and **an abandoned scan keeps running** after its timeout
  (`UIAutomationService.FindClickableElementsAsync`).
- Activation calls those MTA-obtained elements from the STA UI thread.
- The chained prototype creates a **second `CUIAutomation`** with event handlers.
- The taskbar probe owns a dedicated MTA thread for UIA handlers.
- The scroll actuator runs COM on its own worker, "or marshal to the UI thread, as the prototype
  did" (design.md §8).

Microsoft's UIA client guidance is to keep UIA calls, and especially event-handler registration, off
UI threads. There is also direct evidence that multiple automation instances hurt. #77 recorded
cross-instance cache-request HRESULT errors when the sender came from one `CUIAutomation` and the
cache request from another. #81 requires its subscriptions on an MTA, window-less thread with a
cache request. The spec says only "a ref-table … owns their lifetime/release" (phase 03).

Recommendation: `Interop.Uia` exposes a **`UiaWorker`**, a small set of MTA threads with one owner of:

- the `IUIAutomation` instance;
- the ElementRef table;
- UIA event registrations, the counterpart of the WatchHub;
- the activator chain and the scroll actuator's COM calls, with verify waits off the UI thread.

Explicitly forbid UIA calls on the UI thread.

ElementRef needs **identity**, not only a handle: `{RuntimeId, ScanGeneration}`. Chained
progressive reconcile (#74), taskbar dedup (#81) and stable labels (gap G8) all key on RuntimeId.
Phase 10's "refs must not leak COM past their scan generation" rule then becomes "a generation is
retired when a newer snapshot for the same source supersedes it", which fits continuous sources.

### 1.7 [change-now] `HintSource` must yield snapshots over time, not one batch

Phase 04 defines `HintSource` as yielding scan targets once per `BeginScan`, completing as one
`ScanCompleted`. Two decided features need **successive snapshots for one session**:

- chained progressive settle (150 ms polls, 4 quiet polls, 3 s cap, reconciled by RuntimeId; #74);
- taskbar event-triggered full re-read reconcile (#81);
- Start-menu redraw on ScrollPattern percent changes: a 50–100 ms coalesced re-scan while auto
  hints are up (#49);
- #76's conclusion: "a live, self-updating overlay, not one-shot settle detection".

Recommendation:

- Add `ScanProgress(token, snapshot, isFinal)` as an intent, or make `ScanCompleted` repeatable
  until a final flag.
- Make the labeling stage accept the **previous assignment**, so surviving elements keep their
  labels (RuntimeId → label). That stage is pure and lives in Core.

Cost now: a few types and one stage parameter. Cost later: re-cutting the seam that phase 05's
merge-gating suite has frozen.

### 1.8 [change-now] Window rules: adopt `window-rules.md` as the policy model

Phase 04 grows `WindowRule` into a `ScanPolicy` "resolved per (source × matched window rule)", with
first-match semantics lifted into Core. Phase 07 defines `rules/` files "each a list of rules
carrying `ScanPolicy`", resolved by "specificity, then file order". The scroll spec has since
decided a richer and partly different model (E #67 + 3 amendments, H #88, J #90, K #91, L #92,
I #89):

- **sections** with per-section most-specific-wins and no merging within a section;
- app definitions (the only OR), exclusions, `url`/`appId` matchers and a URL tie-break;
- one string syntax;
- unknown sections kept;
- files shaped as `{apps, rules}`, with bare arrays still accepted.

Phases 04 and 07 must be rewritten to **consume that spec** (`ScanPolicy` → the `root` + `click`
sections; `ScrollDetectionPolicy` → `scrollDetection`). Slice 03 should build the matcher and
resolver **in Core** (see §2), so phase 04 ports nothing.

The same applies to auto-hint. #47 drafted a **separate** embedded `SurfaceRule` catalog that
reuses `RootStrategy`/`ScanPolicy` value types, and #78 asks for a per-app "command-surface profile"
(Office ribbon/QAT). Both should be **sections or app definitions in the one rule language**
(window-rules.md §3.1 already anticipates "`menu`, `settle`" sections). They should not be parallel
catalogs with their own matcher and precedence. Decide this before ticket D (#44) is written.

One wrinkle: resolution is not purely data-in/data-out, because `url` is a lazy UIA read (10–50 ms).
Keep the resolver pure over a `WindowIdentity` with a lazy URL callback, as slice 03 already
proposes.

**[change-later] performance:** the built-in `Slack` app definition has a URL-only alternative
(`{ "url": "app.slack.com" }`), and so does `Gmail`. Per window-rules.md §2.1, the URL read is gated
only on cheap fields matching and a web-host class. An alternative with *no* cheap fields therefore
triggers the read on **every Chromium window**: Edge, VS Code, Teams and Slack desktop. That is a
10–50 ms tax on every click-hint scan there. Measure it in slice 04. The likely fix is to resolve
only the sections the current consumer needs, and to skip the URL read when no URL-bearing rule
could change those sections.

### 1.9 [change-now] "External surfaces: None" is factually wrong

The overlay window title **is** an external contract. Talon's community `windows_hinting.py` scopes
on `"[Active]" in win.title`, and CONTEXT.md now defines the **Mode marker**. Both
`target-architecture.md` § External surfaces and #33 should say:

- "one surface: the mode marker", derived from `DesiredConditions` and unit-tested in Core as a
  string;
- plus "keystrokes in";
- plus the hard rule that **no other window of ours may carry the base title**.

On `main`, the auto-hide timer is a `System.Threading.Timer` precisely because a WinForms timer's
HWND confused Talon (`HintController.cs:39`). That is the same contract, discovered by accident.

### 1.10 [keep, with one amendment] No rules editor UI

Still right. Rules are hand-authored, richer than ever, and the two diagnostics (Copy window
identity, Explain window rules; window-rules.md §5) are the right tooling.

But phase 08 item 4 ("no grid, no file manager, no 'open rules folder' link") now works against
users. The rules are the main customization surface, and the scroll bridge requires the existing
grid to round-trip unknown fields until phase 07 (window-rules.md §4.2). **Amend phase 08:** remove
the grid only after phase 07's migration, and add "Open rules folder" next to the two diagnostics.

### 1.11 [change-later] Scan pipeline details that collide with new work

- **Reading order.** `ReadingOrder.Sort` (`fdec648`, local only) is a pure stage. It belongs in
  phase 04's stage list, and scroll numbering reuses it (design.md §3.2).
- **Root strategy.** PR #87 revives `FileExplorerCustomStrategy` and returns **multiple roots**.
  Phase 04's `HintSource` must accept multi-root resolution.
- **Bounds-clamp.** The Neru deep dive (lesson 7) notes Chromium reports `IsOffscreen=false` past
  the window edge. "Bounds-clamp off" is shipped behavior, but a per-rule `click` field for it is
  the obvious first policy edit.

---

## 2. Sequencing

### 2.1 What the current plan assumes, and what is true

| Assumption (README "Development branch") | Reality on 2026-10-15 |
|---|---|
| `modernize` = `main` + docs; `main` stays quiet and shippable | `modernize` carries code (`2030b29`, via the `refactor` merge `f7e7320`). `main` is about to receive five scroll slices. |
| Phases run back to back | Only phase 00 has a PR (#43, open since 2026-08-07, unreviewed). Feature specs and prototypes have run for 10 weeks. |
| Phase 10 waits on an external gate | The gate lifted 2026-09-03 (v0.3.333). |
| Engine behavior spec = "read the current code" (phase 02) | The current code is a moving target: dismissal, reading order, Explorer tab and scroll glue all land in `HintController`/`HintState`/`HintInputHandler` first. |

Every `main` feature written against `HintController` becomes behavior phase 02 must re-derive and
phase 03 must delete. The conflict analysis (`git merge-tree`) already shows `HintController.cs`
conflicting between dismissal × chained, modernize × dismissal and modernize × chained.
`NativeMethods.txt` conflicts in three more pairs.

### 2.2 Is "build on main, port later" cheaper?

It depends on what is ported:

- **Leaves port almost free:**
  - the actuator, hold timer, cursor hide, region harvest and pure stages (scroll);
  - the rule matcher and URL parser (slice 03);
  - `ReadingOrder`;
  - `HintDismissalWatcher`'s hook code.
  
  The scroll spec already writes these as standalone classes.
- **Glue is thrown away:** `ScrollController`, `HintMode.Scrolling` in `HintStateManager`, the
  scroll branch in `HintInputHandler`, `OverlayForm.SetScrollTargets` (design.md §12). Phase 02/03
  rebuild it from the scroll spec's intent tables, which are already written in engine vocabulary.
  The re-derivation cost is low.
- **What is genuinely costlier on `main`** is anything with *threads and subscriptions* (chained,
  taskbar overlay, auto-hint). Built on `main`, each would invent its own watcher and UIA thread
  (§1.4, §1.6), and phase 03 would have to untangle them. **Those three should not be built on
  `main`.**

So: building scroll on `main` is acceptable **if the leaves are born in their target projects**.
That requires phase 01 first. Slices 01 and 03 otherwise create a `Windows-Hinting.Tests`
(`net10.0-windows`, `InternalsVisibleTo`) project that phase 01 must dismantle (slice 01 item 1).

### 2.3 Recommended order

1. **[change-now] Reconcile the dismissal fork.** Choose `HintDismissalWatcher` (a superset: click,
   foreground and move/resize). Revert `2030b29` on `modernize`, push the three local-only branches
   (`feature/hint-dismissal-parity`, `fix/taskbar-hint-reading-order`, `prototype/chained-hinting`),
   and merge dismissal + reading order to `main`.
2. **Merge phase 00 (#43)**, then **retire the long-lived `modernize` branch**: merge it into `main`
   and run the remaining phases as PRs to `main`. Each phase is already "no behavior change +
   smoke", which is exactly what makes trunk-based safe. The README admits gating is identical on
   both branches, so the branch buys only a merge tax. Keep beta-channel `workflow_dispatch` for
   risky phases.
3. **Phase 01** (solution split; mechanical, about one session). Afterwards Core.Tests runs on
   ubuntu, and scroll's pure helpers and the rule engine land in Core directly.
4. **Phase 10** (CsWin32 `GeneratedComInterface`): the gate is open. It must follow 01 (the interop
   leaf exists) and should precede heavy UIA work. It removes `COMReference`, makes the whole
   solution `dotnet build`-able (agents stop needing VS 18 MSBuild) and replaces ~30 manual
   `ReleaseComObject` sites before scroll and chained add more. Run it **before** scroll slice 02
   adds a second UIA harvester. If the generated code proves immature, it is contained in one
   project, so the fallback is "revert one PR".
5. **Phase 05a (new): fixture app + characterization smoke** against today's
   `UIAutomationService`. This is the regression net for 02–04. Include scroll regions in the
   fixture (§5).
6. **Scroll slices 03 → 01 → 02** on `main`: leaves in Core/Interop.Uia, glue thin and labeled
   throwaway.
7. **Phase 02** with the amended state shape (§1.2), designed with `Scrolling`, `ScrollDrive`,
   layered display and capture snapshot **from day one**.
8. **Phase 03 + 06 together** under a short **feature freeze on `main`**. Phase 03 absorbs the click
   and scroll glue, and phase 06 is needed for the layered overlay. Add the WatchHub and UiaWorker
   here.
9. **Phase 04** (rewritten around effective policy and snapshot streams), then **05b** (the smoke
   suite re-pointed at `HintSource`), then **scroll slices 04–05** on the new seams.
10. **Then** chained (#72), auto-hint (#44) and taskbar overlay (#81) **on the modernized seams
    only.**
11. 07 → 08, with 09 floating. Move 07 earlier if the rules bridge (window-rules.md §4.2)
    accumulates user data. Every month on the bridge grows the v0 migration surface.
12. 11 stays optional, though its value rises (§5).

### 2.4 Merge, split, drop

- **Split 05** into 05a (fixture + characterization, early) and 05b (seam contract after 04).
- **Merge 03 and 06** into one cutover window, or run 06 immediately after 03. A layered display
  model is part of what 03 reconciles.
- **Drop** from phase 04 the "grow `WindowRule` into `ScanPolicy`" work item. It is replaced by
  importing the slice-03 rule engine.
- **Pull forward** phase 10 (§2.3 step 4).
- **Keep 09** floating. It is independent.

---

## 3. Stale or wrong assumptions

| # | Spec says | Now | Tag |
|---|---|---|---|
| 3.1 | Phase 10 gated; "v0.3.296 predates it; do not start" | PR #1746 ("Fix PreserveSig COM source generation") is in every tag from **v0.3.333 (2026-09-03)**. Latest is **v0.3.363 (2026-10-14)**. `main` pins 0.3.275. ([releases](https://github.com/microsoft/CsWin32/releases), [PR](https://github.com/microsoft/CsWin32/pull/1746)) | **[change-now]** |
| 3.2 | `modernize` = `main` + docs (README; also this review's brief) | `2030b29` code is on `modernize` only | **[change-now]** |
| 3.3 | Phase 03 adapts `ForegroundWindowHookService`; smoke §3 "foreground-change dismissal" | That service exists only on `modernize`. `main` has no foreground dismissal at all, so smoke §3 fails on a `main` build. `HintDismissalWatcher` is the intended successor. | **[change-now]** |
| 3.4 | Phase 01 moves code into `Interop.Uia`; references `HintStateManager` semantics | The class lives in `Services/HintState.cs`. Harmless, but phase files should name files that exist. | [change-later] |
| 3.5 | `net10.0` everywhere | .NET 10 LTS is supported until **2028-11-14**. .NET 11 (STS) is at RC2 with GA expected at .NET Conf, 10–12 Nov 2026. Stay on 10. .NET 11 WinForms adds `VisualStylesMode`, `FormRevealMode.Deferred` and `SystemVisualSettingsChanged`, which are tempting for phase 08 but not worth an STS hop. ([releases-index](https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json), [WinForms .NET 11](https://learn.microsoft.com/dotnet/desktop/winforms/whats-new/net110)) | [keep] |
| 3.6 | "xUnit v3" | The `xunit.v3` package line is now **4.0.2** (2026-10-09). 4.0 defaults to Microsoft.Testing.Platform v2 and drops MTP v1. `main` still carries `xunit.v3.extensibility.core` 3.2.2 (phase 00 deletes it). Pin **4.x + MTP v2** in phase 01 rather than 3.x. ([4.0.0 notes](https://xunit.net/releases/v3/4.0.0)) | [change-now] (one line) |
| 3.7 | "NetSparkle + WiX stay; installer unchanged" | NetSparkle 3.1.0 is still current (3.1.1 is in preview). The installer is on **WiX v6.0.2**. WiX **v7.0.0** (2026-04) **enforces the Open Source Maintenance Fee EULA** on every command (OSMF v1.1: sponsorship required above US$10k annual revenue). Decide deliberately whether to stay on v6 or accept the v7 EULA in CI. ([OSMF](https://docs.firegiant.com/wix/osmf/)) | [change-later] |
| 3.8 | WinForms overlay, `TransparencyKey`; ULW optional | Still right for a click-through, zero-UIA-footprint overlay. Scroll badges, ambient taskbar numbers and soft outlines all render over arbitrary content, so color-key fringing shows more often. That raises phase 11's value without making it mandatory. `WDA_EXCLUDEFROMCAPTURE` (gap G14) interacts with gaze-ocr screenshots, so it stays off by default. | [keep] |
| 3.9 | Settings UI in modern WinForms (#40) | Re-confirmed by `research/winui-preferences-migration.md` (#104): WinForms dark mode left experimental in .NET 10. WPF's `ThemeMode` is **still experimental (WPF0001)** in .NET 10, which weakens the #103 WPF Fluent fallback. | [keep] |
| 3.10 | "No external surface; returns when Talon ships RPC" (#33) | Talon 1.0 shipped a *preliminary* `talon-rpc`, but it is Talon-hosted (others drive Talon). The Talon → app direction is always an app-hosted listener, whatever Talon ships (`research/talon-direct-channel`, #93). The trigger in #33 is therefore the wrong trigger. The competitive register proposes **G2: outbound hint state + event stream**, plus G1 select-by-name and G3 external targets. Phase 00's deletion is still right (the code is dead, and the hardened-pipe blueprint survives). What to change now is making intents and the state snapshot **plain serializable data** (§5, O3) so G2 stays an adapter. | [change-now] (cheap); the G2 decision itself is [change-later] |
| 3.11 | "No rules UI; README documents `rules/`" (#40) | The rules are now a sectioned policy language with URL syntax and selectors. Keep no editor; add an open-folder link, the diagnostics and a JSON Schema (§1.10, §5). | [change-later] |
| 3.11a | "One propagation path; the settings UI has no privileged write path" (#39) | #50 has the **app** writing an app-owned `surfaceAvailability` block into `preferences.json`. That makes the watcher react to the app's own writes and blurs "the file is user-authored". App-owned state belongs in a separate `state.json` that is not watched. | [change-later] (before #44 ticket D) |
| 3.12 | Debug builds behave like installed builds | Debug uses `app.debug.manifest` (no UIAccess). Several new behaviors differ under UIAccess (§4.1). | [change-now] (test plan) |
| 3.13 | Scroll design: "Elevated target windows block `SendInput` (UIPI)" (design.md §4.4, §11) | True for the **non-UIAccess** Debug/portable builds. A UIAccess process is exempt from UIPI for input injection, so the installed build should be able to hold-scroll elevated windows. This is unverified. Record the limit per build flavor. | [change-later] |

---

## 4. Risks the plan underweights

### 4.1 UIAccess is the shipping configuration and is never exercised in development

Only Release/MSI builds carry `uiAccess="true"` (`app.manifest:16`), and only when signed and
installed under Program Files. Smoke §10 runs only before a release.

Behaviors that change under UIAccess:

- z-order above Start and elevated surfaces;
- LL hooks seeing input while an elevated window is focused;
- `SendInput`/`SetCursorPos` into elevated windows (scroll parking and holds, mouse-click
  activation);
- WinEvent/UIA access to elevated processes.

Mitigation:

- a documented "UIAccess dev install" lane (signed Debug-Beta MSI to a VM snapshot);
- smoke §10 extended with scroll, dismissal and taskbar rows, made part of the done criteria for
  phase 03 and scroll slice 01.

### 4.2 Hook timeouts are silent

See §1.5. The failure mode is "hints stop responding to typing until restart", with no log line. Add:

- the dedicated input thread;
- a hook-latency histogram in boundary logging (phase 09);
- a cheap **liveness probe**: on entering a capture mode, verify that the hook handle still receives
  events, and reinstall if not.

### 4.3 COM apartments and lifetime

`main` already mixes apartments: elements come from MTA scans and are activated on the STA UI
thread. Abandoned timed-out scans keep running (`Task.Run(...).WaitAsync`), so two harvesters can
overlap on one window.

Scroll adds an actuator worker plus a ticker, chained adds a second `CUIAutomation` plus event
handlers, and the taskbar overlay adds an MTA handler thread. Without the UiaWorker (§1.6) this
becomes four independent COM owners with four release policies, right before phase 10 rewrites
lifetime to ComWrappers.

### 4.4 VM versus bare metal

The scroll actuator's central claim is that the parked hidden-cursor wheel reaches the target
wherever the user's mouse is. It was tested **only in a VM** (design.md "Open items", G #69 item 1),
and the spec makes bare metal part of slice 01's done criteria.

Chained and taskbar timings (150 ms polls, 400–600 ms display settle) were measured on specific
machines. CI runners are VMs. The smoke suite therefore must never assert wheel-based scrolling, and
timing constants must stay named, policy-overridable values (as `scrollActuation` already does)
rather than CI-asserted ones.

### 4.5 CI testing of UIA-heavy features

The plan's tier 2 is a dozen tests against a **WinForms** fixture. The features at risk live in
Chromium/Electron (warm-up retry, axis lies, URL read), WinUI/XAML (Settings, taskbar), WPF (VS) and
Office. A WinForms-only fixture proves the seam, not the behavior.

Accept that limit, but:

- add a WebView2 page to the fixture. It is cheap, ships with Windows 11, and exercises the Chromium
  provider, `RootWebArea` URL reads and `className` tokens;
- add a scrollable list, tree and nested panes;
- add a context menu, for chained and auto-hint.

### 4.6 Process risks

- **Local-only branches.** Three branches hold banked findings and code that the specs cite by hash
  (`a324706`, `fdec648`, `28698ff`). One disk failure loses them.
- **Two spec trees disagree.** `docs/architecture/` says three modes and five effects;
  `docs/scroll-regions/` says four and six. Every agent session that reads only one tree gets the
  wrong picture. Amend `target-architecture.md` (see the decisions below) rather than letting
  follow-on specs override it by reference.
- **Phase 00 PR #43 has been open for 10 weeks.** Stale PRs on a spec branch are a smell that the
  branch strategy isn't working (§2.3 step 2).

### 4.7 Desktop-global side effects

`SetSystemCursor` (scroll cursor hide) and `timeBeginPeriod(1)` (scroll ticker) change system-wide
state. The spec's crash marker covers the cursor. `timeBeginPeriod` should be scoped to holds only,
not the whole mode. Both belong in a single "global side-effects" registry restored on every exit
path. That registry should be shared with any future feature (for example G14 display affinity).

---

## 5. New opportunities (ranked by value ÷ cost)

| Rank | Opportunity | Value | Cost | Notes |
|---|---|---|---|---|
| O1 | **WatchHub**: one hook thread with ref-counted WinEvent/LL-mouse subscriptions → coalesced intents; `DesiredConditions` slices drive subscriptions | High: serves dismissal, diagnostics, auto-hint, chained, taskbar; removes the foreground-watch delta | M (one component, ~300 lines, consolidates `HintDismissalWatcher`) | §1.4. Build it in phase 03. |
| O2 | **UiaWorker** in Interop.Uia: owns `IUIAutomation`, the ElementRef table (RuntimeId + generation), UIA event registrations, activator and actuator COM calls | High: correctness under phase 10 lifetime rules; one place for timeouts | M | §1.6. Pairs with phase 10. |
| O3 | **Serializable intents + `StateSnapshot`** in Core (plain records, System.Text.Json source-gen-friendly; a pure `Snapshot(state)` → labels, names, roles, rects, mode) | Medium-high: G2 (outbound state/events), G1 one-shot verbs and G3 external targets become adapters; also free structured logs for phase 09 | **S now**, L to retrofit | Doesn't reverse #33. It keeps the option open. |
| O4 | **Input thread + capture snapshot + `InputInjector` with cookie** | High (reliability) | S–M | §1.5. |
| O5 | **JSON Schema for rule files** (embedded and published; `$schema` in examples) | Medium: editor validation and IntelliSense for a hand-edited policy language, offsetting "no rules UI" | S (generate from the Core types) | Pairs with Explain window rules. |
| O6 | **Fixture-app expansion**: WebView2 page, scroll panes, nested trees, context menu, a "slow settle" panel, an elevated-instance switch for UIPI tests | Medium-high: lets scroll detection, chained settle and auto-hint have tier-2 tests | S–M | §4.5. |
| O7 | **Reusable probe/diagnostic library**: the UiaWorker and WatchHub exposed so a probe is ~100 lines that emits JSONL timelines (event, sender identity, RuntimeId, Δt) | Medium: every map since #44 hand-built a probe (`AutoHintDetectionProbe`, `ScrollExposureProbe`, `ScrollSelectProbe`, `RuleProbe`, `TaskbarOverlayProbe`, ~5,000 lines in total), each with its own UIA COMReference | M | #51 charters a *forked standalone* tool and rules out an in-app diagnostic. That still fits: after phase 10, the fork references `Interop.Uia` (plain `dotnet build`) instead of re-implementing hooks. #52 found the "latency/gap between events" view is a genuine gap in Accessibility Insights, AccEvent and Inspect. |
| O8 | **Label stability stage** (RuntimeId → label carry-over) | Medium: chained, taskbar (numbers must not shuffle), G8 marks | S in phase 04 | §1.7. |
| O9 | **Talon-facing state beyond the title**: a second, stable title token for source (`[Active] [Taskbar]`) | Low-medium: lets Talon scope commands per source without an RPC | S | Design.md §6.4 already reserves trailing tokens. |
| O10 | **Global side-effects registry** (cursor, timer resolution, display affinity) with a crash marker | Medium (safety) | S | §4.7. |
| O11 | **CsWin32-everywhere → NativeAOT trial** for startup time and footprint of a tray app | Low-medium | M | Enabled by phase 10. Explicitly out of scope there, so it is a follow-on. |

---

## Decisions the owner should make next

Each is phrased as a question with a recommended answer. Each could become a wayfinder ticket.

1. **Is the CsWin32 gate lifted, and does phase 10 move up?** *Yes. Record v0.3.333+ (use 0.3.363)
   in phase 10 and schedule it right after phase 01, before scroll slice 02.*
2. **Which foreground-dismissal implementation survives: `2030b29` (`modernize`) or
   `HintDismissalWatcher` (`a324706`)?** *`HintDismissalWatcher`. Revert `2030b29` on `modernize`,
   merge dismissal to `main`, and fix phase 03's and smoke §3's references.*
3. **Do we keep a long-lived `modernize` branch?** *No. Merge phase 00, merge `modernize` into
   `main`, and run the remaining phases as PRs to `main`, with one short feature freeze for 03+06.*
4. **Does phase 01 run before scroll slices 01 and 03?** *Yes, so scroll's pure code and the rule
   engine are born in `Core` with ubuntu-run tests, and no throwaway test project is created.*
5. **Does phase 02's state become orthogonal regions (session + chain + ambient) with per-region
   reducers, and do `Intent`/`Effect` become "closed per release, extended additively"?** *Yes.
   Amend `target-architecture.md` and phase 02 before 02 starts. Fold in scroll's `Scrolling`,
   `ScrollElement` and `ScrollDrive` from day one.*
6. **Is the overlay display model layered (session layer + ambient layers), with the mode marker
   driven only by the session layer?** *Yes. Amend phase 06. Ambient windows never carry the base
   title.*
7. **Do the low-level hooks move to a dedicated input thread, with `SuppressKey` replaced by a pure
   `CaptureDecision` over a published `InputCapture` snapshot?** *Yes. Amend the threading model and
   phase 03. Add cookie-tagged injection, and never filter all injected events (Talon).*
8. **Do we add a WatchHub (host) and a UiaWorker (Interop.Uia) to the target architecture, and ban
   UIA calls on the UI thread?** *Yes. Both land in phase 03, and every watcher-bearing feature (#44,
   #72, #81, scroll dismissal, diagnostics) builds on them.*
9. **Do `HintSource`s emit successive snapshots with RuntimeId-keyed ElementRefs and a label-carry
   stage?** *Yes. Amend phase 04 before the phase-05 suite freezes the seam.*
10. **Do phases 04 and 07 adopt `docs/scroll-regions/window-rules.md` as the rule model, retiring
    `ScanPolicy` as a separate concept?** *Yes. Slice 03 builds the matcher and resolver in Core;
    phase 04 consumes it, and phase 07 only adds files + watcher + migration.*
11. **Are chained hinting (#72), auto-hint (#44) and the taskbar overlay (#81) barred from building
    on legacy `main`?** *Yes. They are watcher-heavy, and building them on `HintController` doubles
    the porting cost. Prototypes stay on prototype branches until phase 03 lands.*
12. **Does phase 05 split into 05a (fixture + characterization, right after 01/10) and 05b (seam
    contract, after 04), and does the fixture gain WebView2, scroll panes and menus?** *Yes.*
13. **Do we make intents and a `StateSnapshot` plain serializable data now, deferring the G2
    external-channel decision?** *Yes. Small now, expensive later. Also amend #33's resume trigger
    from "Talon ships RPC" to "keystrokes demonstrably hurt / a G1/G2 use case is chosen", per
    #93.*
14. **Does phase 08 keep an "Open rules folder" link, and is the grid removed only after phase 07's
    migration?** *Yes. Also ship a JSON Schema for rule files (O5).*
15. **What is the UIAccess test lane?** *A signed Debug-Beta MSI installed on a bare-metal machine
    (VM snapshot as secondary). Smoke §10 gains scroll, dismissal and taskbar rows and becomes part
    of the done criteria for phase 03 and scroll slice 01.*
16. **WiX: stay on v6 or move to v7 under its enforced OSMF EULA?** *Stay on v6 until a v7-only
    feature is needed. Record the licensing position in #35.*
17. **Should the three local-only branches be pushed today?** *Yes. Specs cite their hashes.*
18. **Do auto-hint surfaces (#47 `SurfaceRule`) and command-surface profiles (#78) become sections
    of the one window-rule language rather than parallel catalogs, and does app-owned state (#50
    `surfaceAvailability`) live outside `preferences.json`?** *Yes to both. Settle this before #44
    ticket D is written.*
