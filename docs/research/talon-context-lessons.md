# Lessons from Talon context matching for window rules

Ticket: #92 (L), child of #62, feeds F #68. Date: 2026-10-12.

This note compares the window-rule design, as amended so far, with Talon's context matching and the way the community repo uses it. The window-rule design covers E #67 (sections, per-section most-specific-wins, specificity = matcher-field count, selectors, add/remove deltas), H #88 / J #90 (`url` field and syntax) and K #91 (`appId`, tentative). It is written for a reader who knows Talon well, so Talon concepts are not re-explained.

## Sources

Sources are read-only. "Live" means the operator's user dir `%APPDATA%\talon\user\community`. "Upstream" means the `C:\Users\knausj\git\community` checkout. `C:\Users\knausj\git\talon_community` was spot-checked and has the same shape.

| Ref | Source |
|---|---|
| D1 | Talon docs, *Talon Files*: https://talonvoice.com/docs/reference/guide.language.html (header matchers, OR within a key, AND across keys, `not`, `and` grouping, `/re/flags`) |
| D2 | talon.wiki, *.talon Files*: https://talon.wiki/Customization/talon-files/ (matcher list; `app` = "explicitly declared, well-known name") |
| D3 | talon.wiki, *Basic customization*: https://talon.wiki/Customization/basic_customization/ ("more specific (i.e. it has more rules in it) … will take precedence"; conflicts between non-identical commands follow "internal, undocumented rules") |
| D4 | Talon's shipped type stubs: `C:\Program Files\Talon\Lib\site-packages\talon\scripting\context.pyi` (`score_match(match) -> tuple[int, ...]`; `Context` defines `__lt__`/`__gt__`) and `registry.pyi` (`Impls.settings/lists/actions: dict[str, list[...]]`, but `Impls.tags/apps: frozenset[str]`; `Decls.apps: dict[str, set[AppDecl]]`) |
| C1 | `apps/slack/slack.py` (live): `mod.apps.slack` assigned four times (Windows name/exe, mac bundle, and `tag: browser` + `browser.host: app.slack.com`); `ctx.matches = "app: slack"` |
| C2 | `apps/chrome/chrome.py`, `apps/edge/edge.py`, `apps/firefox/firefox.py`, `apps/teams/teams.py`, `apps/discord/discord.py`, `core/application_matches.py` (live): per-OS identity sets behind one name; repeated `os: windows / and app.name: … / os: windows / and app.exe: /^x\.exe$/i` groups |
| C3 | `apps/vscode/vscode.py` (live): every Windows alternative carries `and not win.class: #32770`, which excludes the app's own Open/Save dialogs; `ctx_editor` adds `and win.title: /focus:\[Text Editor\]/` |
| C4 | `apps/firefox/firefox.py` (live and upstream): the comment "Make the context match more specifically than anything else" sits above a header padded with `os: windows / os: linux / os: mac / tag: browser / app: firefox` |
| C5 | `CONTRIBUTING.md` (live): P02 "Use `browser.host` matcher for web apps … the only unambiguous way of referring to a web app"; P04 "Use both `app.name` and `app.exe` matchers for apps on Windows … the MUICache can break" |
| C6 | `apps/README.md` and `tags/browser/browser.py` (live): on Windows, `browser.address()` parses a URL out of the **window title**, so it needs a URL-in-title extension. `tags/browser/browser_mac.py` reads `AXWebArea.AXURL` instead (macOS accessibility) |
| C7 | `apps/windows_explorer/windows_explorer.py`, `apps/powershell/powershell_win.py`, `apps/windows_command_processor/command_processor_win.py` (live): app identity needs the title (`/File Explorer/`, a PowerShell or cmd tab hosted in `WindowsTerminal.exe`), and the scripts *write* the title to make that work |
| C8 | `plugin/talon_helpers/talon_helpers.{talon,py}` and `create_app_context.py` (live): `talent copy context` puts a ready-to-paste header on the clipboard; `talent context` opens an imgui panel with name, exe, bundle, hostname, title, **AppUserModelId, window AppUserModelId, window class**; `talent create app context` scaffolds `apps/<name>/` |
| C9 | `core/app_switcher/windows/installed_applications.py` (live): `get_application_user_model_for_window` uses `SHGetPropertyStoreForWindow` → `PKEY_AppUserModel_ID`, the same read K #91 proposes |
| C10 | `plugin/mouse/mouse.talon`, `apps/termite/termite.talon`, rango-talon `direct_clicking.talon` (live): `not tag:` / `and not win.title:` used as exclusions |

