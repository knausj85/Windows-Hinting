# Web-page matching: reading the current page URL for window rules

Research for issue #88 (child of #62, feeds F #68). Date: 2026-10-12.

## Question

Window rules match on exe / window class / title / UIA `FrameworkId`. That identifies the
*browser*, not the *site*. Is there a dependable way to learn which web page (e.g. Gmail) is
showing in an Edge / Chrome / Firefox / Electron window, so a rule can match a site?

## Recommendation (TL;DR)

**GO.** Add a `Url` matcher field to `WindowRule` (with a match mode, like `TitleMatchMode`).

- **Signal source (primary):** the UIA `Value` of the window's top-level web **Document**
  element. Chromium and Firefox both deliberately put the document URL there.
  Find it with one `FindFirst(TreeScope.Descendants, AutomationId == "RootWebArea")`
  (Chromium) or `ControlType == Document` (Firefox / generic), with a `CacheRequest` for
  `ValuePattern.ValueProperty`. That is one cross-process round trip, **~14 ms** on Edge
  in a local check.
- **Fallback order:**
  1. Document `Value` (above). Required for the rule to match.
  2. If no document is found, or the value is empty, the `Url` matcher **does not match**
     (it does not fall through to the title). A user who wants title matching already has
     `WindowTitle` + `TitleMatchMode.Contains`. That remains the documented fallback for
     browsers or apps that do not expose a document URL.
  - Not recommended as fallbacks: the omnibox/address-bar `Edit` (localized name, elided
    display text, absent in app windows), COM/DDE, browser extensions, DevTools protocol
    (see below).
- **Match semantics:** match on the **host plus optional path prefix**, not on raw string
  equality. Suggested modes: `Host` (e.g. `mail.google.com`), `Prefix`
  (e.g. `https://mail.google.com/mail/`), `Contains`, and optionally `Regex`. Parse with
  `System.Uri`; ignore the fragment for `Host`/`Prefix`.
- **Gate it:** evaluate the URL matcher only when the cheaper fields already matched a
  browser-like window (exe / `Chrome_WidgetWin_1` class / `FrameworkId`). That way
  non-browser windows never pay the extra UIA call. Count it as one more field under
  "most specific wins, per section".
- **Read it at scan time; don't cache it per HWND.** SPAs change URL without changing the
  window. Tab switches change the active document under the same HWND.

## Evidence

### 1. Chromium (Edge, Chrome, Chromium PWAs, Electron): Document Value = URL (primary source)

`ui/accessibility/platform/ax_platform_node_win.cc` (Chromium `main`, fetched 2026-10-12):

```cpp
BSTR AXPlatformNodeWin::GetValueAttributeAsBstr(AXPlatformNodeWin* target) {
  if (ui::IsPlatformDocument(target->GetRole())) {
    std::wstring url =
        base::UTF8ToWide(target->GetDelegate()->GetTreeData().url);
    ...
```

`GetValueAttributeAsBstr` backs both `IValueProvider::get_Value` (UIA) and
`IAccessible::get_accValue` (MSAA/IA2) in the same file. So the root document's Value is
the tree's URL in both API families.

The same file gives the root a fixed, locale-independent AutomationId:

```cpp
case UIA_AutomationIdPropertyId: {
  // The kRootWebArea is the only element in a web page that cannot have
  // an author provided id. In this case, we return a constant string
  // that needs to be the same for all locales.
  std::u16string automation_id = GetRole() == ax::mojom::Role::kRootWebArea
                                     ? u"RootWebArea" : ...
```

So `AutomationId == "RootWebArea"` is a stable, cheap find condition for Chromium.
Source: https://chromium.googlesource.com/chromium/src/+/main/ui/accessibility/platform/ax_platform_node_win.cc

Nuance: the `UIA_ValueValuePropertyId` branch of `GetPropertyValue` returns only
`kValue`/`kAriaValueText`, not the URL. Even so, the property read and a **cached**
property read (`CacheRequest` + `ValuePattern.ValueProperty`) both returned the URL in the
local check (section 6). UIA core falls back to the pattern getter when the provider
leaves the property empty. Keep this in mind if a future Chromium change starts returning
an empty-string `kValue` for documents. The direct `IUIAutomationValuePattern.CurrentValue`
call is the belt-and-braces path.

