# Window rules: design

The per-app customization layer shared by click hinting and scrolling. A **window rule** is
today's `WindowRule` (matcher + root strategy), grown. Terms (**window rule**, **rule section**,
**app definition**, **exclusion**, **effective policy**) are defined in
[`CONTEXT.md`](../../CONTEXT.md).

Decided in [E #67](https://github.com/knausj85/Windows-Hinting/issues/67) and its three
amendments, [H #88](https://github.com/knausj85/Windows-Hinting/issues/88),
[J #90](https://github.com/knausj85/Windows-Hinting/issues/90),
[K #91](https://github.com/knausj85/Windows-Hinting/issues/91),
[L #92](https://github.com/knausj85/Windows-Hinting/issues/92) and
[I #89](https://github.com/knausj85/Windows-Hinting/issues/89). File layout and diagnostics form
settled in [F #68](https://github.com/knausj85/Windows-Hinting/issues/68).

## 1. A rules file

```jsonc
{
  "apps": [
    { "name": "Slack", "match": [ { "exe": "slack.exe" }, { "url": "app.slack.com" } ] },
    { "name": "VSCode", "match": [ { "exe": "Code.exe", "not": { "class": "#32770" } } ] }
  ],
  "rules": [
    {
      "name": "Slack panes",
      "app": "Slack",
      "scrollDetection": {
        "expectedRegions": [
          { "controlType": "Group", "className": "p-view_contents--sidebar" },
          { "controlType": "Group", "className": "p-view_contents--primary" }
        ]
      }
    },
    {
      "name": "PowerPoint images",
      "exe": "POWERPNT.EXE",
      "click": { "controlTypes": { "add": ["Image", "HeaderItem"] } },
      "scrollActuation": { "rungOrder": "LargeFirst" }
    }
  ]
}
```

A file is an object with optional `apps` and `rules` arrays. A file that is a bare array is read as
`{ "rules": [...] }`, so a phase-07 rules file still loads.

## 2. Matching a window

### 2.1 Matcher fields

A rule's matcher is the matcher fields set directly on it. All fields given must match (AND).

| Field | Value read from the window | Cost |
|---|---|---|
| `exe` | process image name; a trailing `.exe` is optional on both sides | cheap |
| `class` | top-level window class name | cheap |
| `title` | window title | cheap |
| `appId` | AppUserModelID: `SHGetPropertyStoreForWindow` → `PKEY_AppUserModel_ID`, falling back to `GetApplicationUserModelId(process)` | cheap (~0.01 ms) |
| `url` | the page URL: UIA `Value` of the top-level web Document (`FindFirst(Descendants, AutomationId = "RootWebArea")` on Chromium; the outermost `Document` on Firefox) | ~10–50 ms, lazy |
| `app` | the name of an app definition (§2.3) | per its alternative |
| `not` | an exclusion (§2.4) | — |

`FrameworkId` is **not** a matcher field: Chromium, Electron and WebView2 window roots all report
`Win32` (I #89).

**The `url` read is lazy.** It happens only when a rule or alternative has `url`, all its cheap
fields already match, and the window class is a web host (`Chrome_WidgetWin_1` or
`MozillaWindowClass`; the list is a constant that can grow). It is read once per scan and never
cached per window (single-page apps and tab switches change it). No document or an empty value
means the field doesn't match; there is no fallback to the title. Full URLs are never logged.

### 2.2 String syntax

Every text field except `url` takes one syntax (L #92):

- **plain** — exact match, ignoring case;
- **`/regex/flags`** — a .NET regex searched in the value; flag `i` ignores case. Compiled with a
  100 ms match timeout.

Today's `TitleMatchMode.Contains` is retired; a `contains` title is written as `/text/i`.

`url` (J #90) takes either:

- **a plain pattern** `[scheme://][*.]host[:port][/path-prefix][/*]`, parsed with `System.Uri` and
  compared part by part, never as a raw string prefix. A bare host is that host exactly; `*.host`
  is the host and any subdomain (on a `.` boundary). No scheme = http or https; no port = any port.
  The path is a prefix that stops at a `/` boundary and ignores case. Query and `#fragment` are
  ignored. A trailing dot is stripped and hosts are compared in punycode form;
- **a regex** `/.../` or `/.../i`, searched in the full URL including query and fragment.

A pattern or regex that doesn't parse is a load error for that rule (§4.3), never match-all.

| Want | `url` |
|---|---|
| All of Gmail | `mail.google.com` |
| All of google.com | `*.google.com` |
| One GitHub repo | `github.com/knausj85/Windows-Hinting` |
| VS Code workbench | `vscode-file://vscode-app/` |
| A Gmail label (fragment) | `/^https://mail\.google\.com/mail/u/\d+/#label/Work/` |

### 2.3 App definitions

An app definition names one application across the ways it appears:
`{ "name": "...", "match": [ alternative, ... ] }`. Each alternative is an AND of matcher fields
(it may have its own `not`); the alternatives are OR'd. This is the **only** place OR exists; rules
stay AND-only.

- A rule refers to one with `"app": "<name>"`, alongside any other matcher fields.
- App names are global across all files. A user app with the same name as a built-in (or another
  user app) **adds** alternatives.
- An `app` reference to an unknown name is a load error for that rule.

### 2.4 Exclusions

`not` is an object (or an array of objects) of matcher fields. If every field of any one of them
matches, the rule or alternative does not apply. Exclusions never count toward specificity. Typical
use: an app's own Open/Save dialogs (`"not": { "class": "#32770" }`).

## 3. Effective policy

### 3.1 Sections

| Section | Fields | Consumer |
|---|---|---|
| `root` | `strategy` (a named compiled root strategy: today's `RootStrategy` values) | every hint source, click and scroll |
| `click` | `controlTypes { add, remove }` over today's 17 clickable control types | the click scanner |
| `scrollDetection` | see [design.md §3.5](design.md#35-scrolldetection-fields) | `ScrollRegionSource` |
| `scrollActuation` | see [design.md §4.5](design.md#45-scrollactuation-fields) | `IScrollActuator` |

Control types are written by their programmatic names (`Image`, `HeaderItem`, `Group` …), parsed to
the UIA IDs. Lists are add/remove deltas against the defaults, never restatements. Later efforts
add sections (`menu`, `settle`) without touching the matcher, precedence or file format. An unknown
section is kept and reported (§4.3), not an error.

### 3.2 Precedence

For each section separately, the effective section is the one from the **most specific** matching
rule that has that section. Fields that winning section leaves out take the **built-in defaults**,
not values from less specific rules: there is no merging within a section. That per-section rule is
what lets a Chromium-wide actuation rule and a Slack detection rule both apply to Slack.

**Specificity** = the number of matcher fields set. An `app` reference counts as the field count of
the alternative that matched. `not` counts zero. **Ties**, in order:

1. if both rules have `url`, the more specific URL wins: plain over regex, exact host over `*.`,
   more host labels, port given, more path segments;
2. a user rule beats a built-in rule;
3. file order: built-ins in embedded order, then user files in file-name order, then position in
   the file.

A user rule with the same `name` as a built-in **replaces** that built-in completely.

### 3.3 Selectors

Selectors identify elements *inside* a matched window (`scrollDetection.expectedRegions`; later
click includes). They are never part of the matcher.

| Field | Matches |
|---|---|
| `automationId` | `AutomationId`, exact |
| `controlType` | control type, by programmatic name |
| `className` | **one** whitespace-separated token of `ClassName`, ignoring case (Chromium puts the DOM class list there) |
| `name` | `Name`, using the string syntax. Built-in rules that use it are English-only, and say so |
| `region` | `"parent"`: the region is the matched element's parent, one per parent, clipped to the window |
| `not` | a selector; the element is skipped if it matches |

All fields given must match. `LocalizedControlType` is never a selector field (it is localized).

## 4. Storage

### 4.1 Modernize (phase 07)

`rules/` directory beside `preferences.json`; each file as in §1. Built-in rules ship embedded and
are never written to disk. The watcher → reload → validate → `OptionsApplied` path applies rule
edits like any other preference change. There is no rules editor UI; rules are hand-edited and
documented in the README.

### 4.2 Bridge to `main`

Until phase 07, rules stay in the existing settings JSON (`HintOverlayOptions`):

- `WindowRules` items gain the new matcher fields (`App`, `AppId`, `Url`, `Not`) and optional
  section objects (`Root`, `Click`, `ScrollDetection`, `ScrollActuation`). Today's `Strategy` is
  read as the `root` section's strategy. Use the file's existing property casing.
- A new `WindowApps` list holds app definitions.
- On load, `TitleMatchMode.Contains` titles convert to `/escaped-text/i`.
- Built-ins live in `WindowRuleRegistry.GetDefaultRules()` (the existing `Windows Search` rule plus
  [`builtin-rules.json`](builtin-rules.json)), and built-in app definitions beside them.
- `WindowRuleRegistry.ResolveStrategy` becomes effective-policy resolution (§3.2). Today's
  first-match-wins becomes most-specific-wins. With one built-in rule this changes nothing for
  existing users unless they have overlapping user rules; call it out in the release notes.
- **The Preferences "Window Rules" grid keeps editing only the matcher and strategy, and must keep
  every other field when it saves** (round-trip the sections, `App`, `AppId`, `Url`, `Not`).
- Phase 07's migration moves `WindowRules` + `WindowApps` into `rules/` files.

### 4.3 Load errors

Preserve + notify, per rule: a rule with an unparseable pattern, a bad regex, or an unknown `app` is
dropped and listed in the tray notification; the rest load. An unknown section or field is kept on
disk, ignored, and reported. Fields whose names start with `_` are comments and are ignored
silently.

## 5. Diagnostics

Two read-only tray-menu items (L #92; form settled in F #68). They are not an editor.

Both act on the **last active window**: the most recent foreground window that is not one of this
app's windows and not the taskbar or tray, tracked with `EVENT_SYSTEM_FOREGROUND`. Clicking the tray
makes the taskbar foreground, so "the current foreground window" would always be the taskbar.

- **Copy window identity** — puts a ready-to-paste matcher on the clipboard:
  `{ "exe": ..., "class": ..., "title": ..., "appId": ..., "url": ... }`, with the URL reduced to
  scheme, host and path (no query or fragment). Fields that read empty are left out. A tray balloon
  confirms with the window's title.
- **Explain window rules** — writes a plain-text report to
  `%AppData%\Windows-Hinting\diagnostics\explain-<timestamp>.txt` and opens it. It lists the window
  identity; every rule and app alternative considered, with matched / excluded / not matched and its
  specificity; the winning rule for each section and the tie-break that decided it; and the
  resulting effective policy as JSON.

## 6. Built-in rules

[`builtin-rules.json`](builtin-rules.json) holds the draft built-ins from the I #89 probe, rewritten
in this schema. Unverified entries carry a `_note`; the builder confirms them in slice 04.