C8's AUMID and class rows, C3's `#32770` exclusion and C9's window-AUMID helper are in the **live (operator) dir only**, not in upstream. They are the operator's own additions.

## How Talon works, in the terms of our design

**Matcher vocabulary.** The documented keys are `os`, `tag`, `mode`, `language`, `code.language`, `hostname`, `app`, `app.name`, `app.exe`, `app.bundle`, `app.exe_path`/`app.path`, `title`, `speech.engine` and `win.filename`/`win.file_ext` (D1, D2). Community also uses `win.title`, `win.class` and `browser.host`, plus `user.*` scopes (C3, C6; `user.windows_hinting_active` comes from `mod.scope` in `apps/windows_hinting/windows_hinting.py`). On Windows, `browser.host` is only as good as the window title (C6).

**Match semantics.** Repeated lines with the same key are OR'd. Different keys are AND'd. `and` joins lines into a group, and the groups are OR'd. `not` negates a line. A value is an exact string or `/regex/flags` (D1). In practice, community writes OR almost only to list **alternative identities** of one app, and puts those lists in `mod.apps.*`, not in per-feature headers (C1, C2).

**App indirection.** `mod.apps.x = "<header>"` can be assigned many times, and each assignment *adds* an alternative (`Decls.apps: dict[str, set[AppDecl]]`, D4; C1). `app: x` in any header is true when any alternative matches. A single definition can span native and web: `slack` = `slack.exe` OR the mac bundle OR `tag: browser` + `browser.host: app.slack.com` (C1).

**Precedence.** Each action, setting and list is resolved **separately**: the active contexts are ranked, and the highest-ranked context that *defines that key* wins (`Impls.settings: dict[str, list[SettingValue]]`; the community helper reads `registry.lists[name][-1]` as the winner, D4). Tags and active apps are a **union** over all matching contexts (`frozenset`, D4). The rank comes from `score_match → tuple[int, ...]`, compared as a tuple (D4). The wiki only says "more rules wins" (D3), and the exact weighting and tie-break are **undocumented**. Authors respond by padding headers to win (C4). Actions have a cascade (`actions.next()`); settings and lists do not.

## Ranked recommendations

### 1. ADOPT — named app definitions (`apps`) referenced by rules