### 2. Firefox: Document Value = URL in both native UIA and MSAA (primary source)

`accessible/windows/uia/uiaRawElmProvider.cpp` (mozilla-firefox `main`), `get_Value`:

```cpp
  if (value.IsEmpty() && acc->IsDoc()) {
    // Exposing the URl via the Value pattern doesn't seem to be documented
    // anywhere. However, Chromium does it, as does the IA2 -> UIA proxy.
    nsAccUtils::DocumentURL(acc, value);
  }
```

`HasValuePattern()` in the same file returns true for `acc->IsDoc()`.
`accessible/windows/msaa/MsaaDocAccessible.cpp` keeps the same behavior for MSAA:
"For backwards-compat, we still support old MSAA hack to provide URL in accValue". It
applies to roles `DOCUMENT` / `APPLICATION`.

Firefox's native UIA is controlled by `accessibility.uia.enable` in
`modules/libpref/init/StaticPrefList.yaml`. The value is currently `2` = "Enable unless
incompatible accessibility clients are detected". When it is off, Windows' MSAA/IA2→UIA
proxy maps `accValue` to the Value pattern (the Mozilla comment above notes the proxy does
this). So either path yields the URL.
Sources: https://github.com/mozilla-firefox/firefox/blob/main/accessible/windows/uia/uiaRawElmProvider.cpp ,
https://github.com/mozilla-firefox/firefox/blob/main/accessible/windows/msaa/MsaaDocAccessible.cpp ,
https://github.com/mozilla-firefox/firefox/blob/main/modules/libpref/init/StaticPrefList.yaml

Firefox has no `RootWebArea` AutomationId. Use `ControlType == Document` (FindFirst is
pre-order, so it returns the outermost document before any iframe document). Not yet
checked hands-on: whether Firefox's outermost UIA `Document` is the content document or a
chrome (browser-UI) document. Probe this before shipping Firefox support. The fallback is
to take the first Document whose Value starts with a web scheme.

### 3. Window title: works only by accident

- Titles carry the page `<title>`, which sites change freely: unread counts
  ("Inbox (63,199)"), localized strings, account names, notification prefixes. Some SPAs
  have a generic or empty title.
- The browser appends its own decoration ("- Personal - Microsoft Edge", "and 1 more page"),
  which varies with the profile name and the number of tabs.
- On the operator's machine the titles already contain the URL
  (`... - Gmail - https://mail.google.com/mail/u/0/#inbox`). That comes from a
  URL-in-title browser **extension** (the "A |"/"B |" prefixes show the same thing), not
  from the browser. It is per-user and per-browser, and can't be assumed.
- Verdict: keep `TitleMatchMode.Contains` as the documented fallback. It is not a
  dependable site identity.

### 4. COM / shell / DDE: no modern browser coverage

