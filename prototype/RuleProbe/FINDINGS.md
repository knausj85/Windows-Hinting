# I #89 — Probe the problem apps to populate the built-in window rules

Tool: `RuleProbe <process|title-substring> [--all] [--depth N] [--warm]` (headless UIA dump: containers, class tokens, ARIA role/landmark, ScrollPattern view sizes, control-type histogram, Document URL read timing). Scroll actuation was re-tested with `ScrollSelectProbe` (Teams queries added on `prototype/slack-scroll-panes`).

Draft built-in rules: [`rules/builtin-draft.json`](rules/builtin-draft.json). Raw dumps are not committed (they contain personal data).

## Cross-app facts
- **FrameworkId is useless as a window matcher.** Chromium, Electron and WebView2 window roots all report `Win32`; `Chrome` appears only on descendants. Match by window class / exe / app definition instead.
- **Chromium `ClassName` is the DOM class list** (whitespace-separated, often hashed). A selector's `className` must match **one token**, not the whole string.
- **URL read** (RootWebArea Document `Value`): 9–10 ms in Edge, ~46 ms in Teams. Fine for per-scan matching.
- **Click targets:** no click rules needed for any probed app.

## Per app
| App | Finding | Rule |
|---|---|---|
| Slack (Edge web app; desktop `Slack.exe` ran windowless) | Generic detector finds nothing usable. Sidebar = `Group` token `p-view_contents--sidebar` (inner Tree `c-virtual_list__scroll_container`); messages = `Group` token `p-view_contents--primary`. | className-token selectors; app = exe OR url |
| VS Code | Explorer/lists = `Tree`/`List` token `monaco-list`; editor + tab strip found generically. `monaco-breadcrumbs` must be excluded. | className-token selectors |
| Teams (`ms-teams`, class `TeamsWebView`) | Chat list = `Tree` token `fui-Tree` (sometimes exposes ScrollPattern → generic owns it). Message list = `Group` **Name `Message List`**, hashed classes — only the name identifies it. Calendar = anonymous Group (content ~2400 px tall) found only via its `DataItem` (role gridcell) children. | `name` selector; `region: parent` |
| Gmail (Edge) | Mail list found generically (ScrollPattern). Folder list = className token `aeN` (also ARIA landmark navigation). | className-token selector |
| Explorer | Inactive tabs produce **phantom scroll regions** (visible HWNDs, on-screen rects). | `root` active-tab strategy also scopes scroll |

Prototype run (Teams): calendar, chat list and message list all detected and scrolled; operator verdict "mostly works".

## Schema changes accepted (amend E #67)
1. **Drop `FrameworkId` from the matcher** — use window class (and app definitions).
2. **`className` selector matches a single whitespace-separated token** (case-insensitive).
3. **Add `name` selector** — built-ins that use it are English-only; documented as such.
4. **`root` strategy also scopes scroll detection** (Explorer).
5. **`region: parent` on an expectedRegions selector** — the region is the parent of the matched elements, deduped (one region per container), clipped to the window.

## Unverified (for the builder, not blocking F)
- PowerPoint `Image`/`HeaderItem` harvest; Firefox URL read (operator has neither).
- Slack thread pane (`--secondary` guessed); Slack desktop app tree.
- What "mostly works" missed in the Teams run was not specified.
- `FileExplorerCustomStrategy` is commented out on `modernize` — the scroll scoping depends on reviving it.