This is the strongest lesson. Every web/PWA/desktop app community cares about has one name and many identities (C1, C2), and we have the same problem with more fields: exe, `appId` (per browser and profile, K #91), `url`, and Electron versus PWA versus tab. Without indirection, every rule for Slack (detection, actuation, later `menu`/`settle`) repeats the identity set, and K's opaque AUMIDs get copied around.

**Proposed amendment (E #67 storage + matcher):**

```jsonc
// rules/slack.json: a file may hold apps and rules
{
  "apps": [
    { "name": "slack",
      "match": [                               // alternatives, OR'd (Talon mod.apps.x assigned N times)
        { "exe": "slack.exe" },                // each alternative = AND of matcher fields
        { "appId": "/^app\\.slack\\.com-.*!App$/i" },
        { "url": "app.slack.com" }
      ] }
  ],
  "rules": [
    { "name": "Slack: sidebar is scrollable",
      "app": "slack",                          // + any other matcher fields, AND'd
      "scrollDetection": { "expectedRegions": [ { "automationId": "…" } ] } }
  ]
}
```

- **OR lives only here.** An app is a list of alternatives, and each alternative is the same AND-matcher a rule uses (including `not`, see item 4). A rule stays an AND of fields. This matches how community actually uses Talon's OR (C1, C2).
- **Specificity of `app`:** count the fields of the **alternative that matched**, as if it were written inline, plus the rule's own fields. So `app: slack` matched via `{exe}` is 1, and `app: slack` + `url` is 2. Counting `app` as a fixed 1 would let an app defined by `exe+appId` lose to a plain `exe+class` rule, which surprises people. Two rules that both say `app: slack` always match through the same alternative, so their relative order stays stable.
- **Same-name semantics:** a user `apps` entry whose `name` matches a built-in one **adds** alternatives (Talon's additive `mod.apps` behaviour, C1). A rule can still be overridden by reusing its `Name` (phase 07). If the probe finds a built-in alternative that must be removed, add `"replace": true` then, not before.
- **Lazy `url` stays lazy:** an alternative containing `url` is evaluated only after the cheap fields of the other alternatives fail, and only on a browser-like window (H #88's gate). This is the analogue of community gating `browser.host` behind `tag: browser`. Unknown app name in a rule → load error for that rule (phase-07 notify), never match-all.
- **Groups double as capability sets.** See item 6.
- Main bridge: `HintOverlayOptions.WindowRules` stays a list. Add an optional `WindowApps` list next to it, or wait for phase 07. F decides.

### 2. ADOPT — "copy window identity" and "explain effective policy"

This closes K's TBD with a yes. Talon users find matcher values in three ways (C8):
- `talent copy context` puts a **ready-to-paste header** on the clipboard;
- `talent context` shows every identity field, which the operator has extended with AUMID and window class;
- `talent create app context` scaffolds the files.

The operator already needed window AUMID and class in Talon, and built them in locally (C8, C9). Talon's real gap is the **ranking**: nothing shows *which* context won and why (D3, D4), and that is what drives header padding (C4).

**Proposed amendment (F #68 scope):**
- **Copy window identity** (tray menu item and/or hotkey). It writes a JSON skeleton to the clipboard: `{"name": "<exe stem>", "exe": …, "class": …, "frameworkId": …, "appId": …, "title": …}` plus `url` (scheme + host + path, **query and fragment stripped**) when a Document URL is present. It also emits an `apps` stub. Clipboard only; never logged (H #88's no-URL-logging rule).
- **Explain effective policy** (debug command or log line at Debug level). For the foreground window, print each matching rule with its specificity and, per section, the winner and the tie-break used. URLs are shown as host only. This is the part Talon never had, and it makes the count-based specificity safe to live with.

### 3. ADOPT (keep) — per-section resolution; record per-field resolution as the known alternative

Talon resolves **each key separately** over the ranked contexts (D4). That is exactly E #67's "most specific rule *that has the section*" idea, and it supports it: a `FrameworkId=Chrome` actuation rule and a `slack.exe` detection rule both apply, just as `edge.talon` settings and `browser.talon` commands both apply in Talon. Our unit is the **section**, while Talon's is the **individual setting**. So if two rules both set `scrollDetection`, the less specific one's other fields (e.g. `warmUp`) are lost, not merged. E #67 chose this deliberately ("no cascade within a section").

Keep it for v1, with one recorded trigger: if the probe ticket or F finds a real case of two rules each contributing **different scalar fields** to one section, switch **scalar fields** to per-field resolution (Talon's model) and keep **list deltas** per-section. Do not add an `actions.next()`-style cascade.

### 4. ADAPT — `not` as an exclusion object, uncounted

Community uses `not` for real. The most relevant case for us is C3: an exe-only VS Code rule would otherwise also fire inside VS Code's **own Open/Save dialogs** (`#32770`), and that applies equally to our `root`, `click` and `scroll*` sections. C10 shows exclusions by title and tag as well.

**Proposed amendment (matcher):** an optional `"not": { <matcher fields> }` on a rule or on an app alternative. The rule matches if its positive fields match and the `not` block does **not** fully match (Talon `and not` group semantics). `not` fields **do not count** toward specificity: they narrow the set of windows without identifying the window any better, and counting them would invite padding. Candidate built-in: a default `apps` alternative or a documented pattern that uses `not: {class: "#32770"}`. Do not make it hidden behaviour.

### 5. ADAPT — one string-match grammar for all text fields; exe/class/appId case-insensitive by default

Community writes `app.exe: /^slack\.exe$/i` almost everywhere (C1, C2, C3, and the generator in C8). The regex is there only to get anchored, case-insensitive exact matching. `win.title` regex is used for real substring and structure matches (C3, C7). Our design currently has three dialects: title `exact|contains`, `url` plain-or-`/re/i` (J #90), and `appId` exact-or-`/re/i` (K #91).

**Proposed amendment:** every text matcher (`exe`, `class`, `title`, `frameworkId`, `appId`) takes **either** a plain string, compared exactly and case-insensitively (title: case-sensitive exact, to keep today's behaviour if F prefers), **or** `/regex/flags` with a match timeout, the same parser as `url`. This retires the `title` `exact|contains` mode (`contains` = `/…/`). A regex counts as one field. Optionally extend J's "plain beats regex" tie-break to every field. That is cheap and consistent, but not required.

### 6. ADAPT — "behaves like Chromium" via FrameworkId and named app groups, not declared tags

Talon tags are **declared** by one context (`tag(): browser` in `chrome.talon`) and **matched** by another (`tag: browser` + `browser.host`), and they union across contexts (D4, C1, C6). The analogue would be a section like `traits: ["chromium"]` plus a matcher `trait: chromium`. That makes rule activation depend on other rules, which means fixpoint evaluation, ordering questions and harder explanations.

Our case doesn't need it:
- **Detected** capability: window-root `FrameworkId` (E #67) already says "Chromium/Electron" without anyone declaring it. That is better than Talon, where `tag: browser` must be declared per browser app (C2).
- **Curated** capability: a named app group, e.g. `apps: [{ name: "chromium_browsers", match: [{exe: "chrome.exe"}, {exe: "msedge.exe"}, {exe: "brave.exe"}, {exe: "vivaldi.exe"}] }]`, and a rule `app: chromium_browsers`. This reuses item 1.
- **Shared behaviour** is already handled by E's named compiled behaviours (`strategy`, `axisPolicy`): data picks the behaviour, and code implements it.

Reject declared tags/traits for v1. Revisit only if a capability turns out to be neither detectable nor expressible as an app group.

### 7. REJECT — general boolean expressions (OR across arbitrary fields, nested groups) in rules

Talon allows OR'd groups in any header (D1), and that is where its scoring gets hard to predict: which group's lines count? C4's padding shows the result. With OR confined to app alternatives (item 1) and specificity defined as "the matched alternative inlined", every rule keeps one well-defined count.

### 8. REJECT — runtime-state matchers: `tag`, `mode`, `user.*` scopes, `hostname`, `os`, `code.language`, `win.filename`

- **Runtime state** (`tag`, `mode`, `user.*` scopes): window rules describe *what a window is*, not *what the user is doing*. Hint-mode state belongs in code. The Talon-side integration already reads our state through a title scope (`apps/windows_hinting/windows_hinting.py`), which shows the state flowing **from** us, not into our matcher.
- **Not applicable:** `os` (Windows only), `code.language` and `win.filename`.
- **`hostname`:** `rules/` is already per machine. If shared rule repos appear later, a file-level `"hosts": [...]` guard is the cheap answer.

### 9. REJECT — `app.name` and title-sourced URLs

- **`app.name`:** on Windows, Talon's `app.name` is the friendly name, which community itself distrusts (C5 P04, MUICache). It also changes by locale and version. `exe` (+ `appId`) is the stable key. Don't add `name`.
- **URL from the title:** community on Windows needs a URL-in-title extension (C6). On macOS it reads the accessibility tree's web-area URL (`AXWebArea.AXURL`, C6), which is the direct analogue of H #88's UIA Document `Value`. This supports H's choice; there's nothing new to adopt. Community's own principle (C5 P02: the host is "the only unambiguous way of referring to a web app") supports `url` as the primary web key, ahead of `appId`, as K concluded.

### 10. REJECT — numeric priority / weights; KEEP the documented deterministic tie-breaks

Talon has no explicit priority, and its tie-break is undocumented (D3, D4), so authors pad (C4). We already have what Talon lacks: specificity = count, then the URL tie-break (J), then user over built-in, then file order, all written down. Adding `priority: N` would recreate CSS `!important`. Overriding a built-in is done by reusing its `Name` (phase 07). Item 2's explain output makes the result visible.

## Matcher vocabulary: Talon ↔ window rules

| Talon key | Windows source (Talon/community) | Window-rule equivalent | Verdict |
|---|---|---|---|
| `app` | `mod.apps.*` alternatives | **`app` → named `apps`** | Adopt (item 1) |
| `app.exe` | process image; community writes `/^x\.exe$/i` | `exe` (case-insensitive exact) | Have; item 5 |
| `app.name` | friendly name / MUICache (C5) | — | Reject (item 9) |
| `app.path` / `app.exe_path` | full path | — (`exe` regex if ever needed) | Reject for v1 |
| `win.class` | window class (C3) | `class` | Have |
| `title` / `win.title` | window title | `title` | Have; item 5 |
| `browser.host` | title parse + extension (C6) | `url` host form (J) | Have (better source, H) |
| `browser.url` / `browser.path` | same | `url` path / regex form | Have |
| — (operator-local AUMID, C8/C9) | `SHGetPropertyStoreForWindow` | `appId` (K) | Have (tentative); same API the operator already uses |
| `tag` | declared by contexts | FrameworkId + app groups | Adapt (item 6) |
| `not` | line negation | `not: {…}` uncounted | Adapt (item 4) |
| `mode`, `user.*`, `hostname`, `os`, `code.language`, `win.filename`, `language`, `speech.engine` | runtime / platform | — | Reject (item 8) |
| — | — | `frameworkId` | Ours only; no Talon analogue |

## Uncertain

- **Talon's exact scoring** is closed source. `score_match` returns an int tuple compared lexicographically (stub, D4), and the wiki says "more rules wins" (D3). Whether keys are weighted (e.g. `app` versus `title`), whether OR'd lines or `not` lines count, and the tie-break are not verified. The comparison above depends only on "count-like, undocumented, padded in practice", which C4 shows directly.
- **How Talon scores `app:`**: whether it counts as 1 or as the inner alternative's terms is unknown. Our proposed "inline the matched alternative" rule is a design choice, not something copied from Talon.
- **Per-key resolution** is inferred from `Impls.*: dict[key, list[...]]` and community's `registry.lists[name][-1]` usage. That fits Talon's documented override behaviour, but the stub doesn't spell it out.
- **Slack desktop versus Slack PWA/web selector parity** (whether one `slack` app definition can safely share `expectedRegions` selectors across Electron and browser) is unverified. It needs the probe ticket (I #89 / E's probe). If they differ, define `slack_desktop` and `slack_web` and let `slack` be a group used only where behaviour is shared.
- **`browser.url`/`browser.path`** are not in the official matcher tables fetched here (D1, D2). Only `browser.host` is used in community (C5, C6). This repeats J #90's caveat.
- **File shape** (`{apps, rules}` object versus a separate `apps/` directory) is left to F/phase 07.
