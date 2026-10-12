# URL match syntax for window rules

Research for issue #90 (J; feeds F #68). Date: 2026-10-12.

## Question

H (#88) decided where the URL comes from: the UIA `Value` of the window's top-level web
Document, read at scan time
([findings](https://github.com/knausj85/Windows-Hinting/blob/research/web-page-url-matching/docs/research/web-page-url-matching.md)).
This ticket picks the **syntax and semantics** of the window rule's `url` matcher field.
The operator wants partial URLs to work as written: `https://mail.google.com/` should match
every Gmail page. Rules are hand-edited JSON. "Most specific rule wins, per section", and
specificity is the number of matcher fields set (E #67).

## Recommendation (TL;DR)

**One `url` string field. A literal pattern is a component-wise prefix, parsed with
`System.Uri`. A value wrapped in `/.../` is a regex escape hatch.** No dependency.

```text
literal:  [scheme://][*.]host[:port][/path-prefix][/*]
regex:    /<.NET regex>/[i]          (Talon-style, searched in the full URL)
```

| Part | Rule |
|---|---|
| scheme | Optional. Omitted = `http` or `https`. Any other scheme (`vscode-file`, `edge`, `file`) must be written. Case-insensitive. |
| host | Required (except `file:`, which has no host). **Exact host** by default, like Talon `browser.host: github.com`. **`*.` prefix = that domain or any subdomain** (Chrome match-pattern meaning). No other wildcards. Compared as lower-case punycode (`Uri.IdnHost`), trailing dot stripped. |
| port | Optional. Omitted = any port. Given = must equal the effective port (`:443` on https equals no port). |
| path | Optional. **Prefix on a segment boundary**, case-insensitive: `/mail` matches `/mail`, `/mail/`, `/mail/u/0`, not `/mailbox`. A trailing `/*` is accepted and means the same as `/` (so copied extension patterns work). |
| query, fragment | **Ignored** in the page URL. A literal pattern containing `?` or `#` is a load error; use a regex. |
| userinfo | Ignored. The host is the parsed host, never a substring. |

Examples:

| Want | `url` value | Matches | Does not match |
|---|---|---|---|
| All of Gmail | `https://mail.google.com/` or `mail.google.com` | `https://mail.google.com/mail/u/0/#inbox` | `https://mail.google.com.evil.com/`, `https://mail.google.com@evil.com/` |
| All of google.com | `*.google.com` | `google.com`, `mail.google.com`, `docs.google.com/...` | `evilgoogle.com`, `google.co.uk` |
| One GitHub repo | `github.com/knausj85/Windows-Hinting` | `.../Windows-Hinting`, `.../Windows-Hinting/issues/90`, `.../windows-hinting/pulls` | `.../Windows-Hinting-fork` |
| VS Code workbench (Electron) | `vscode-file://vscode-app/` | `vscode-file://vscode-app/c:/.../workbench.html` | `https://vscode.dev/` |
| Edge settings | `edge://settings` | `edge://settings/profiles` | `edge://flags` |
| Local dev server | `localhost:3000` | `http://localhost:3000/app` | `http://localhost:8080/` |
| Gmail label view (fragment) | `/^https://mail\.google\.com/mail/u/\d+/#label/Work/` | (regex sees fragment and query) | |

**Specificity:** `url` counts as **one** field, like `title`. Do not split host and path
into two fields. **Among rules tied on field count, a more specific URL pattern wins** before
the existing tie-breaks (user over built-in, then file order). URL specificity, compared in
order: literal beats regex; exact host beats `*.` host; more host labels; port given beats
not; more path segments. This mirrors the Chrome/Edge URL-list rule ("longest host, then
longest path").

## Evidence: prior art

### WHATWG URLPattern (the web standard)

- Component-wise patterns over protocol, username, password, hostname, port, pathname,
  search, hash, with `:name` groups, `*` wildcards, regex groups and `?`/`+`/`*` modifiers.
  Source: https://urlpattern.spec.whatwg.org/
- **Paths are exact unless a wildcard is written.** MDN: `new URLPattern({ pathname: "/books" })`
  tests true for `/books` and false for `/books/123`; trailing slashes must match unless made
  optional with `{/}?`. So `https://mail.google.com/` as a URLPattern matches only the root
  page, the opposite of what the operator wants. "Domain plus subdomains" needs
  `{:subdomain.}*example.com` (or `{*.}?`). Source:
  https://developer.mozilla.org/en-US/docs/Web/API/URL_Pattern_API
- Constructor strings fill omitted components with `*`, except the port, which becomes `""`
  (default port) when a hostname is given. Source:
  https://developer.mozilla.org/en-US/docs/Web/API/URLPattern/URLPattern
- The spec defines no "which pattern is more specific" comparison, so it would not answer
  the tie-break question.
- A .NET port exists: `Meziantou.Framework.Uri` (`UrlPattern`, MIT, net10.0, v2.0.14
  2026-10-10). Source: https://www.nuget.org/packages/Meziantou.Framework.Uri. The app is
  `net10.0-windows`, so it would fit.
- **Verdict:** powerful and standard, but the wrong default for hand-written partial URLs, and
  the subdomain syntax is hard to type by hand. Rejected as the main syntax. A
  URLPattern-compatible mode could be added later if a real need appears.

### Browser-extension match patterns and userscript `@match` / `@include`

- Grammar `<scheme>://<host>/<path>`. Scheme `*` = http or https only. Host `*` may only be
  first and must be followed by `.` or `/` (`*.example.com`). Omitted port = any port.
  `/*` = all paths, `/foo*` = paths starting with `foo`. Source:
  https://developer.chrome.com/docs/extensions/develop/concepts/match-patterns
- Tampermonkey `@match` follows Chrome's grammar. `@include` is a looser glob. When it
  contains `://`, Tampermonkey treats the part between `://` and `/` as the host, so that
  `*` cannot run into the path. That is a guard against the `example.com.evil.com` class of
  mistake. Source: https://www.tampermonkey.net/documentation.php?q=include#meta:include
- **Taken:** the `*.` host prefix and its meaning (the domain and any subdomain), omitted port
  = any, tolerance of a trailing `/*`. **Not taken:** the path is a glob that must match the
  whole path, so `https://mail.google.com/` would not match Gmail pages without `*`.

### Vimium / Tridactyl / Surfingkeys (keyboard-hinting extensions)

- **Vimium** exclusion rules: `new RegExp("^" + pattern.replace(/\*/g, ".*") + "$")`. That is
  an anchored whole-URL glob. **Dots are not escaped**, so `mail.google.com` also matches
  `mailXgoogle.com`, and `*` can cross the host into the path. When several rules match, their
  pass-keys are unioned. There is no "most specific" rule. Source:
  https://github.com/philc/vimium/blob/master/background_scripts/exclusions.js
- **Tridactyl** `seturl` / `bindurl`: "The URL regex pattern ... e.g. `^https://en.wikipedia.org`
  or `/index.html`". They are unanchored regexes, and the docs warn they are "regex-like, not
  glob-like". Conflicts are settled by an explicit per-pattern `priority` (default 10).
  Source: https://github.com/tridactyl/tridactyl/blob/master/src/excmds.ts (doc comment on
  `seturl`)
- **Surfingkeys**: `settings.blocklistPattern` is "A regex to match the sites", and
  site-specific config is written as JS conditions on `window.location.origin`. Source:
  https://github.com/brookhong/Surfingkeys/blob/master/README.md
- **Lesson:** this domain fell back to raw regex or glob-over-the-whole-string. Both are
  easy to get subtly wrong (Vimium's unescaped dot). None of them has an automatic
  specificity rule. The two that need one either union the results or ask the user for a
  number.

### Talon (`browser.host`, the operator's mental model)

- Context header values are either a literal (whole-string equality) or `/regex/flags`,
  which runs as Python `re.search`. Source: https://talon.wiki/Customization/talon-files
- talonhub/community CONTRIBUTING P02: "Prefer exact host strings such as
  `browser.host: github.com` when possible. When a regex is needed to match subdomains ...
  anchor it ... `browser.host: /^(.+\.)?example\.com$/`". Source:
  https://github.com/talonhub/community/blob/main/CONTRIBUTING.md
- Real usage in community: `browser.host: github.com`, `browser.host: app.slack.com`,
  `browser.host: /atlassian\.(com|net|cloud)$/`. On Windows the host comes from a URL in the
  title bar (needs a URL-in-title extension). Source:
  https://github.com/talonhub/community/blob/main/apps/README.md
- Talon specificity: "A useful approximation of these heuristics is that contexts with more
  matching rules will win". This is the same as E #67's field count. Source:
  https://github.com/TalonCommunity/Wiki/blob/main/docs/Customization/Talon%20Framework/modules_and_contexts.md
- **Taken:** a bare host means an exact host, a `/.../` value means a regex, and the URL is
  one matcher (one count).

### uBlock Origin, Stylus / `@document`, Dark Reader

- uBlock `||example.com^` = example.com and all its subdomains, with `^` as a separator so
  `example.com.evil.org` does not match. `google.*` "entity" = any public suffix. Source:
  https://github.com/gorhill/uBlock/wiki/Static-filter-syntax
- CSS `@document` (used by Stylus): `url()` exact, `url-prefix()` starts-with, `domain()` =
  the domain or a subdomain, `regexp()` must match the entire URL. Source:
  https://developer.mozilla.org/en-US/docs/Web/CSS/@document
- **Lesson:** every mature system ends up with the same three tools: domain-with-subdomains,
  prefix, and regex. Raw `url-prefix()` string prefixing is the classic trap:
  `url-prefix("https://mail.google.com")` matches `https://mail.google.com.evil.com/`. That
  is why the recommendation does component-wise matching, not string prefixing.
- Dark Reader's site list was not checked against a primary source. Its observable behaviour
  (host lists with `*` wildcards) adds nothing beyond the above.

### Chrome / Edge enterprise URL-list policy format

- `[scheme://][.]host[:port][/path][@query]`. The scheme is optional and case-insensitive.
  A bare host **matches subdomains too**, and a leading `.` turns that off. The path is
  case-sensitive. "The filter selected for a URL is the most specific match found":
  longest host, then longest path, then most query tokens. Sources:
  https://support.google.com/chrome/a/answer/9942583 ,
  https://learn.microsoft.com/en-us/DeployEdge/edge-learnmmore-url-list-filter-format
- **Taken:** a component-wise prefix with an optional scheme, and a built-in "most
  specific wins" ordering (longest host, then longest path). This is the only prior art
  with an automatic specificity rule. It is written by admins by hand, at scale.
- **Changed:** Chrome's bare host = subdomains too. Here a bare host is exact (matching
  Talon and the extension grammar), and `*.` asks for subdomains. That is less surprising
  for `mail.google.com` and is visible in the text.

## Normalization (both sides go through the same code)

Pattern and page URL are both parsed with `Uri.TryCreate(..., UriKind.Absolute)`. For a
literal pattern without a scheme, prepend `https://` for parsing only, and remember
"scheme = http|https". Checked locally (pwsh 7.6 / .NET 10.0.9):

| Input | `System.Uri` result | Handling |
|---|---|---|
| `HTTPS://Mail.Google.COM/mail/u/0/?tab=rm#inbox` | scheme `https`, Host `mail.google.com`, path `/mail/u/0/`, query and fragment split out | Scheme and host lower-cased by Uri. Query/fragment ignored. |
| `https://mail.google.com@evil.com/mail/` | Host `evil.com` | Userinfo cannot spoof the host. |
| `https://mail.google.com.evil.com/` | Host `mail.google.com.evil.com` | Exact/`*.` host compare (with a `.` boundary) rejects it. |
| `https://mail.google.com./mail/` | Host `mail.google.com.` (dot kept) | **Strip one trailing dot** before comparing. |
| `https://münchen.de/x` | Host `münchen.de`, IdnHost `xn--mnchen-3ya.de` | **Compare `IdnHost`** on both sides, so Unicode and punycode patterns agree. |
| `https://mail.google.com:443/mail` | Port 443, `IsDefaultPort` true | Compare effective `Port`. |
| `vscode-file://vscode-app/c:/...workbench.html` (and `VSCode-App` upper case) | scheme `vscode-file`, Host `vscode-app` (lower-cased), port -1 | Non-http schemes work unchanged. |
| `edge://settings/profiles` | Host `settings`, path `/profiles` | `edge://settings` works as a pattern. |
| `file:///C:/Users/x/a.html` | Host empty, AbsolutePath `C:/Users/x/a.html` (no leading `/`) | Allowed with an empty host. Path compare must not assume a leading `/`. |
| `about:blank` | Host empty, path `blank` | Matchable only by regex (no `//`). |
| `https://example.com/caf%C3%A9` and `/café` | Both give AbsolutePath `/caf%C3%A9` | Compare `AbsolutePath` (escaped) on both sides. |
| `https://[::1]:8080/a` | Host `[::1]`, IdnHost `::1` | `*.` is a load error on IP hosts. |

Path match: `patternPath` is the pattern's `AbsolutePath` with a trailing `/*` turned into `/`.
`"/"` or empty matches everything. Otherwise it matches when
`pagePath.StartsWith(patternPath, OrdinalIgnoreCase)` **and** (the pattern ends in `/`, or the
page path is the same length, or the next page character is `/`). Case-insensitive on
purpose: these rules guard no security boundary, people type repo and path names by hand,
and Chrome's case-sensitive paths would make `github.com/knausj85/windows-hinting` silently
fail. (This differs from the Chrome policy format, and is the one deliberate deviation.)

Regex form: a value that starts with `/` and ends with `/` or `/i`. A literal pattern never
starts with `/`, so this is unambiguous. It is a .NET `Regex` run with `IsMatch` (search,
not anchored, like Talon and Tridactyl) over the **raw** UIA value, including query and
fragment. Compile once at load, `RegexOptions.CultureInvariant` (+ `IgnoreCase` for `/i`),
and set a match **timeout** (or use `RegexOptions.NonBacktracking`) so a bad user regex
cannot stall a scan. Docs should tell users to anchor and escape dots (the Talon P02 advice).

Load-time validation (via phase-07's rule-file diagnostics): reject `*` anywhere except a
leading `*.` host label and a trailing `/*`; reject `?` and `#` in literals; reject an
unparseable pattern; reject `*.` on an IP host. A bad pattern disables that rule and
reports it. It never matches everything.

## Security and correctness pitfalls, and how they are handled

| Pitfall | Handling |
|---|---|
| `mail.google.com.evil.com` (string-prefix trap of `url-prefix` and naive `StartsWith`) | Hosts compared as parsed components: equality, or `*.` suffix with a `.` boundary. |
| `https://mail.google.com@evil.com/` (userinfo) | Uri puts it in UserInfo; Host = `evil.com`. |
| `evilgoogle.com` vs `*.google.com` | The suffix must be `.google.com` or equal `google.com`. |
| Unescaped regex dot (Vimium's `mail.google.com` matching `mailXgoogle.com`) | Literals are never turned into regex. Regex is opt-in and visibly `/.../`. |
| `#fragment` / `?query` (Gmail puts the view in the fragment; #88 saw `#inbox` in the Value) | Ignored by literals; available to regex. |
| Trailing slash (`/mail` vs `/mail/`) | Segment-boundary prefix: both match each other's pages. `github.com/foo` does not match `github.com/foobar`. |
| Case | Scheme and host are always case-insensitive (Uri lower-cases them). The path is case-insensitive by choice. |
| IDN / punycode | `IdnHost` on both sides. |
| Port | Effective-port compare. An omitted port matches any. |
| Trailing-dot FQDN | Stripped. |
| Non-http schemes (`vscode-file`, `edge`, `chrome-extension`, `file`) | Parsed by the same code. The scheme must be written for them, and a scheme-less pattern never matches them. |
| ReDoS from user regex | Match timeout / NonBacktracking. |
| Privacy | Unchanged from #88: never log full URLs at info level. |

## Specificity: why one field, plus a tie-break

- **One field, not host + path as two.** E #67 counts **matcher fields**. They answer "which
  window is this?" in kinds (exe, class, title, framework, page). Counting host and path
  separately would give a URL-only rule `github.com/knausj85/Windows-Hinting` a count of 2.
  That ties `exe=msedge + url=github.com`, which checks strictly more about the window, only
  because a path was written. The count would depend on how a pattern is spelled, not on
  how many independent facts about the window it checks. Talon counts `browser.host`
  as one matcher too.
- **But a longer pattern must win among URL rules.** The common case is two rules that
  differ only in URL: a `*.google.com` default and a `mail.google.com` override, each also
  pinned to `FrameworkId=Chrome`. They tie on count, and E #67's tie-breaks (user over
  built-in, then file order) would let file order decide. That is wrong and fragile. Chrome
  and Edge solve the same problem with "longest host, then longest path"
  (https://support.google.com/chrome/a/answer/9942583).
- **Proposed order** (an amendment for F #68 to fold into E #67's precedence):
  1. matcher field count (unchanged);
  2. **URL specificity**, only when both rules have `url`: literal > regex; exact host > `*.`
     host; more host labels; port given > not; more path segments;
  3. user rule > built-in; 4. file order.
- A rule with `url` already outranks the same rule without it (+1 field). Two regex rules tie
  at step 2 and fall to file order, as in Tridactyl, with no attempt to measure a regex.
- This applies per section, like everything else in E #67: the winning rule is chosen
  separately for each section.

## Implementation cost (pure Core C#)

About 120–180 lines plus tests: a `UrlPattern` value type (`Parse(string) -> UrlPattern |
error`, `IsMatch(string rawUrl)`, `Specificity` as a comparable tuple), built on
`Uri.TryCreate`, `IdnHost`, `Port`, `AbsolutePath` and `Regex`. No package. Table-driven
tests can reuse the rows above. The page URL is parsed once per scan and memoized next to
#88's single Document read.

## Alternatives considered

- **URLPattern via Meziantou.Framework.Uri:** standard, but its exact-path default breaks
  the operator's core case, `{*.}?` is hostile to hand-editing, it has no specificity
  comparison, and it adds a dependency. Rejected for v1.
- **#88's `UrlMatchMode` enum (Host | Prefix | Contains | Regex):** two fields to edit.
  `Prefix` as a raw string prefix has the `.evil.com` trap. `Contains` is too loose to be
  safe and adds nothing that regex does not. The one-string design gives Host and Prefix
  from the pattern's shape, and Regex from the slashes.
- **Host-only (Talon-like):** cannot target a GitHub repo or `edge://settings`.
- **Whole-URL glob (Vimium/`@include`):** a trailing `*` is needed, `*` crosses component
  boundaries, and specificity is unclear.
- **Chrome policy's bare host = subdomains:** surprising for `mail.google.com` and
  inconsistent with Talon. Use the explicit `*.` instead.

## Uncertain / not verified

- The Talon wiki page fetched documents literal/regex matching and `re.search`, but no
  primary Talon page for `browser.url` / `browser.path` matchers was found. Only
  `browser.host` is confirmed (community usage and CONTRIBUTING). Talon's core is closed
  source, so how it derives `browser.host` (lower-casing, port) was not checked.
- Whether Chrome's policy path prefix respects segment boundaries (`/path` vs `/pathology`)
  was not verified. The recommendation does not depend on it.
- Dark Reader and Stylus's own docs were not read. `@document` semantics come from MDN.
- Whether real users will want several hosts in one rule (`google.com` + `google.co.uk`).
  v1 says: two rules, or a regex. An array value could come later without changing the
  semantics.
- Hash-routed SPAs (Gmail labels) are reachable only through regex. If that becomes common,
  an opt-in to include the fragment could be added.
