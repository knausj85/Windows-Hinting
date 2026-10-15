# Research: challenging the modernize plan before execution (merged review)

**Date:** 2026-10-15. **Scope:** the spec in `docs/architecture/` on `modernize` (`dc4c219`): README,
`target-architecture.md`, `smoke-test.md`, phases 00–11, `CONTEXT.md`, and
`research/winui-preferences-migration.md`. It is measured against everything since the spec merged on
2026-08-07:

- the scroll-regions spec (`docs/scroll-regions/` on `spec/scroll-regions` `7c40f4c`, PR #105);
- the wayfinder maps #44, #51, #54/#62, #72, #81, #94 and #103;
- the unmerged and local-only branches;
- current `main` (`e331d0d`).

**Provenance.** Two skeptical reviews were written independently on the same day and are merged
here. The first is `1427a38` on this branch. The second is `2a7078a` on
`research/modernize-challenge-2026-10-15`. Each original stays in git history and carries more
citations than this merge. Where they disagreed, this document says so and names the open choice.
Every finding cites a file, commit or issue. External facts (NuGet, GitHub, the .NET release index,
Microsoft Learn) were re-checked on 2026-10-15.

**Tags:**
- **[change-now]**: amend the spec before phases 01–04/06 start, because those phases would bake the
  wrong shape in.
- **[change-later]**: real, but it can wait for the phase that touches it.
- **[keep]**: challenged, and still right.
- **[opportunity]**: unplanned, and newly cheap.

---

## Verdict

1. **The core holds** [keep]. Keep the pure `Transition`, one-shot effects, derived
   `DesiredConditions`, the scan-token guard, the opaque `ElementRef` seam, the Core/Interop/host
   split and the testing tiers. Scroll (#63) and auto-hint (#46) both fitted in by addition.
2. **Execution has not started.** No phase code is on `modernize`. Phase 00 has sat in PR #43 since
   2026-08-07. The liability is a spec that is drifting, not branch code that is drifting.
3. **Phase 02's state shape does not hold.** Chained hinting (#72), the persistent taskbar overlay
   (#81), auto-hint (#44) and scroll (#62) need state that outlives one scan, element identity in
   Core, successive results per session, and several overlay layers. Built as written, phase 02
   bakes in "one scan, one hint list, one mode".
4. **Threading is the largest unexamined risk.** The low-level keyboard hook, the engine, overlay
   painting and activation COM calls all share the UI thread. Seven features each bring their own
   watcher or UIA thread.
5. **Phases 04 and 07 are superseded** by `docs/scroll-regions/window-rules.md`.
6. **The phase-10 gate lifted on 2026-09-03.** CsWin32 v0.3.333 contains PR #1746.
7. **An external contract is unprotected.** The overlay title `… [Active]` is what Talon matches.
   The spec says "External surfaces: None".
8. **Recommendation:** go trunk-based on `main` and keep the phase content. Order:
   - reconcile the diverged and local-only commits;
   - then 00 → 01 → 10;
   - land the scroll leaves directly in Core/Interop.Uia;
   - **amend phase 02 before anyone starts it**;
   - then 03 as the cutover, with a new 03b (UiaWorker + WatchHub).

   Don't hand phases 02–04/07 to an implementing model unamended.

---

## 1. Is each decision still sound?

| Decision / phase | Status | Evidence |
|---|---|---|
| Core/Interop.Uia/host split; neutral-TFM Core (#31/#32/#36); phase 01 | **[keep], more urgent** | Every new feature has a large pure part: the scroll Gate/Suppress/Dedup/Number stages (design.md §3.2), the window-rule matcher and precedence (window-rules.md §2–3), RuntimeId reconcile (#74), and taskbar sender filter + dedup (#86). `HintController.cs` grew +1662 lines on `prototype/chained-hinting` (`6e93282`). |
| Pure engine, intent funnel, one-shot effects, derived conditions (#31/#38) | **[keep]** | Scroll adds a `Scrolling` mode, seven intents, one effect (`ScrollElement`) and one condition (`ScrollDrive`). The velocity loop stays in the actuator. Auto-hint adds a source (`AutoSurface`) and an intent (`ShowSurfaceHints`). Both are additive. |
| "Effects are **exactly five**" | **[change-now] wording** | Scroll's `ScrollElement` is the sixth. The principle (one-shots only) holds; the count will mislead an implementer who writes a test for it. It should read "closed per release, extended additively". |
| Modes `Inactive/Scanning/Active` (phase 02) | **[change-now]** | See §2.1. |
| Scan token: `ScanCompleted` once per token | **[change-now]** | Progressive reconcile (#74, `b53707f`) delivers many results per session: 150 ms polls, quiet after 4, cap 3 s. Menu settle polls up to 8× (#77). Start-menu redraw rescans on ScrollPattern changes (#49). The *session* token must be separate from the *scan generation*. |
| Labels assigned as a pure pipeline stage (phase 04) | **[change-now]** | #74: labels must stay stable per RuntimeId across re-scans, so the taskbar numbers must not shuffle. Labels become engine state keyed by identity, or a label-carry stage that takes the previous assignment. Scroll numbering (reading order, ≤9, per scan) is a different model. |
| Opaque `ElementRef`; ref-table lifetime "per scan generation" (phases 03, 10) | **[change-now]** | Reconcile needs COM-free identity in Core. Bounds-keyed identity failed in #74 (jitter caused dupes, collisions caused misses). Lifetime-per-generation conflicts with reconcile, with scroll (refs held for the whole mode, design.md §3.3) and with the persistent overlay. Scroll regions can have no ref (`Region.ElementRef?`), and G4 OCR targets (#100) are points. |
| `ScanPolicy` per (source × first-matching rule) (phases 04, 07; #39) | **Superseded** | See §2.5. |
| Rule matching is pure Core (#39) | **[change-now] amend** | `url` is a lazy UIA read (10–50 ms) that happens only after cheap fields match (window-rules.md §2.1). `appId` needs a shell call. Core needs a `WindowFacts` port with lazy fields. That is a fake, not a mock, so the no-mocks rule survives. |
| External surface: **None** (#33) | **[change-now] factually wrong** | `OverlayForm.cs:21-22,83` sets the title to `Windows Hinting Overlay [Active]`. Talon's `windows_hinting` scopes on `"[Active]" in win.title`. `CONTEXT.md` now defines the **Mode marker**. The smoke test has no title check. On `main`, the auto-hide timer is a threading timer precisely because a WinForms timer's HWND confused Talon (`HintController.cs:39`): the same contract, found by accident. |
| Delete the pipe (#33, phase 00) | **[keep]** | #93: the REPL pipe is a NO-GO, talon-rpc is LATER, and the Talon → app direction always needs an app-hosted listener. #33's resume trigger ("Talon ships RPC") is the wrong trigger. It should be "a G1/G2 use case is chosen". |
| Host serializes intents on the UI thread; the hook reads `SuppressKey` synchronously; "the host owns all threading" | **[change-now]** | See §2.4. |
| WinForms overlay, one form per screen, composer/presenter (#34, phase 06) | **[keep]; display model needs layers** | See §2.3. |
| WinForms preferences (#40) | **[keep]** | Re-confirmed by `research/winui-preferences-migration.md` (#104). WPF `ThemeMode` is still experimental (WPF0001) in .NET 10. |
| No rules editor UI (#40, phase 08 §4) | **[keep], plus diagnostics** | Keep the two read-only tray diagnostics from window-rules.md §5 (*Copy window identity*, *Explain window rules*). **Open question:** review 1 also wants an "Open rules folder" link and a JSON Schema; review 2 would keep #40 strict. On `main` the bridge keeps the rules grid until phase 07 (window-rules.md §4.2), so remove the grid only after 07's migration. |
| CsWin32 gate (#32, phase 10) | **Gate lifted** | v0.3.333 (2026-09-03) contains PR #1746; the latest is v0.3.363 (2026-10-14); `main` pins 0.3.275. A build that proves UIA generation has not been attempted. |
| Testing tiers, fixture app (#36, phase 05) | **[keep]; fixture too narrow** | The fixture has no scroll regions, menus/flyouts, Chromium content or lazy children. Every recent bug lived there (#59, #74, #77). See §4.5. |
| Boundary logging of intents in (phase 09) | **[change-later]** | Intents include every captured key, and in scroll mode that is *all* keys. The scroll spec forbids logging full URLs. A redaction rule is needed, or the opt-in log becomes a keystroke log. |
| `net10.0` | **[keep]** | .NET 10 LTS is supported until 2028-11-14, and .NET 11 is an STS release. |
| xUnit v3 | **[change-now], one line** | The `xunit.v3` line is now 4.0.2 (2026-10-09): MTP v2 by default, MTP v1 dropped. Pin 4.x in phase 01. |
| NetSparkle + WiX | **[change-later]** | The installer is on WiX v6.0.2. WiX v7 enforces the OSMF EULA on every command. Stay on v6 deliberately, and record the position in #35. |

---

## 2. New requirements and the seams they need

| Requirement (source) | Seam today? | Section |
|---|---|---|
| Session state that outlives a scan or an activation: chaining, menu-aware chaining (#72/#73/#77) | No | §2.1 |
| Successive snapshots per session; progressive reconcile; stable labels (#74, #76, #49, #81) | No | §2.2 |
| Persistent, always-on overlay layer (#81) | No | §2.3 |
| Long-lived UIA/WinEvent subscriptions (#45/#46, #77, #84/#86, design.md §7, #49, window-rules.md §5) | No | §2.4 |
| Keyboard hook off the busy thread | No | §2.4 |
| Per-app rule layer with sections (#62 E/H/I/J/K/L) | Partly | §2.5 |
| Four overlapping per-app concepts (#47, #78, #72, #67) | No owner | §2.6 |
| Scroll mode (#62) | Yes, by addition | Fold it into phase 02 from day one |
| Cursor hide as a desktop-wide side effect, with crash recovery (design.md §6.3); `timeBeginPeriod` | No | A `CursorHidden` condition plus a global side-effects registry restored on every exit path; scope `timeBeginPeriod` to holds only |
| Click dismissal parity (`a324706`) vs `ForegroundWindowHookService` (`2030b29`) | Conflicting | §3 |
| Mode marker as an external contract | No | Put `ModeMarker` in `DesiredConditions`, unit-tested as a string in Core; add a smoke step; no other window of ours may carry the base title |
| DPI / multi-monitor: cross-monitor DPI residual (#86), clamped badges (#65), secondary-monitor surfaces (#44) | Partly | Specify the coordinate space at the seam (UIA physical px → Core `Bounds` → per-screen form); add mixed-DPI smoke rows |
| Outbound state, select-by-name, label schemes (#94: F1/G2, G1, F6) | Partly | Filter as a value type (label prefix now, name text later); label scheme as policy (`Labels.cs` A..Z,AA is not prefix-free); serializable intents + `StateSnapshot` |
| Non-UIA targets (G4 OCR/CV, #100) | No | A polymorphic `ElementRef`: `Uia(identity)` or `Point/Rect` |
| Taskbar hint source vs persistent taskbar overlay (#81) | Product question | Decide before phase 02 freezes the source set |

### 2.1 Engine v2: one session plus ambient regions [change-now]

Replace the flat mode enum with orthogonal regions:

```
State = {
  Session: None | Scanning(source, sessionId, gen) | Hinting(...) | Scrolling(...),
  Chain:   Off | On(source)            // what Hinting does after an activation
  Ambient: { TaskbarBadges: Off|On(snapshotToken), AutoHint: Off|Armed }
  Options
}
Transition       = per-region reducers composed in one place
DesiredConditions = union of per-region slices (incl. ModeMarker, InputCapture, Layers)
```

- **At most one interactive session.** A `SessionId` survives activation when chaining, so chaining
  is `Hinting --activate--> Hinting(rescan pending)`, not `Active → Inactive → Scanning`. The
  prototype needed a `_chainActive` guard across the "commit-gap Inactive" (#77, `55f50f6`).
- **Ambient regions** never take keyboard capture unless the session does.
- **Interaction rules to specify:**
  - the hotkey pressed over an auto-hint session;
  - a session started while the persistent layer is visible;
  - which layer owns the digits.

Each feature adds a region reducer and a condition slice instead of editing one central `switch`.

### 2.2 Snapshots over time, identity and labels [change-now]

- `HintSource` yields **successive snapshots** for one session, as `ScanProgress(token, snapshot,
  isFinal)` or a `HintsReconciled(sessionId, generation, items)` intent. Settle and stop policy
  (150 ms / 4 polls / 3 s, from #74) is **adapter** behaviour, parameterized by a `settle` rule
  section.
- `ElementRef` gains a COM-free **identity key**: RuntimeId bytes, with a bounds fallback only where
  the RuntimeId is empty (#78 prototype note). It also gains a point/rect variant.
- **Labels:** the engine keeps `label ↔ identity`. Survivors keep their labels, and new identities
  get fresh ones. This replaces label assignment as a per-scan pure stage for click; scroll keeps
  per-scan numbering.
- **Ref-table lifetime.** Review 1 retires a generation when a newer snapshot for the same source
  supersedes it. Review 2 ties the lifetime to the session or layer. Either way, phase 10's DoD
  ("refs must not leak COM past their scan generation") must be rewritten. The engine-v2 ticket
  decides which.
- PR #87 revives `FileExplorerCustomStrategy` with **multiple roots**, so `HintSource` must accept
  multi-root resolution.

### 2.3 Layered display model [change-now in the type, change-later in phase 06]

The overlay slice of `DesiredConditions` becomes `Layers[]`, each with a z-rank:

- click hints (session);
- scroll badges and the active outline (session);
- persistent taskbar/tray numbers (ambient).

The composer draws them all, and the presenter is unchanged. The **mode marker is driven only by the
session**. If ambient numbers live on separate windows (so the full-screen forms can stay hidden),
those windows carry a different title. They must sit above the topmost taskbar (a real fix in the
scroll prototype, #61). "Redraw only on state change" still holds: ambient updates are state changes
that arrive as intents, not timers.

### 2.4 Threading: input thread, UiaWorker, WatchHub [change-now]

**The keyboard hook.** A `WH_KEYBOARD_LL` callback runs on the installing thread, which on `main` is
the STA UI thread (`NativeInterop/KeyboardHook.cs`, installed from `HintController.OnModeChanged`).
After this effort, that thread would also:

- paint N per-monitor bitmaps, plus scroll and ambient layers;
- run activation UIA calls (already true on `main`: `OnSelectionCommitted` → `_overlay.BeginInvoke`);
- host settings and the log viewer.

Scroll mode captures every key. Windows silently removes a hook that exceeds
`LowLevelHooksTimeout` (capped at 1000 ms since Windows 10 1709), and the app is never told.

Recommendation:

- Install the LL keyboard and mouse hooks on a **dedicated input thread**.
- Phase 02 publishes an `InputCapture` snapshot (mode, pass-through keys, eaten `HJKL` key-ups).
- The hook calls a pure Core `CaptureDecision(snapshot, key) → Suppress|Pass` and posts the intent.

`SuppressKey` then stops being an effect: it becomes a pure function over a condition. Name the race
where a key arrives after a toggle but before the snapshot is republished. It already exists on
`main`, so it is not a regression.

**Injection hygiene.** Tag our own `SendInput` with a `dwExtraInfo` cookie and skip only tagged
events. **Never drop all `LLKHF_INJECTED`/`LLMHF_INJECTED` events**, because Talon's keys and clicks
are injected. Design.md §7 currently skips "injected events", so a Talon click would not dismiss
scroll mode. A shared `InputInjector` serves the scroll actuator, chained hover and the mouse-click
activator (Neru lesson 6, `research/neru-architecture`).

**UiaWorker** (in `Interop.Uia`). Today:

- scans run on pool MTA threads, and abandoned timed-out scans keep running
  (`UIAutomationService.FindClickableElementsAsync`);
- activation calls MTA-obtained elements from the STA;
- chained creates a second `CUIAutomation`;
- the taskbar probe has its own MTA thread;
- the scroll actuator has its own worker (design.md §8).

#77 recorded cross-instance cache-request HRESULT errors, and #81 needs MTA, window-less
subscriptions. One owner should hold:

- the `IUIAutomation` instance;
- the ElementRef table;
- UIA event registrations;
- activator and actuator COM calls, with verify waits off the UI thread.

Ban UIA calls on the UI thread. Amend "the host owns all threading" to "the host or its named
workers".

**WatchHub** (host). Watchers that are built or decided:

| Consumer | Signals | Lifetime | Where |
|---|---|---|---|
| Click/scroll dismissal | LL mouse down, `EVENT_SYSTEM_FOREGROUND`, target `LOCATIONCHANGE` | per session | `HintDismissalWatcher` `a324706` (local); design.md §7 |
| Foreground auto-hide (competing) | `EVENT_SYSTEM_FOREGROUND` | always | `2030b29` on `modernize` |
| Rule diagnostics: last active window | `EVENT_SYSTEM_FOREGROUND` | always | window-rules.md §5 |
| Auto-hint SurfaceWatch | menu/popup WinEvents | always, when enabled | #44/#45/#46 |
| Chained / menu-aware | UIA `MenuOpened/Closed`, `StructureChanged`, settle polls | per chain | `55f50f6`, `b6976b0` |
| Taskbar overlay | UIA `StructureChanged` + container `BoundingRectangle`, MSAA `REORDER`/`STATECHANGE`, `RegNotifyChangeKeyValue`, display change + 400–600 ms settle | always, when enabled | `1b533bf`; #81 |
| Start-menu redraw | ScrollPattern changes | per auto-hint session | #49 |

Each re-solves the same problems:

- out-of-context hook threads;
- self-process and injected filtering;
- sender identity (#81);
- RuntimeId dedup;
- debounce;
- shutdown ordering.

One hub provides:

- a dedicated hook thread;
- process-wide WinEvent hooks where needed (#84: thread-scoped hooks missed explorer events);
- ref-counted subscriptions driven by `DesiredConditions` slices (`DismissalWatch`, `SurfaceWatch`,
  `TaskbarWatch`, `LastActiveWindowTracking`);
- output as coalesced intents (`ForegroundChanged`, `TargetMoved`, `SurfaceAppeared`,
  `TaskbarDirty`).

It answers #46's open question for ticket D: SurfaceWatch and the foreground adapter are both
subscribers. It also removes phase 03's accepted "foreground watch only after scan" delta.

### 2.5 Window rules supersede phases 04 and 07 [change-now]

window-rules.md (E #67 + amendments, H #88, I #89, J #90, K #91, L #92) decided:

- **sections** with per-section most-specific-wins and no merging within a section;
- app definitions (the only OR), `not` exclusions, `url`/`appId` matchers and a URL tie-break;
- one string syntax; unknown sections kept;
- files shaped as `{apps, rules}`, with bare arrays still accepted;
- precedence: matcher-field count, URL tie-break, user over built-in, then file order.

Phase 07's "lifting today's merge semantics" means first-match-wins: the opposite of this.

Rules also change **what UIA is asked for**: control-type deltas, `expectedRegions` selectors and
`region: parent` (I #89). That is harvest-side policy in Interop, not only post-scan stages.

Consequences:

- Phases 04 and 07 consume this spec. `ScanPolicy` maps to the `root` + `click` sections, and
  `ScrollDetectionPolicy` maps to `scrollDetection`.
- Slice 03 builds the matcher and resolver in Core, so phase 04 ports nothing.
- Phase 07 adds the files, the watcher and the v0 migration of the bridged `WindowRules`/`WindowApps`.

**[change-later] performance.** Built-in `Slack` and `Gmail` have URL-only alternatives. An
alternative with no cheap fields triggers the 10–50 ms URL read on **every Chromium window**: Edge,
VS Code, Teams and Slack desktop. Measure it in slice 04. The likely fix is to resolve only the
sections the consumer needs, and to skip the URL read when no URL-bearing rule could change them.

### 2.6 One per-app vocabulary [change-now, before #44 ticket D and #78]

There are four overlapping notions:

- window-rule sections (#67);
- the `SurfaceRule` catalog (#47);
- the command-surface profile (#78, Office ribbon/QAT);
- the settle profile (#72, "confirmed-required").

Recommendation: make `menu` (#78) and `settle` (#72) **sections** of the window rule, as #67
anticipates. **Open question on `SurfaceRule`:**

- Review 1 makes it a section too.
- Review 2 keeps it as an app-locked catalog (its reasons in #47 still hold) that reuses the #67
  matcher and section value types.

Separately, #50 has the app write `surfaceAvailability` into `preferences.json`. That breaks "one
propagation path" (#39) and the "file is user-authored" assumption. App-owned state belongs in an
unwatched `state.json`. Write the vocabulary into `CONTEXT.md`.

---

## 3. Sequencing and branch strategy

### 3.1 What is true on 2026-10-15

| Assumption (README "Development branch") | Reality |
|---|---|
| `modernize` = `main` + docs; `main` stays quiet | `modernize` carries `2030b29` (`ForegroundWindowHookService`, via the `refactor` merge `f7e7320`). `main` carries `c36db1b` (#10 Explorer active tab, #87), which `modernize` lacks. Five scroll slices are queued for `main` (PR #105). |
| Phases run back to back | Only phase 00 has a PR (#43, open since 2026-08-07). Phases 01–11 have not started. |
| Phase 10 waits on an external gate | The gate lifted 2026-09-03. |
| "Read the current code as the behavioral spec" (phase 02) | There are two diverged `HintController`s, and dismissal, reading order, Explorer tab and scroll glue all land there first. |
| Banked work is safe | `feature/hint-dismissal-parity` (`a324706`), `fix/taskbar-hint-reading-order` (`fdec648`) and `prototype/chained-hinting` (`28698ff`/`6e93282`) are **local only**. The specs cite their hashes. |

`git merge-tree` shows `HintController.cs` conflicting for dismissal × chained, modernize ×
dismissal and modernize × chained. `NativeMethods.txt` conflicts in three more pairs. Smoke §3
("foreground-change dismissal") fails on a `main` build, because `main` has no foreground dismissal.

### 3.2 What ports cheaply from `main`

- **Leaves port almost free.** These include the scroll actuator, hold timer, cursor hide, region
  harvest and pure stages; the rule matcher and URL parser; `ReadingOrder`; and the dismissal hook
  code. The scroll spec already writes them as standalone classes.
- **Glue is thrown away.** This means `ScrollController`, `HintMode.Scrolling`, the scroll branch in
  `HintInputHandler` and `OverlayForm.SetScrollTargets`. The scroll intent tables are already in
  engine vocabulary, so the cost of re-deriving it is low.
- **Thread- and subscription-heavy features are costly on `main`.** Chained, the taskbar overlay
  and auto-hint would each invent their own watcher and UIA thread. They should not be built on
  legacy `main`; prototypes stay on prototype branches until 03/03b land.

### 3.3 Recommended order

1. **Reconcile the forks.** Choose one dismissal watcher. `HintDismissalWatcher` is a superset
   (click + foreground + move/resize), so either revert `2030b29` or merge it and supersede it.
   Bring `c36db1b` to `modernize` or retire the branch. Push the three local-only branches. Merge
   dismissal + reading order to `main`.
2. **Merge phase 00 (#43) and retire the long-lived `modernize` branch.** Merge its docs into `main`
   and run the remaining phases as PRs to `main`. Each phase is "no behavior change + smoke", so
   trunk-based is safe. Keep the beta-channel `workflow_dispatch` for risky phases.
3. **Phase 01** before scroll slices 01/03, so scroll's pure code and the rule engine are born in
   Core with ubuntu-run tests. Otherwise slice 01 creates a throwaway `Windows-Hinting.Tests`
   project.
4. **Phase 10** right after 01, before scroll slice 02 adds a second harvester. It removes
   `COMReference` and the VS 18 MSBuild tax from every later agent session, and replaces about 30
   manual `ReleaseComObject` sites. If generation is immature, revert one PR.
5. **Phase 05a (new):** fixture + characterization smoke against today's scanner, as the regression
   net for 02–04.
6. **Scroll slices 03 → 01 → 02** on `main`, with leaves in Core/Interop.Uia and glue thin and
   labelled throwaway.
7. **Phase 02 (engine v2)**, amended per §2.1–2.3, with scroll, layers and the capture snapshot from
   day one.
8. **Phase 03 + 06 under a short feature freeze**, with **03b UiaWorker + WatchHub** immediately
   after. Phase 03 absorbs the click, scroll, dismissal and chain glue.
9. **Phase 04** (rewritten), then **05b** (seam contract), then scroll slices 04–05 on the new seams.
10. Then chained (#72), auto-hint (#44) and the taskbar overlay (#81) **on the modernized seams
    only**.
11. 07 → 08, with 09 floating. Move 07 earlier if the rules bridge accumulates user data. Phase 11
    stays optional; its value rises with soft outlines and badges over arbitrary content.

---

## 4. Risks the plan underweights

1. **UIAccess is the shipping configuration and is never exercised in development.** Debug builds use
   `app.debug.manifest`. Under UIAccess, z-order above Start, hooks over elevated windows, and
   `SendInput`/`SetCursorPos` into elevated targets all change, and the scroll spec's UIPI limit
   (design.md §4.4) differs per build flavor. Add a UIAccess lane: a signed Debug-Beta MSI on bare
   metal. Make smoke §10, extended with scroll, dismissal and taskbar rows, part of done for phase 03
   and scroll slice 01. Document the portable-mode UIPI gap.
2. **Hook timeouts are silent.** Add a hook-latency histogram in boundary logging. Add a liveness
   probe that reinstalls the hook when a capture mode is entered and the hook is dead.
3. **COM apartments and lifetime.** Without the UiaWorker there will be four COM owners with four
   release policies, right before phase 10 rewrites lifetime to ComWrappers.
4. **VM vs bare metal.** The parked hidden-cursor wheel claim is VM-tested only (G #69). CI runners
   are VMs. Never assert wheel scrolling in the smoke suite. Keep timing constants as named,
   policy-overridable values.
5. **A WinForms-only fixture proves the seam, not the behavior.** Add a WebView2 page (the Chromium
   provider, `RootWebArea` URL, `className` tokens), scroll panes, nested trees, a context menu, a
   slow-settle panel and an elevated-instance switch.
6. **Process risks:**
   - two spec trees disagree (three modes and five effects vs four and six);
   - local-only branches;
   - a ten-week-old PR;
   - the decision table stops at #40, while #45–#50, #63–#69, #74, #77, #86 and #88–#93 constrain
     the spec but are reachable only through map bodies.
7. **Privacy:** the `url` read, the cursor-hidden marker and intent logging are new on-disk and log
   data. No spec covers privacy as a whole.

---

## 5. Handoff readiness: what an implementing model would stumble on

1. **README:** PRs target `modernize`, while PR #105 says `main`. One rule is needed.
2. **Phase 00:** done but unmerged, so a fresh agent would redo it.
3. **Phase 01:**
   - It doesn't say whether new Core code (rules, scroll stages) may land before 02.
   - `IUIAutomationService.cs` gained members in `2030b29`.
   - `HintStateManager` lives in `Services/HintState.cs`.
4. **Phase 02:**
   - "exactly five effects";
   - no `Scrolling`, no session/generation split, no `ElementRef` identity;
   - label location ambiguous;
   - the hook-thread question unanswered;
   - no mode marker;
   - two diverged "current code" baselines.
5. **Phase 03:**
   - no dismissal condition beyond foreground;
   - ref-table lifetime undefined;
   - no instructions for porting scroll and rule code that landed on `main`;
   - `HintMode.Selecting` is still referenced by `TrayIconManager.cs:195`, so both ends need
     deleting.
6. **Phase 04:**
   - "grow `WindowRule` into `ScanPolicy`" contradicts window-rules.md;
   - no harvest-side policy;
   - no `ReadingOrder` stage (`fdec648`);
   - still lists #10 as future work, but `c36db1b` fixed it.
7. **Phase 05:** the fixture lacks the content classes that matter.
8. **Phase 07:**
   - file shape and precedence;
   - v0 migration of the bridged sections;
   - reserving `surfaces`/`surfaceAvailability` (#47/#50).
9. **Phase 08:**
   - keep the diagnostics tray items;
   - list the bridge-era rules grid it removes;
   - the #103 spike follows.
10. **Phase 09:** intent-log redaction (keys, URLs).
11. **Phase 10:** the gate text is stale, and the lifetime DoD depends on §2.2.
12. **Smoke test:** add the title marker, scroll mode, cursor restore after a kill, chained
    re-hint, menu open and mixed-DPI rows.
13. **Shared code to unify:** `ElementDeduplicator` and the scroll Dedup stage should be one Core
    function. Reading order and label assignment should be one ordering stage.

---

## 6. Opportunities (ranked by value ÷ cost)

| Rank | Opportunity | Cost | Notes |
|---|---|---|---|
| O1 | WatchHub | M | §2.4. It consolidates `HintDismissalWatcher`. |
| O2 | UiaWorker (one `IUIAutomation`, ref table, events, activator/actuator COM) | M | §2.4; pairs with phase 10. |
| O3 | Serializable intents + `StateSnapshot` in Core | S now, L later | Keeps G1/G2/G3 (#94) and a talon-rpc adapter purely additive, and gives phase 09 structured logs for free. |
| O4 | Input thread + capture snapshot + cookie-tagged `InputInjector` | S–M | §2.4. |
| O5 | Fixture expansion (WebView2, scroll panes, menus, slow settle, elevated switch) | S–M | §4.5. |
| O6 | Label-carry stage (RuntimeId → label) | S | §2.2. |
| O7 | JSON Schema for rule files | S | Generated from Core types. It offsets "no rules UI" (open question in §1). |
| O8 | Reusable probe library on UiaWorker/WatchHub | M | Every map since #44 hand-built a probe (about 5,000 lines in total). The #51 forked tool could reference `Interop.Uia` after phase 10. |
| O9 | A second, stable title token for the source (`[Active] [Taskbar]`) | S | Design.md §6.4 reserves trailing tokens. |
| O10 | Global side-effects registry (cursor, timer resolution, display affinity) | S | §2 table. |
| O11 | NativeAOT trial | M | A follow-on, enabled by phase 10. |

---

## 7. Proposed amendments and tickets (prioritized)

**P0: before any engine work is handed off** (each is a decision ticket on map #26):

1. **Branch strategy and fork reconciliation:**
   - trunk-based on `main`, or not;
   - which dismissal watcher survives (`2030b29` vs `a324706`);
   - bring over `c36db1b`, `fdec648` and `a324706`;
   - push the local-only branches;
   - retarget PRs #43 and #105;
   - phase order 00 → 01 → 10, and whether 01 precedes scroll slices 01/03;
   - bar #44/#72/#81 from legacy `main`.

   Covers §3.
2. **Threading model:**
   - a dedicated input thread and `CaptureDecision` over an `InputCapture` snapshot (replacing the
     `SuppressKey` effect);
   - cookie-tagged injection, never filtering all injected input;
   - the UiaWorker owning COM, refs and UIA events, with UIA calls banned on the UI thread;
   - the WatchHub as the single owner of WinEvent/LL-mouse subscriptions;
   - amending "the host owns all threading".

   Covers §2.4.
3. **Engine v2 amendment** to `target-architecture.md` + phase 02:
   - session + ambient regions;
   - `SessionId` vs scan generation;
   - successive snapshots / `HintsReconciled`;
   - the `ElementRef` identity key, point variant and ref-table lifetime unit;
   - labels as engine state;
   - the layered display slice and mode marker in `DesiredConditions`;
   - `Scrolling`/`ScrollElement`/`ScrollDrive` folded in;
   - "one-shots only" wording;
   - serializable intents + snapshot.

   Covers §2.1–2.3. It depends on 2, because `SuppressKey`'s fate changes the effect set.

**P1: before phases 03/04/07**

4. A new phase 03b, UiaWorker + WatchHub, with unified dismissal as its first consumer.
5. Rewrite phase 04 around window-rules.md: `WindowFacts` port, harvest policy, `ScrollRegionSource`,
   multi-root, `ReadingOrder`; drop "#10 future".
6. Rewrite phase 07: file shape, precedence, v0 migration, reserved blocks.
7. Unify the per-app vocabulary in `CONTEXT.md` (§2.6), including `state.json` for app-owned state.
8. Phase 10: record the lifted gate (≥ 0.3.333, use 0.3.363) and reorder it; confirm with a build.
9. Phase 01: pin xUnit 4.x + MTP v2.

**P2: within the phases that touch them**

10. Phase 05 split; fixture expansion.
11. Phase 06: layered display, title marker ownership, z-order above the taskbar.
12. Phase 08: keep the diagnostics; settle the "Open rules folder" link and JSON Schema question.
13. Phase 09: redaction.
14. Smoke test additions (§5.12) and a UIAccess lane (§4.1).
15. Extend the decision table to #45–#50, #63–#69, #74, #77, #86, #88–#93.
16. Amend #33's resume trigger. Record the WiX v6/v7 position in #35.

**Product questions**

17. Does the persistent taskbar overlay (#81) replace the `Taskbar` hint source?
18. Scope the build-spec maps for #44, #72 and #81 against the amended phase 02 and 03b.

---

## What could not be determined

- Whether CsWin32 v0.3.333+ fully covers UIA generation. Only release notes were checked; there was
  no build.
- Whether `2030b29` was left off `main` on purpose.
- The contents of the local-only branches beyond diff stats.
- Whether UIA work on the UI thread actually caused hook timeouts in the prototypes. This is
  inferred from design.md §4.1 p.3, not measured.
