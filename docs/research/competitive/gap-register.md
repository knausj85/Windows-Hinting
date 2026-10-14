# Feature-gap register: Windows-Hinting vs comparable tools

Ticket: #100 (child of wayfinder map #94). This merges the five competitive notes into one deduplicated, ranked register with a **draft** disposition for every gap. The triage ticket #101 locks the dispositions with the user. Nothing here has been decided yet.

**Inputs.** Each note lives on its own branch. Cite a row back to its note to find the primary sources:

| Short | Note | Branch @ commit |
|---|---|---|
| **W** | `docs/research/competitive/windows.md` (#95) | `research/competitive-windows` @ `322ebd7` |
| **M** | `docs/research/competitive/macos.md` (#96) | `research/competitive-macos` @ `5f84d40` |
| **L** | `docs/research/competitive/linux.md` (#97) | `research/competitive-linux` @ `39d5c76` |
| **V** | `docs/research/competitive/voice.md` (#98) | `research/competitive-voice` @ `b063632` |
| **R** | `docs/research/competitive/reference.md` (#99) | `research/competitive-reference` @ `d4fa8a2` |

A sixth input, the Neru deep dive (#102), is kept local at the user's request. Its implementation lessons are filed in a local addendum and are **not** restated here. Where it changes a draft disposition, the row says "see local addendum" and nothing more.

**Method.**
- Every gap row in W, M, L, V and R was mapped onto one register entry. Same feature, different words → one entry, with the source rows listed (for example W#2 = M#1 = L-G5 = V#1 = R-7/8).
- Value follows the #94 lens: **voice/Talon + accessibility first, keyboard power users second**. V's "already covered for a Talon user?" column discounts a gap when gaze-ocr, Rango or the community mouse grid already give it to Talon users.
- Feasibility is rough: **S** = a few sessions on existing seams, **M** = a new pipeline stage or mode, **L** = a new subsystem or a reversed decision.
- Baseline is the #94 Notes list. Overlap with it means the draft is *fold into X*.

---

## Corrections to the input notes

These change dispositions, so they come first.

1. **The named pipe is not a live integration.** W, M, L and R list WH's `WindowsHinting_Pipe` (`TOGGLE` / `SELECT <label> [action]` …) as an existing inbound channel. Per the #33 resolution, `NamedPipeService` is never registered in the shipped build (its DI line is commented out). The README's Talon path uses **simulated keystrokes**, and `modernize` phase 00 **deletes** the pipe with no replacement. V is the only note that says the Talon example drives WH by key presses. Consequences:
   - Every "outbound IPC" gap is really **reviving an external control surface that #33 decided against**. That is a reversal, not an extension.
   - #33 left a transport blueprint (a hardened `NamedPipeServerStreamAcl` pipe, `CurrentUserOnly`, a line protocol, the intent funnel as the attachment point). #93 named the triggers for resuming it, including "a need for acked state reads". The top voice gap below is exactly that trigger.
2. **The WH baseline differs between notes.** W, M and L describe WH as foreground-window plus taskbar, A–Z labels and Space/Enter commit, which is accurate. R adds the key fact that the labels are **not prefix-free** (`A` is a prefix of `AA`), so the commit key is structural and not a style choice.
3. **"KeyNavigator" does not exist** as a Windows desktop hinter (W). The #94 tier list can drop it.

---

## Cross-cutting families

Several gaps share one enabler or one subsystem. Triage should disposition a family as a unit, then confirm the members.

| Family | Members | Why it is one unit |
|---|---|---|
| **F1 External control channel** | G2 outbound state, G3 target injection, the one-shot half of G1, G15's copy/read-back half, CLI flags | All need a listener that #33 deleted. One decision (reopen #33 or not) gates them all. |
| **F2 Selection by name** | G1 click-by-name, G6 role filter, reflow and spatial "next" | One selection model: filter the hint set by UIA text or role, then re-label the survivors. |
| **F3 Non-UIA targets** | G4 OCR/CV discovery, G5 grid fallback, G8 pointer mode, plus G3 as the "let Talon's OCR supply them" route | All answer "UIA returned nothing useful here". The first decision is build-vs-delegate. |
| **F4 Pointer actions** | G7 drag, G9 click variants and modifiers, G8 nudge | All are SendInput work outside the UIA activator chain. They share modifier-normalisation hygiene. |
| **F5 Scope** | G10 all windows / monitor / named app, G11 window picker | Both widen the scan root beyond the foreground window. Overlays already exist per screen. |
| **F6 Label scheme** | G12 prefix-free and alphabet, G13 numeric, G14 stable labels, auto-commit | All live in the labels stage of the `modernize` hint engine (phase 02/04). |

---

## The register

Ranked by value for voice/a11y users, then power users. **Draft** = the proposed disposition for #101 to accept or change.

| # | Gap | Who has it | Sources | Value: voice / power | UIA feasibility | Baseline overlap | **Draft disposition** |
|---|---|---|---|---|---|---|---|
| **G1** | **Select by element name/text.** Partial, case- and accent-insensitive match on UIA `Name` (then `HelpText`/`Value`). Numbered disambiguation when several match. Re-label the survivors. Auto-fire on a unique match. Both an **in-mode filter** (works today over Talon keystrokes) and a **one-shot "click <name>" verb** (needs F1). | Voice Access, Voice Control, Dragon, Rango, gaze-ocr; Fluent (in-window search), Homerow, Shortcat (fuzzy + synonyms), Wooshy, Neru (`/`), Vimium/Tridactyl/Surfingkeys filter modes | W#2, M#1, L-G5, V#1, R-7/8/9/5 | **Highest** / high. Every OS voice tool does it, and nothing covers UIA names on Windows for Talon users. | M. Text comes from properties WH already caches. The in-mode filter needs a text-entry path; IME and dead keys need a real text field. | #21 is generic only. The one-shot verb depends on F1. | **New map** ("select by name"). Charter: the in-mode filter first; the one-shot verb is a later ticket gated on G2. |
| **G2** | **Outbound hint state + event stream.** Publish the current hints (label, name, role, rect, source) as JSON on request, plus mode-enter, mode-exit and select events. This lets Talon build dynamic lists and confirm actions. | Neru (`query hints --json`, `watch`), Wooshy (notifications), Rango (scripting API); Talon 1.0 exposes a UIA tree API itself | W#1, M#2, L-G4, V#2 | **High** / low. It enables G1's one-shot form, G3, and read-back actions. | M for the code, **L for the decision**: it reverses #33. A transport blueprint already exists (#33), and #93 named this as a resume trigger. | #33 (decided: no surface), #93 (resume triggers), #66 (overlay title as a level-triggered state signal). | **New map** ("revive the external control channel"). Its first ticket is a grilling on whether #93's "acked state reads" trigger has now fired. If that is a no, park F1 as a whole. |
| **G3** | **External target injection.** Another tool supplies rectangles; WH labels them and acts on them. | Neru (`--targets`), warpd (`--query`), wl-kbptr (stdin areas) | M#19, L-G4 | Medium / low. It gives WH OCR coverage *through Talon's existing OCR* without building OCR. | S once G2's channel exists. It is one more hint source on the `modernize` phase 04 seam. | Phase 04 `HintSource` seam; #33. | **Fold into the G2 map** as a ticket. It is also an option inside G4. |
| **G4** | **Non-UIA discovery fallback: OCR word/line targets and CV contour detection,** for canvases, remote desktop, games and thin trees. Auto-fallback when UIA returns little, or per app. | Fluent (Image Recognition + OCR + Auto), Neru (`Windows.Media.Ocr` + contour, manual switch only), mousemaster (`ui-vision`), hints (OpenCV fallback chain), Superkey (OCR-first), gaze-ocr | W#3, M#4, L-G2/G3, V#12 | Medium (gaze-ocr already gives Talon users OCR click and select) / high. | M–L. `Windows.Media.Ocr` is built in on Win10+ and needs a language pack. CV is new code. | #20 is UIA-only, so partial. **Prior research exists outside any map:** `docs/research/ocr-ai-region-detection.md` (branch `research/ocr-ai-region-detection`, 2026-10-13: in-box OCR or PP-OCRv5-mobile plus a YOLOv8n interactable detector, cropped to UIA-thin regions, under 100 ms on CPU per vendor numbers; merge rule "UIA authoritative, vision box only if IoU ≤ 0.1 with every UIA box"), building on an earlier `cv-clickable-scrollable-detection.md`. | **New map** ("non-UIA targets", family F3), seeded from that research. The first decision is **build OCR vs delegate to Talon via G3**. That needs a local latency and quality benchmark (the open questions the OCR note already lists), not a competitor hands-on. |
| **G5** | **Wider scope.** Hint every visible window ("show numbers everywhere"), a chosen monitor (monitor-select hints), or a named background app ("show numbers on <app>"), with occlusion culling. | Voice Access, Voice Control, Dragon grid; Fluent, mousemaster, keyhop, win-vind, Neru (monitor select), warpd | W#5, M#10, L-G15, V#5, R-22 | Medium-high / medium | M. Scan several roots and cull occluded windows. WH's per-screen overlays already fit. | #26 phase 04 (scan pipeline, partial); #81 (taskbar only). | **Backlog issue** sized as a new-map candidate. It needs `modernize` phase 04 first. |
| **G6** | **Role-filtered hinting.** Hint only buttons, links, checkboxes or edit fields, for fewer and shorter labels. | Dragon ("Click Link/Button…"), Shortcat (`btn`, `radio`), Wooshy role search, Neru `--role` | M#1 (role filter), V#7, L-G5 | Medium / medium | S. It is a ControlType filter on the existing scan. | #20 and #21 loosely. | **Fold into the G1 map.** It is the same selection-filter model. |
| **G7** | **Drag target→target.** Hint the source, hint the destination; mark/drag pairs. | Voice Access, Voice Control, Dragon, community mouse grid; Neru, Scoot, warpd, wl-kbptr (two-pick, edge-anchored), hints, keynav | W#8, M#7, L-G6, V#3, R-21 | Medium (Talon has community drag) / medium | S–M. SendInput down, move, up. UIA has no drag pattern, and some apps ignore a cursor warp as a drag (see local addendum). | None. #25 is adjacent only. | **Backlog issue** ("drag between two hints"), family F4. |
| **G8** | **Stable / memorable labels and saved marks.** The same element keeps its label across scans (keyed on AutomationId/RuntimeId), plus "mark X as <name>". | Rango (marks), Tridactyl (`numeric` stability, `words` labels), Voice Control "show names" | R-4, V#13 | Medium (repeat workflows) / low | M. The identity work overlaps #72's RuntimeId reconcile. | #72 (RuntimeId), #81. | **Backlog issue.** Revisit after #72 ships. |
| **G9** | **Click variants and modifiers.** Middle and triple click; Alt/Win and combination modifiers; **sticky** modifiers (tap to hold); momentary modifiers held while typing a label; count-prefix repeat. | Neru, Scoot, Wooshy, Shortcat, warpd, keynav, hints (`2jk`), Vimium C | W#8, M#14, L-G16, R-12 | Low-medium / medium | S. These are extensions of the existing click-action modes. | #25 is adjacent; the existing Shift+key modes. | **Backlog issue** (one issue, family F4). |
| **G10** | **History / repeat-last / recent targets.** "Again", jump back to the previous target, hints over recently used targets only. | warpd history mode, keynav macros, mousemaster `position-history`, Vimium C | W#13, L-G10, R-14 | Medium ("repeat that" is natural voice) / medium | S–M. Store RuntimeId/AutomationId per app and re-find it. | None. #70 is scroll-only. | **Backlog issue.** |
| **G11** | **Window-picker hints.** Label top-level windows, including minimized ones, to switch to. | keyhop, Homerow (Mission Control), Shortcat (window search) | W#12, M#11 | Low (Talon already has "focus <app>") / medium | S–M. Enumerate top-level windows; this is a taskbar-like surface. | #81 is adjacent. | **Backlog issue**, family F5. Low priority under the voice lens. |
| **G12** | **Non-click actions on a hinted element.** Copy its text, URL or value; focus without activating; select its text; read it aloud or send it back to the caller; explicit expand/toggle. | Wooshy (`⌘C` copy), Fluent (select text), Vimium/Vimium C/Tridactyl action catalogue, Voice Access ("Focus/Expand/Toggle <item>"), Rango (flick, copy, type-into) | W#9/#18, M#13, V#9/#14, R-15 | Medium / medium | S per action. UIA `Value`, `TextPattern`, `SetFocus`, ExpandCollapse, Toggle. | **#8/#25** for explicit expand/toggle/focus. Read-back needs F1. | **Split:** explicit expand/toggle/focus → **fold into #8/#25**. Copy text/value and select text → **backlog issue**. Read-back → **fold into the G2 map**. "Type into field" reduces to focus-only + Talon dictation → covered by the #8/#25 fold. |
| **G13** | **Feedback for non-visual confirmation.** A sound on activate or fail; a screen-reader announcement of the mode and the selection through the UIA Notification event. | Homerow (sounds), Shortcat (VoiceOver), Neru (alerts) | M#20 | Medium (a11y) / low | S | None | **Backlog issue** (small). |
| **G14** | **Hide the overlay from screen capture** (`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`), as an opt-in preference. | Neru (on macOS only; its claim that Windows has no equivalent is wrong) | M#20 | Low (it could hide hints from a user's own OCR tools, so off by default) / low | S. One call; behaviour with WinForms layered windows needs checking. | #26 phase 06/11 (overlay). | **Backlog issue** (small). Note the gaze-ocr interaction. |
| **G15** | **Grid / recursive-grid / bisect fallback** for areas with no targets. | Voice Access, Voice Control, Dragon, community mouse grid; Neru, mousemaster, warpd, keynav, win-vind, wl-kbptr, Scoot | W#4, M#3, L-G1, V#8, R-19/20 | **Low for voice** (the community mouse grid covers it) / high | M. A new mode; pure geometry. | None | **Backlog issue**, family F3. Revisit if the G4 map decides on "build". |
| **G16** | **Pointer "normal mode" / nudge / two-stage refine.** Arrow or hjkl nudge after a pick, acceleration, hint-then-sub-grid. | warpd (normal mode, `hint2`), Neru (nudge + glide), Scoot, mouseless | M#7, L-G8, R-20/21 | Low / medium | M. A new mode. | None | **Reject for now.** It is keyboard-mouse territory outside WH's element-hinting core, and Talon users have the community mouse grid and mouse commands. Reopen only if G7 drag needs a fine-adjust step. |

### Fold-ins: gaps already covered by baseline work

These need no new home. Triage confirms them and appends a pointer to the target map or issue.

| Gap | Who has it | Sources | **Draft: fold into** |
|---|---|---|---|
| **Prefix-free labels, configurable/home-row alphabet, uniform length, spread first letters**; auto-commit on a unique label; post-activation key-swallow window | Vimium, Tridactyl, Homerow, Neru, keyhop, Fluent, hints, warpd | W#6, M#6/#15, L-G12, R-1/2/3/9 | #26 phase 02 (hint engine labels stage) + phase 08 (preferences). Prefix-free removes Space-to-commit. |
| **Numeric label mode** (the voice-tool default) | Voice Access, Voice Control, Dragon, mouse grid | V#6 | #26 phase 02, as an alphabet preset. The voice lens says to ship it as a preset, not as the default. |
| **Label theming**: colours, size, font, contrast, idle fade | Fluent, Scoot, Neru, warpd, hints, keyhop, Voice Control (fade) | W#11, M#16, L-G13, V (fade) | #26 phase 06 (overlay composer), phase 08 (preferences), phase 11 (per-pixel alpha). Arrow-key *label* nudging (Fluent) → **reject**: niche. |
| **Overlapping hints**: layers, rotate, peek | hints (layers), Vimium (Space rotate), Surfingkeys (peek) | L-G11, R-18 | **#21** ("handling overlapping hints"). |
| **Occlusion / visibility hit-test** | Neru, mousemaster, Shortcat (visibility boost) | W#5, M (discovery table) | **#21** + #20. |
| **Pre-scan / caching / warm daemon** | Fluent, keyhop, hints, vimium-everywhere | W#10, L-G19 | #26 phase 04, #20, #81 (event reconcile). |
| **Per-app profiles and per-app include/exclude roles** | Neru `app_configs`, hints `application_rules`, Vimium C, Homerow (ignored apps) | W#14, M#9, L-G14, R-17 | **#62** (profile = window rule grown) + #20. |
| **Chaining / persistent overlay / stay-in-mode / multi-select queue / re-hint key** | Homerow, Shortcat, Neru, Voice Control, Rango, Tridactyl, Vimium, goodnight-mouse | M#5, L-G9, V#4/#10, R-11/13 | **#72** (chained hinting) + **#81** (persistent taskbar). Multi-select "same set" vs re-scan is a design note for #72. |
| **Scroll mode, targeted scroll, snap target to top/centre** | Homerow, Vimac, Neru, warpd, hints, Rango, Voice Access | W#7, M#8, L-G7, V#11, R-13/16 | **#54 / #62** + #70. |
| **Menus/popups in scope; menu-bar and status-area hints; notifications** | mousemaster, Wooshy, Shortcat, Neru (macOS), Fluent (notifications) | W#15/#16, M#12 | **#44 / #72** (+ #78/#79) for menus; **#81** for tray and notifications. |
| **Inspector / why-not-hinted / doctor** | Wooshy Inspector, Neru `roles --explain` / `doctor`, hints `-vv` | M#17, L-G18 | **#51** (live-UIA tool) + #19 (logger UI). |
| **Config file with schema, hot reload** | mousemaster, Neru | W#17, M#18 | #26 phase 07 (debounced hot reload is already planned). |
| **CLI one-shot flags** | warpd, hints, Neru, Hunt-and-Peck, keyhop | W#20, L-G17 | **G2 map** (a CLI is just a client of the channel). Without F1 → reject. |
| **Alternate "shifted" semantic action** (for example close tab) | goodnight-mouse | L-G20 | **#25**. |

### Rejects

| Gap | Sources | Why reject |
|---|---|---|
| **Text selection / caret placement by spoken text; "show text numbers"** | V#12, M (VC), W#9 (partly) | gaze-ocr already gives Talon users this, and UIA `TextPattern` caret work is a separate product area. G12 keeps the small "select an element's text" action. |
| **Multi-target in one utterance** ("A and C", "A until F") | V#10, R-6 | Rango-specific grammar. The Talon side can loop G1/G2 calls, and #72's chaining covers serial picks. Revisit only if F1 lands. |
| **Browser tabs as targets, emoji insertion, label delay** | M#21 | Tabs are already UIA `TabItem` targets; emoji is out of scope; label delay has no voice value. |
| **Spatial "next/left/right hint" navigation, `hintshift`** | R-10, R-6 | Minor. It rides along with G1 if wanted, not on its own. |

---

## What graduates from the map's fog

The #94 map has two fog items. The synthesis resolves both:

1. **Hands-on verification of a competitor: not needed.** No draft above hinges on whether a competitor's documented feature really works. Every draft rests on *whether WH should have the capability*, which the documentation answers. The two measurements that do matter sit **inside** future maps, not in #94:
   - OCR latency and quality (`Windows.Media.Ocr` vs Talon's OCR) → the first ticket of the G4 map.
   - Scan-shape cost on big trees → #20.

   Recommendation: drop the fog item rather than graduate it into a ticket.
2. **Cross-cutting families: identified (F1–F6 above).** Only **F1** needs a decision before triage can finish: G2's disposition is "new map" only if triage is willing to reopen #33. That is one question for the #101 grilling, not a separate ticket.

**So #101 can start immediately.** Suggested triage order: F1 (it gates G2, G3, the G1 verb and part of G12), then G1, G4, the remaining kept rows, the fold-ins (quick confirm), then the rejects (quick confirm).

---

## Where Windows-Hinting is already ahead

These are not gaps, but they are worth keeping in view so triage does not trade them away:
- **The UIA pattern activator chain** (Invoke, Toggle, SelectionItem, ExpandCollapse, SetFocus, then the mouse). Most rivals only synthesise clicks.
- **The cursor is restored** after a mouse click.
- **Server-side UIA conditions** with pattern-availability gating.
- **Window rules** keyed by exe, class and title, with custom root strategies. Exe-path-only matching cannot tell UWP apps apart.
- **Taskbar hints**, plus #81's event-driven tray reconcile. No rival hints Windows shell surfaces this well.
- **#72's re-scan without a timer** (progressive reconcile by RuntimeId).
- **One overlay per screen**, so a focused window on a secondary monitor still gets hints.
- **UIAccess install**, which reaches elevated windows without running elevated.
