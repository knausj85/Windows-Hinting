# Competitive inventory: voice "show numbers" and grid overlays

Resolves [#98](https://github.com/knausj85/Windows-Hinting/issues/98), a child of map [#94](https://github.com/knausj85/Windows-Hinting/issues/94). Desk research only, gathered 2026-10-13. Every row cites a primary source: vendor docs, a README, the repo source, or a type stub. Nothing was installed or run.

**Lens.** Voice and Talon users come first. Each gap answers one question: what does a Talon user driving Windows-Hinting (WH) miss? It is also checked against what that user already gets from the rest of the Talon ecosystem.

**WH reference point.** This is `main` at `e331d0d`, per `README.md` and the source:

- **Targets:** UIA elements in the foreground window, plus a separate taskbar hotkey. Window rules choose the UIA root strategy (`Configuration/WindowRule.cs`).
- **Labels:** letters only. They run `A`–`Z`, then `AA`, `AB`, … (`Labels.cs`).
- **Selection:** type the label prefix, then Space or Enter. You cannot filter by element name or text.
- **Actions:** the default is an activator chain (Invoke → mouse click → ExpandCollapse → SelectionItem → Toggle, plus SetFocus). Shift+L/R/D/M/C/S switches to left, right, double, mouse-move, ctrl-click or shift-click.
- **Integration:** a one-way inbound named pipe, `WindowsHinting_Pipe`. It accepts `TOGGLE`, `TOGGLETASKBAR`, `SELECT <label> [LEFT|RIGHT|DOUBLE|MOVE|CTRL|SHIFT]` and `DEACTIVATE` (`Services/NamedPipeService.cs`). The shipped Talon example drives WH with key presses, not the pipe (`apps/windows_hinting/windows_hinting_active.talon` in the operator's Talon user directory).

## Sources

| Ref | Source |
|---|---|
| VA1 | Microsoft, *Voice access command list*: https://support.microsoft.com/en-us/topic/voice-access-command-list-dac0f091-87ce-454d-8d57-bef38d3d8563 |
| VA2 | Microsoft, *Use voice to interact with items on the screen*: https://support.microsoft.com/en-US/accessibility/windows/voice-access/use-voice-to-interact-with-items-on-the-screen |
| VA3 | Microsoft, *Use the mouse with voice*: https://support.microsoft.com/en-US/Accessibility/windows/voice-access/use-the-mouse-with-voice |
| VA4 | Microsoft, *Use voice access on a multi-display setup*: https://support.microsoft.com/en-us/accessibility/windows/voice-access/use-voice-access-on-a-multi-display-setup |
| VC1 | Apple, *Use Voice Control commands to interact with your Mac* (overlays): https://support.apple.com/en-am/guide/mac-help/mh40719/mac |
| VC2 | Apple, *Customize Voice Control on Mac* (Overlay and Fade settings): https://support.apple.com/en-am/guide/mac-help/mchl9899c8a5/mac |
| VC3 | Apple, *Number and grid overlays* (macOS 10.15 guide, the first Voice Control release): https://support.apple.com/pa-in/guide/mac-help/number-grid-overlays-voice-control-mchl26854b08/10.15/mac/10.15 |
| DR1 | Nuance, *Using your mouse with MouseGrid* (Dragon 16): https://www.nuance.com/products/help/dragon16/dragon-for-pc/enx/dpg-cp/Content/CommandandControl/using_your_mouse_with_mousegrid.htm |
| DR2 | Nuance, *Working with Chrome* (Dragon 15, Web Extension): https://www.nuance.com/products/help/dragon15/dragon-for-pc/enx/dpg-cp/Content/Web/working_with_chrome.htm |
| CG1 | talonhub/community `core/mouse_grid/` (`mouse_grid.py`, `mouse_grid.talon`, `mouse_grid_open.talon`) and `plugin/mouse/mouse.talon`. Local checkout `0f1e9851` (2026-09-20). MIT. https://github.com/talonhub/community |
| RG1 | Rango README: https://github.com/david-tejada/rango (MIT; latest release v0.8.7 on 2026-03-09; last push 2026-10-13, per the GitHub API) |
| RG2 | rango-talon: https://github.com/david-tejada/rango-talon (MIT; last push 2026-06-17) |
| GO1 | talon-gaze-ocr README: https://github.com/wolfmanstout/talon-gaze-ocr (Apache-2.0 per its `LICENSE` file; last push 2026-10-05) |
| TA1 | Talon 1.0 shipped stub `C:\Program Files\Talon\Lib\site-packages\talon\windows\ax.pyi`: `Element.find/find_one` with filters, `runtime_id`, `rect`, `clickable_point`, and every UIA pattern accessor. There is also an `axquery` package. |
| WH | This repo, `main`: `README.md`, `Labels.cs`, `Services/NamedPipeService.cs`, `Services/ElementActivators/*` |

## Tool × feature matrix

Key: ✅ = documented. ◐ = partial or indirect. — = absent or undocumented. **Gap?** marks features WH lacks.

| Taxonomy | Feature | VA (Win) | VC (Mac) | Dragon | Talon grid (CG) | Rango (browser) | gaze-ocr | WH | Gap? |
|---|---|---|---|---|---|---|---|---|---|
| Discovery | Accessibility-tree targets | ✅ UIA (VA2) | ✅ AX (VC1) | ✅ (DR2: web via extension) | — | ✅ DOM (RG1) | — | ✅ UIA | |
| Discovery | OCR / CV targets | — | — | — | — | — | ✅ OS OCR + eye tracking (GO1) | — | **Gap** |
| Discovery | Target by spoken name, partial-word match | ✅ "Click privacy" → "Privacy & security" (VA2) | ✅ "Show names", "Click <name>" (VC1) | ✅ "Click <name>" or part of it (DR2) | — | ✅ "follow <text>", fuzzy (RG1) | ✅ fuzzy, homophones (GO1) | — | **Gap** |
| Discovery | Ambiguous name → numbers on the candidates | ✅ (VA2) | ◐ | ✅ numbers when several controls match (DR2) | — | — | ✅ disambiguation (GO1) | — | **Gap** |
| Discovery | Filter hints by role (links only, buttons only…) | — | — | ✅ "Click Link/Button/Check Box/Edit Box/Image/Radio Button/Text Field" (DR2) | — | ◐ include/exclude extra hints (RG1) | — | — | **Gap** |
| Scope | Foreground window | ✅ "show numbers here" (VA1) | ✅ | ✅ | ✅ "grid win" (CG1) | page | main screen (GO1) | ✅ | |
| Scope | Every window / whole screen | ✅ "show numbers everywhere" (VA1) | ✅ (VC1) | — | ✅ | — | ✅ | — | **Gap** |
| Scope | A named, non-foreground app | ✅ "show numbers on <app>" (VA1) | — | — | — | — | — | — | **Gap** |
| Scope | Taskbar | ✅ "show numbers on taskbar" (VA1) | n/a | — | — | — | — | ✅ | (#81) |
| Scope | Multi-monitor | ◐ numbers: current display; grid: per-display letters A/B/C (VA4) | — | ✅ grid on up to 3 monitors, say the monitor number (DR1) | ✅ "grid screen N" (CG1) | — | — main screen only (GO1) | ◐ overlay spans screens; scan is foreground only | partial |
| Scope | Text-content numbering (lines, words, characters) | — | ✅ "show text numbers" (VC1) | — | — | — | ✅ via OCR (GO1) | — | **Gap** |
| Labels | Numbers | ✅ | ✅ | ✅ | ✅ 1–9 | — | — | — | **Gap** |
| Labels | Letters | — | — | — | — | ✅ 1–2 letters (RG1) | — | ✅ | |
| Labels | Item names as labels | — | ✅ "show names" (VC1) | — | — | — | — | — | **Gap** |
| Labels | Saved named references that persist | — | — | — | — | ✅ "mark <target> as <name>" (RG1) | — | — | **Gap** |
| Labels | Hint size and style tuning | — | ◐ fade slider (VC2) | — | — | ✅ size/style commands, settings (RG1) | — | ◐ prefs | partial |
| Selection | Say the number or label alone (no "click") | ✅ "<number>" (VA1) | ✅ (VC1) | ✅ optional "Click" prefix (DR2) | ✅ | ✅ direct vs explicit modes (RG1) | — | ✅ via Talon keys | |
| Actions | Left, right, double click | ✅ incl. triple (VA1) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | |
| Actions | Modifier-click | — | — | — | ✅ "<modifiers> touch" (CG1) | ✅ new or background tab (RG1) | — | ✅ ctrl, shift | |
| Actions | Hover / move pointer | ◐ "move to" = focus (VA1) | ✅ | — | ✅ | ✅ hover and unhover (RG1) | ✅ | ✅ Mouse Move | |
| Actions | Focus without clicking | ✅ "Focus on <item>" (VA1) | — | — | — | ✅ "flick" (RG1) | — | ◐ SetFocus is only a chain fallback | **Gap** (minor) |
| Actions | Expand / toggle by name | ✅ "Expand/Toggle <item>" (VA1) | — | — | — | — | — | ◐ chain only (#8, #25) | partial |
| Actions | Drag from target to target | ✅ grid "Mark" → "Drag" (VA3) | ✅ "Drag <a> to <b>" (VC1) | ✅ Mark/Drag (DR1) | ✅ drag start/end (CG1) | — | ✅ (GO1 text select) | — | **Gap** |
| Actions | Scroll a targeted region | ✅ "scroll <dir>", continuous (VA1) | — | — | ✅ wheel, continuous (CG1) | ✅ scroll a hinted container; snap "crown/center/bottom <target>" (RG1) | ✅ eye scroll (GO1) | — | **Gap** (#54/#62) |
| Actions | Type into the target field | — | — | — | — | ✅ "insert/enter/paste <text> to <target>", "change", "pre/post" (RG1) | ✅ caret before/after text (GO1) | — | **Gap** |
| Actions | Select text by spoken text | ✅ "select <text>", "select from X to Y" (VA1) | ✅ via text numbers (VC1) | — | — | — | ✅ (GO1) | — | **Gap** |
| Actions | Multiple targets in one utterance | — | — | — | — | ✅ "air and bat", "until" ranges (RG1) | — | — | **Gap** |
| Actions | Copy link or text of a target | — | — | — | — | ✅ (RG1) | — | — | Gap (web-specific) |
| Chaining | Overlay stays up after an action | — | ✅ Overlay setting; "show numbers continuously" (VC2, VC3) | — | grid stays until you click (CG1) | ✅ hints always on (RG1) | — | — dismisses after commit | **Gap** (#72, #81) |
| Chaining | Undo or step back | ✅ grid "Undo" (VA3) | — | ✅ "Undo That" (DR1) | ✅ "grid back", "grid reset" (CG1) | — | — | ◐ Backspace removes a filter char | |
| Fallback | Numbered drill-down grid | ✅ 3×3, screen or window, "mouse grid 5 3 1" chain (VA1, VA3) | ✅ screen or window, drills until it can't refine (VC1) | ✅ 3×3, screen or window (DR1) | ✅ 3×3, zoom, phone/numpad layout, edge expansion (CG1) | — | — | — | **Gap** |
| Integration | Voice built in | ✅ | ✅ | ✅ | Talon | Talon | Talon | via Talon | |
| Integration | Scripting API | — | ✅ custom commands (VC2) | ✅ scripting language with a MouseGrid command ([scriptref](https://www.nuance.com/products/help/dragon/dragon-for-pc/scriptref/Content/scrptref/mousegrid.htm)) | ✅ Talon actions | ✅ `user.rango_run_action_on_reference` and others (RG1) | ✅ Talon actions | ◐ one-way pipe | **Gap** (#28/#93) |
| Integration | Export the target list to the voice grammar | n/a (built in) | n/a | n/a | — | ✅ extension resolves text (RG1) | n/a | — | **Gap** |
| Config | Per-app behavior | — | ✅ custom commands per app (VC2) | — | Talon contexts | per-site custom hints (RG1) | — | ✅ window rules | |
| Visual | Fade or dim the overlay when idle | — | ✅ "Fade overlay after inactivity" (VC2) | — | — | — | — | — | Gap (only matters with persistence) |
| Platform | | Windows 11, built in | macOS 10.15+, built in | Windows, commercial | Windows/Mac/Linux, MIT | Chrome/Firefox/Edge/Safari, MIT | Windows/Mac (no Linux OCR), Apache-2.0 | Windows 11 | |

Maturity and licensing:

- **VA** and **VC** are first-party OS features with no separate license.
- **Dragon** is commercial (Nuance, now Microsoft). Help pages for version 16 are current. Its product roadmap was not checked.
- **community** is MIT and very active (pushed 2026-10-12, 879 stars).
- **Rango** is MIT and active: v0.8.7 shipped 2026-03-09, and the repo was pushed the day of this research.
- **gaze-ocr** is Apache-2.0 and active. Its README calls it "alpha functionality which uses experimental/unsupported APIs" (GO1).
- **TA1:** Talon itself exposes a full UIA tree API (`find`, RuntimeId, patterns) that a Talon-side script could use. No community package found here uses it for hinting.

## What a Talon user driving WH misses, ranked

Ranked voice and accessibility first. "Already covered" means the Talon user already gets it from another tool, so the gap matters less for them than for a keyboard-only user.

| # | Gap | Who has it | Already covered for a Talon user? | Baseline overlap → suggested disposition |
|---|---|---|---|---|
| 1 | **Click by spoken element name.** Partial-word match, with numbered disambiguation when several elements match. WH only knows labels, and its one-way pipe cannot publish names to a Talon dynamic list. | VA2, VC1, DR2, RG1 (web), GO1 (OCR) | Partly: gaze-ocr for visible text, Rango in browsers. Nothing covers UIA names on Windows. | None directly. Depends on the outbound channel (#28/#33, #93). → **new map** |
| 2 | **Outbound IPC.** Publish the hint list (label, name, role, rect) and the overlay state to Talon. This is the enabler for #1, #7 and #9. | RG1 (scripting API), TA1 | No | **Fold into #28/#33 (#26) and #93/#66 (#62)** |
| 3 | **Drag target → target.** For example "drag A to F", or a mark/drag pair. | VA3, VC1, DR1, CG1 | Yes, by pointer through community drag plus the grid | None → **backlog issue** (new click mode) |
| 4 | **Persistent overlay.** Labels stay after an action, as with VC "show numbers continuously", optionally fading when idle. | VC2, RG1 | Rango only, browser only | **Fold into #72** (chained hinting) and **#81** (taskbar persistent) |
| 5 | **Wider scope.** All windows ("everywhere"), a named background app, and the monitor of your choice. | VA1, VA4, VC1, DR1 | No | Partial overlap with #44 and #81. → **backlog** or fold into #21 |
| 6 | **Numeric label mode.** Every OS voice tool uses numbers. Talon users say numbers fluently, and numbers avoid alphabet-word collisions. | VA, VC, Dragon, CG1 | n/a | None → **backlog** (label-scheme option; #26 hint engine) |
| 7 | **Role-filtered hinting.** Hint only links, buttons, checkboxes or edit fields, so there are fewer and shorter labels. | DR2; RG1 include/exclude | No | Loosely #20/#21 → **backlog** |
| 8 | **Numbered grid fallback** for no-UIA surfaces, at screen, window or element scope. | VA, VC, Dragon, CG1 | **Yes** (community mouse grid) | None. Low priority for Talon users; higher for keyboard users and #95/#96 peers → **backlog** or reject for the voice lens |
| 9 | **Type into the hinted field.** For example "insert <text> to <label>", clear the field, or put the caret at its start or end. | RG1 | Browser only (Rango) | None → **backlog**. Needs #2 for text payloads |
| 10 | **Multi-target in one command.** "A and C", or a range "A until F". This pairs with ctrl-click multi-select. | RG1 | Browser only | None → **backlog** |
| 11 | **Scroll by target, and snap a target to top, center or bottom.** | RG1, VA1 | Rango (web), gaze-ocr eye scroll | **Fold into #54/#62** (+ #70) |
| 12 | **Text selection and caret by spoken text, or OCR fallback** where UIA is empty. | GO1, VA1, VC1 | **Yes** (gaze-ocr) | None. Candidate cross-cutting "OCR/CV fallback" family (see #94 Not yet specified) |
| 13 | **Show names, stable references, and saved named marks.** | VC1, RG1 | Browser only | Partly #81 (RuntimeId stability) → **backlog** |
| 14 | **Explicit focus, expand and toggle actions** chosen by the user, rather than the chain's choice. | VA1, RG1 | No | **Fold into #8/#25** |

## Notes and caveats

- **Voice Access numbers do not persist.** They "get automatically dismissed when you select a numbered item" (VA2). The same page says overlays "work best on the primary monitor", and using them on a secondary monitor may show an error. WH already matches VA on dismissal. Persistence is where VC and Rango lead.
- **Number placement.** None of the OS tools document how they place numbers or how they handle overlapping labels. The overlap question in #21 has no prior art among these sources.
- **Dragon.** The numbered tags in DR2 are for web pages through the Dragon Web Extension. Dragon on native Windows apps matches by name. No general "show numbers" command was found for Dragon on Windows desktop apps.
- **No Talon UIA hinter found.** The talonhub/community checkout has no UIA-based hinting. Its only overlay targeting tool is the mouse grid (CG1). TA1 shows that Talon could build one, which is a possible future competitor or a possible integration route.