- `ShellWindows` / `IWebBrowser2` enumerate **File Explorer and Internet Explorer** windows
  (the repo's own Explorer-path code uses this). Edge, Chrome and Firefox do not register
  there. IE is retired. Ref: https://learn.microsoft.com/en-us/windows/win32/shell/shellwindows
- DDE `WWW_GetWindowInfo` is a Netscape-era interface. It was not verified against current
  sources in this pass. Chromium has no DDE server for it, and it would not cover Electron
  either way. Treat it as dead.

### 5. Browser-side (extension + native messaging, DevTools protocol): not acceptable as a dependency

- **Extension / native messaging:** reliable and gives the exact active-tab URL, but needs
  a per-browser extension install (separate store listings for Chrome/Edge/Firefox), a
  native-messaging host registration, and an always-on IPC channel. It also hands
  browsing history to a background hinting tool. That is too much to install and too much
  privacy exposure for one matcher field. Users who already run a URL-in-title extension
  get title matching for free, which is enough as an opt-in.
- **DevTools protocol:** needs the browser launched with `--remote-debugging-port`. Since
  **Chrome 136** that switch is ignored for the default user-data directory: it must be
  paired with a non-default `--user-data-dir`. The change was made because attackers used
  it to steal cookies. That rules it out for a user's everyday browser.
  Source: https://developer.chrome.com/blog/remote-debugging-port

### 6. Local empirical check (2026-10-12, Win11 26200, Edge stable, VS Code)

Scripts used `System.Windows.Automation` from Windows PowerShell 5.1.

| Window | Result |
|---|---|
| Edge **app window** (installed Gmail PWA; no tabs, no omnibox) | One `Document`, `AutomationId=RootWebArea`, `FrameworkId=Chrome`, Value = `https://mail.google.com/mail/u/0/#inbox` (the fragment is included) |
| Edge **tabbed window**, 2 tabs (example.com active, iana.org background) | Exactly **one** `Document`, Value = `https://example.com/`. The **background tab exposes no Document**; it shows only as a `TabItem` (name = tab title) |
| VS Code (Electron) | One `Document`, Value = `vscode-file://vscode-app/.../workbench.html` (the app's internal page, not a "site") |
| Timing | `FindFirst(Descendants, AutomationId=RootWebArea)` with `ValuePattern.ValueProperty` cached: **14–15 ms**. `FindAll(ControlType=Document)` over the window: 50–200 ms (avoid it) |

## Edge cases and their handling

- **Background tabs:** not exposed (observed), so the matcher sees only the active tab.
  That is the right semantics for hinting: the active tab is what gets hinted.
- **Incognito / InPrivate / Firefox private:** neither browser's URL-exposure code above
  checks for private mode, so the URL is expected to be exposed. Not tested hands-on. The
  value is used in memory only. **Never log full URLs** at info level: log the host at
  most, or only when verbose diagnostics are on.
- **PWAs / app windows:** work (observed). They also often have a distinct AUMID or
  `--app-id`, but the URL matcher covers them without special casing.
- **Electron apps:** the Document Value is the app's own page URL (`vscode-file://…`,
  `file://…`, or the remote origin for web-wrapper apps such as Slack/Teams). That is
  still useful (e.g. a Slack workspace host), but for most Electron apps exe + title is
  the better key. The `Url` field is not harmful there.
- **iframes:** Chromium and Firefox may expose child documents. FindFirst is pre-order,
  so the outermost (top-level page) document is returned first.
- **Accessibility not yet active:** Chromium/Firefox build their a11y trees lazily when a
  UIA client first asks. Windows-Hinting already queries these trees to hint, so the cost
  is already paid. Right after the browser starts, the first read may find no Document →
  the rule doesn't match (fail-open to less-specific rules). That is acceptable.
- **Navigation timing:** read the value at scan time; the read sees what the tree has at
  that moment. A scan fired mid-navigation may see the previous URL. Not a problem for
  rule selection.
- **Non-web schemes** (`edge://`, `chrome://`, `about:`, `file://`, PDF viewer): exposed
  as-is. Host/prefix matching handles them naturally.

## Proposed shape (for F #68)

```text
WindowRule
  Url           : string?        // null = don't care
  UrlMatchMode  : Host | Prefix | Contains | Regex   (default Host)
```

Evaluation: cheap fields first (exe/class/FrameworkId/title). Then, only for rules with
`Url != null` whose other fields matched, do **one** `FindFirst` for the root document
(Chromium `AutomationId="RootWebArea"`, else `ControlType=Document`) with the Value cached.
Memoize the result for the duration of a single scan, so several URL rules share one read.
If there is no document or the value is empty → no match.

## Uncertain / not verified

- Firefox hands-on: whether the outermost Document is the content document, and the
  timing. Code evidence is strong; observation is missing.
- InPrivate/incognito exposure: inferred from source (no gating found), not observed.
- Whether a future Chromium change could make the cached `ValueValue` property return a
  non-URL `kValue` for documents. The mitigation is to call the pattern getter directly.
- DDE `WWW_GetWindowInfo`: dismissed on age and lack of coverage, not on a source check.
