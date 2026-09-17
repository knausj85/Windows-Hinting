# Research: readiness detection for progressive/virtualized UIA trees (settle alternatives)

**Ticket:** [#76](https://github.com/knausj85/Windows-Hinting/issues/76) (Wayfinder research, AFK).
**Parent map:** [#72](https://github.com/knausj85/Windows-Hinting/issues/72). Feeds Prototype B ([#74](https://github.com/knausj85/Windows-Hinting/issues/74))'s settle decision.

**Question (verbatim):** For chaining (after a click, re-scan the foreground app and re-show keyboard
hints), how do comparable tools decide **"the accessibility tree is ready to act on"** when the app UI
builds **progressively / lazily (virtualized)** — and is there a signal or strategy that avoids acting
on a *partial* tree?

**Why this ticket exists.** The B-prototype ([#73](https://github.com/knausj85/Windows-Hinting/issues/73),
`PROTOTYPE-CHAINING-FINDINGS.md` on `prototype/chained-hinting`) empirically defeated **all three**
obvious local "guess when it's done" heuristics on Windows Settings' XAML:

| Heuristic tried | Failure mode (confirmed live) |
|---|---|
| Fixed delay after click | races the settle → partial tree |
| UIA `StructureChanged` quiet-window debounce | SC stream goes **silent before** the tree finishes materializing (a fresh scan ~1–2 s later sees everything) |
| Poll-until-count-stable | a **build pause** looks identical to *done* → false-stables on a partial tree; highly timing-dependent |

All three are the same losing bet: **detect a single "finished" moment.** This document establishes,
from primary sources, that no such moment is reliably observable over UIA for WinUI/XAML app views, and
that the tools which handle this best do not try to detect one.

**Scope guardrail — this is the docs + cross-tool side.** A hands-on `AsyncContentLoaded` probe on
Settings runs in parallel on `prototype/chained-hinting`. This doc cites prior art and does **not**
re-derive it:

- **[#71](https://github.com/knausj85/Windows-Hinting/issues/71)** (`research/edge-chromium-settle`,
  `docs/architecture/research/edge-chromium-settle.md`) — found `AsyncContentLoaded` **is** the reliable
  navigation signal **for Chromium/Edge**, because Chromium's own code explicitly fires it on
  `kLoadComplete`. This ticket asks the same question for **WinUI/XAML + native**.
- **[#48](https://github.com/knausj85/Windows-Hinting/issues/48)** — `StructureChanged` = an optional
  readiness *supplement* on an already-classified root, timeout backstop mandatory; never a primary
  trigger.
- **[#49](https://github.com/knausj85/Windows-Hinting/issues/49)** — on virtualized XAML,
  `StructureChanged` is virtualization-bound (reports realized children, not visible content);
  `LayoutInvalidated` was **dead** on XAML.

---

## VERDICT (TL;DR)

1. **`AsyncContentLoaded` is NOT a dependable "content ready" signal for WinUI/XAML app views.** It is a
   **provider-opt-in** event: over both the Win32 provider API and the WinRT/WPF peer API, *the
   application (or a custom control) must explicitly call it*. The **XAML framework does not raise it**
   on in-app navigation / pane / view changes — grep of the WinUI (`microsoft-ui-xaml`) source shows the
   only callers are **test apps and sample pages**, one of which openly documents that it is *abusing*
   the event as a generic stand-in ("here isn't a custom AutomationEvent type, so use an existing event
   type"). No shipping framework control (`Frame`, `NavigationView`, `ListView`, …) raises it on a view
   change. So the Chromium/Edge result from #71 **does not generalise**: it works there only because
   Chromium hand-fires it, and virtually no WinUI/native app (Windows Settings included) does. Treat any
   `AsyncContentLoaded` from a WinUI/native app view as a **lucky bonus, never a guaranteed gate.** (Live
   confirmation on Settings is the parallel probe's job; the docs already settle the *dependability*
   question — it cannot be depended upon.)

2. **"Reconcile progressively" is the correct design.** The only comparison tool with a mature answer
   for *continuously changing* content — macOS Voice Control's **"Show numbers continuously"** — uses a
   **live, self-updating overlay**, not a one-shot settle. The others (Vimac, Vimium) get away with
   one-shot only because their environments sidestep the problem (Vimac accepts a partial tree + manual
   re-trigger; Vimium reads a synchronous in-process DOM with no lazy a11y tree). For an out-of-process
   UIA hinter over lazily-built XAML, "when is it done?" is **not reliably answerable**, so the design
   must not depend on answering it.

3. **Recommendation: progressive-reconcile as the primary mechanism, with a hardened-poll/event floor +
   hard-timeout as the scaffold, and semantic signals used opportunistically where a provider happens to
   emit them.** A **hybrid**, but with the centre of gravity moved off "detect the finished moment" and
   onto "render now, keep updating." Details in §4.

---

## 1. Is there a semantic readiness signal over UIA/IA2 for app content?

### 1.1 `AsyncContentLoaded` is provider-initiated by definition

Primary sources (Microsoft Learn, fetched 2026-09-17):

- **Win32 provider API — `UiaRaiseAsyncContentLoadedEvent`:** "**Called by a provider** to notify the
  Microsoft UI Automation core that content is being loaded asynchronously." Signature takes
  `IRawElementProviderSimple* pProvider`, an `AsyncContentLoadedState`, and a `double percentComplete`
  the **provider supplies**.
  ([UiaRaiseAsyncContentLoadedEvent](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiaraiseasynccontentloadedevent))
- **`AsyncContentLoadedState`** = `Beginning` / `Progress` / `Completed` — "the progress of asynchronous
  loading of content."
  ([AsyncContentLoadedState](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/ne-uiautomationcoreapi-asynccontentloadedstate))
  The percent-complete is a **free-form number the provider chooses**, not something UIA computes.
- **WinRT / WPF peer API — `AutomationPeer.RaiseAsyncContentLoadedEvent(AsyncContentLoadedEventArgs)`:**
  "**Called by the AutomationPeer** to raise the AsyncContentLoadedEvent event." The peer (i.e. the app's
  control) is the caller.
  ([AutomationPeer.RaiseAsyncContentLoadedEvent](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.peers.automationpeer.raiseasynccontentloadedevent?view=windowsdesktop-10.0))

The event is a **channel a provider MAY use to volunteer its own load progress**. It is not a
UIA-computed "the tree is now complete" signal, and there is no client-side API that derives one.

### 1.2 The WinUI framework does not raise it on view/pane changes

Authenticated code search of `microsoft/microsoft-ui-xaml` (via `gh search code`, 2026-09-17):

- `RaiseAsyncContentLoadedEvent` — **zero** hits in the framework.
- `AsyncContentLoaded` — hits are only the **enum/event-id plumbing** (`EnumDefs.g.h`, `UIAWindow.cpp`,
  `UIAEnums.g.h`, the IDL, the codegen model) **plus three call sites, all in test/sample code**:
  - `controls/dev/Materials/Acrylic/TestUI/AcrylicBrushPage.xaml.cs`
  - `controls/test/TestAppUtils/TestPage.cs`
  - `controls/test/MUXControlsTestApp/MainPage.xaml.cs`

  Each is `automationPeer.RaiseAutomationEvent(AutomationEvents.AsyncContentLoaded)`, and the Acrylic one
  carries the tell-tale comment: *"there isn't a custom AutomationEvent type, so use an existing event
  type — AsyncContentLoaded."* i.e. WinUI's own engineers reach for it as a **generic test event**, not
  a content-ready contract.

**Conclusion:** for a real WinUI app view (Settings, any Frame/NavigationView-based app), `AsyncContentLoaded`
fires only if that specific app hand-coded it — which is rare. It is **not** a framework-level readiness
signal. This is exactly why #71's Chromium result is Chromium-specific: Chromium *is* an app that
hand-fires it (on `kLoadComplete`), and even guarantees it across reload via `AlwaysFireUIAEvent`. WinUI
gives no such guarantee.

### 1.3 Corollary — no Microsoft client-side "content ready" API exists either

There is no UIA **client** API that reports "this lazily-populated subtree has finished building." The
client-side surface is `TreeWalker` / `FindAll` / cache requests + the event subscriptions; none of them
expose a completion state for a provider that populates incrementally. Microsoft's guidance for
lazily-populated content is aimed at **providers** (raise `StructureChanged` / `AsyncContentLoaded` as
you go), which only helps a client if the provider bothered to. This matches #48/#49: on XAML the only
readiness-ish client signal is `StructureChanged`, and it is unreliable as a completion gate (fires on
realization, goes silent mid-build).

---

## 2. How comparable "click-by-label / item-number" tools handle progressive/virtualized trees

| Tool | Platform / API (in- vs out-of-process) | Readiness strategy | Evidence |
|---|---|---|---|
| **macOS Voice Control** | Apple AX, **out-of-process** | **Progressive-reconcile (live overlay)** + coarse per-navigation refresh | One-shot `"Show numbers"` numbers the current items; **`"Show numbers continuously"`** keeps a **live overlay** that re-numbers as content changes, and the grid **auto-dismisses after an action** and re-shows on the next context. Refresh is tied to navigation/focus, not to a "settled" detector. ([Apple Support — item number & grid overlays](https://support.apple.com/guide/mac-help/mchl26854b08/mac); [Voice Control commands](https://support.apple.com/guide/mac-help/use-voice-control-commands-mh40719/mac)) |
| **Vimac** | macOS AX (AXSwift), **out-of-process** | **One-shot traversal; accept-partial + manual re-trigger** | `HintModeQueryService.perform()` is a single fire-and-forget AX traversal of menu-bar / extras / notification-center / window layers, **no timeout, no retry, no wait-for-ready, no debounce, no re-query**. `HintModeController` queries once on `activate()` and thereafter only listens for keystrokes — it never subscribes to AX notifications. If the tree wasn't ready, the user simply re-invokes hint mode. ([HintModeQueryService.swift](https://github.com/nchudleigh/vimac/blob/master/ViMac-Swift/Accessibility/HintMode/HintModeQueryService.swift), [HintModeController.swift](https://github.com/nchudleigh/vimac/blob/master/ViMac-Swift/Modes/HintModeController.swift)) |
| **Talon Voice** | Win UIA / macOS AX / OCR, **out-of-process** | **On-demand re-query per command; no persistent live model, no documented settle gate; OCR fallback** | Talon enumerates native elements when a command runs (click by name, `element.find()`, `element.parent`), re-querying rather than holding a settled snapshot; when the a11y tree is insufficient it falls back to an **OCR** pass over pixels. No published "wait until the tree is ready" mechanism. *(Lower-confidence: from Talon changelog + community wiki, not source.)* ([Talon changelog](https://talonvoice.com/dl/latest/changelog.html); [Talon wiki — accessibility](https://talon.wiki/Integrations/accessibility/)) |
| **Vimium** *(contrast)* | In-page DOM (JS), **in-process** | **One-shot scan on activation; no `MutationObserver`** | `LinkHintsMode` builds all markers once from a static descriptor list (`getLocalHints` → `hintMarkers = descriptors.map(...)`); **no `MutationObserver`, no continuous monitoring, static hint set for the session.** It can afford this because the DOM is a **synchronous, complete, in-process** tree — there is no lazy cross-process a11y snapshot and no build race. ([link_hints.js](https://github.com/philc/vimium/blob/master/content_scripts/link_hints.js)) |

**Reading of the table:**

- **Nobody waits on a semantic "content ready" signal**, because (per §1) none is dependably available
  out-of-process. The one place a semantic signal *is* used is #71's Chromium/Edge case, which is
  provider-specific.
- **The two out-of-process AX hinters that ship one-shot (Vimac, Talon) tolerate partial trees**: Vimac
  by design (re-invoke to refresh), Talon by re-querying per command and by an OCR fallback. Neither
  claims to solve "is it done."
- **The tool built for *continuously changing* content — Voice Control — chose a live, self-updating
  overlay.** That is the direct prior-art precedent for "reconcile progressively."
- **Vimium is the control case**: one-shot is *only* safe when your tree is in-process and synchronous.
  An out-of-process UIA hinter over lazy XAML is the opposite of that, so Vimium's model does **not**
  transfer — which is precisely the trap the three failed heuristics fell into (they imported Vimium's
  one-shot assumption into an environment that violates it).

---

## 3. Is "reconcile progressively" the accepted answer?

**Yes, with a caveat.** There is no formal spec that says "when is it done is unanswerable for virtualized
trees" in so many words, but the convergent evidence supports treating it as unanswerable and designing a
live overlay:

1. **The signal simply isn't there (§1).** No framework readiness event on XAML view changes; no
   client-side completion API; `StructureChanged` proven unreliable as a gate (#48/#49) and empirically
   silent-before-done (#73).
2. **The prototype proved the negative directly (#73):** every "detect the finished moment" heuristic
   failed on the *same* surface, and a fresh scan 1–2 s later saw everything — i.e. the tree *does* finish,
   there just is no reliable *event* announcing it. That is the definition of "unanswerable locally."
3. **The best-fit prior art picks a live overlay** (Voice Control "continuously"); the one-shot tools
   only survive by accepting partial + manual refresh (Vimac) or by being in-process (Vimium).

So "reconcile progressively" is the **established shape for continuously/lazily changing content**, and
the correct default here. It reframes the problem from *"scan once at the right instant"* (impossible) to
*"scan early, then keep the overlay in sync as elements appear"* (robust to the build race by
construction).

---

## 4. Recommendation for the chaining prototype

**Progressive-reconcile as the primary mechanism, wrapped in a hardened poll/event floor + hard-timeout,
with semantic signals consumed opportunistically. (Hybrid, centre of gravity on reconcile.)**

Concretely, replace "wait for settle, then scan once" with "scan now, then update":

1. **Initial scan after a short floor.** After the chained click (t0, which we injected and therefore
   know exactly), wait a small **FLOOR** (~150–250 ms, per #71) so the first batch has a chance to
   serialize, then do the first scan and **show hints immediately** — even if partial. This kills the
   "blank/partial and stuck" failure: the user sees *something* fast.
2. **Keep reconciling until quiescent-or-timeout.** Subscribe (element-scoped on the classified
   foreground root, `TreeScope.Children`/`Subtree`, off the UI thread per #48) to the **update triggers**,
   not as a settle gate but as *"re-scan and merge"* prompts:
   - `StructureChanged` `ChildAdded` / `ChildrenBulkAdded` on the root (#48),
   - a cheap **child-count poll** as backstop for the "SC goes silent mid-build" case (#73),
   - `ScrollPattern VerticalScrollPercent` where the surface scrolls (#49),
   - and, **if present**, `AsyncContentLoaded` (bonus coarse trigger — Chromium/Edge per #71, or the rare
     WinUI app that hand-fires it; never relied upon on XAML per §1).

   On each trigger, re-scan and **additively update the overlay**: add hints for newly-appeared elements,
   drop hints for vanished ones, and **keep existing hint labels stable** so a label the user is mid-typing
   doesn't move. This is the Voice-Control "continuously" model.
3. **Stop reconciling on a quiet-window OR a hard-timeout backstop.** Stop updating once triggers have
   been quiet for a short window **or** at `t0 + HARD` (~800–1200 ms, per #73's constraint and #71).
   Reconciliation makes the exact stop point **non-critical** — stopping early just means a late element
   is missed until the next user action, not a broken scan. The timeout is a resource guard, not a
   correctness gate.
4. **Cheap manual refresh as the final backstop (Vimac's lesson).** Provide a one-key "re-scan now"
   (re-run the existing `Activate` + `ScanForHints` path from #73's harness) so the rare miss is a single
   keystroke to fix, never a dead end.

**Why not the alternatives:**

- **Pure semantic-signal** — rejected: §1 shows no dependable semantic signal exists for WinUI/XAML app
  views; it only works for Chromium (#71). Building the mechanism on it would fail on Settings and most
  native/XAML apps.
- **Pure hardened-poll (one-shot with a smarter settle detector)** — rejected: #73 already hardened the
  poll three different ways and all three false-stabled on a build pause. Any single-moment detector is
  fighting a signal that is *absent by construction*, not merely noisy.
- **Progressive-reconcile** — accepted as primary: it is the only approach that is *correct-by-construction*
  under "there is no finished-moment event," and it is the shipping choice of the one comparison tool
  built for this exact problem.

**Per-app profile still applies.** FLOOR / QUIET / HARD and which update-triggers to subscribe to are
surface-dependent (Edge vs Settings vs a chatty dashboard), extending the #47 catalog / #49 redraw-signal
fields additively — same conclusion #71 reached for Edge. Reconciliation *reduces* the tuning sensitivity
(the constants stop being correctness-critical) but does not remove the per-app layer.

---

## Sources

**Primary — Microsoft Learn (fetched 2026-09-17):**

- UiaRaiseAsyncContentLoadedEvent (provider-called; provider supplies percent-complete) —
  https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiaraiseasynccontentloadedevent
- AsyncContentLoadedState enum (Beginning/Progress/Completed) —
  https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/ne-uiautomationcoreapi-asynccontentloadedstate
- AutomationPeer.RaiseAsyncContentLoadedEvent (called by the AutomationPeer, i.e. the app's control) —
  https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.peers.automationpeer.raiseasynccontentloadedevent?view=windowsdesktop-10.0

**Primary — WinUI source (`microsoft/microsoft-ui-xaml`, `gh search code`, 2026-09-17):**

- `RaiseAsyncContentLoadedEvent`: **0 framework callers**.
- `AsyncContentLoaded`: enum/event-id plumbing only, plus three **test/sample** call sites —
  `controls/dev/Materials/Acrylic/TestUI/AcrylicBrushPage.xaml.cs` (with the "use an existing event type"
  comment), `controls/test/TestAppUtils/TestPage.cs`, `controls/test/MUXControlsTestApp/MainPage.xaml.cs`.

**Primary — comparison-tool source / vendor docs (fetched 2026-09-17):**

- Vimac — `HintModeQueryService.swift` (one-shot traversal, no timeout/retry/settle) and
  `HintModeController.swift` (query once on activate, no AX-notification subscription) —
  https://github.com/nchudleigh/vimac/blob/master/ViMac-Swift/Accessibility/HintMode/HintModeQueryService.swift ,
  https://github.com/nchudleigh/vimac/blob/master/ViMac-Swift/Modes/HintModeController.swift
- Vimium — `content_scripts/link_hints.js` (one-shot scan, no MutationObserver) —
  https://github.com/philc/vimium/blob/master/content_scripts/link_hints.js
- macOS Voice Control — item-number & grid overlays, incl. "Show numbers continuously" (live overlay) —
  https://support.apple.com/guide/mac-help/mchl26854b08/mac ;
  https://support.apple.com/guide/mac-help/use-voice-control-commands-mh40719/mac
- Talon Voice *(lower-confidence: changelog + community wiki, not source)* —
  https://talonvoice.com/dl/latest/changelog.html ; https://talon.wiki/Integrations/accessibility/

**Prior art in-repo (cited, not re-derived):**

- #71 `research/edge-chromium-settle` (`docs/architecture/research/edge-chromium-settle.md`) —
  `AsyncContentLoaded` reliable *for Chromium* because Chromium hand-fires it on `kLoadComplete`.
- #48 `research/uia-structurechanged` (`docs/architecture/research/uia-structurechanged.md`) —
  `StructureChanged` = optional readiness supplement + timeout backstop, never primary.
- #49 (`prototype/auto-hint-detection`, `FINDINGS.md`) — `StructureChanged` virtualization-bound;
  `LayoutInvalidated` dead on XAML; redraw signal = `ScrollPattern VerticalScrollPercent`.
- #73 `PROTOTYPE-CHAINING-FINDINGS.md` (`prototype/chained-hinting`) — the three defeated heuristics.
