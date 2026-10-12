# Identifying installed PWAs for window rules

Research for issue #91 (K; feeds F #68). Date: 2026-10-12.

## Question

Installed PWAs run inside the browser's own process (`msedge.exe`, `chrome.exe`), so the
`exe` matcher names the browser, not the app. H (#88) gave us the page URL (UIA `Value` of the
Chromium `RootWebArea` Document) and J (#90) set the `url` syntax. Is the URL enough to match a
PWA, or should window rules gain an **app-identity** matcher field? How is each read, what does
it cost, and how does it differ across Chromium browsers?

## Recommendation (TL;DR)

**URL plus an optional app-identity field: add `appId`, the window's AppUserModelID (AUMID).**

- `url` stays the way to say "this website, wherever it is shown" (a tab or a PWA window).
- `appId` says "this installed app's window, whatever page it is on". It is the identity the
  Windows taskbar itself uses to give each PWA its own button. Chromium sets it per window for
  every PWA, and the read is a window property, not UIA.
- Read order: `SHGetPropertyStoreForWindow(hwnd)` → `PKEY_AppUserModel_ID`. If that is empty,
  fall back to the process's package AUMID (`GetApplicationUserModelId`). The fallback is what
  identifies packaged apps such as Windows Terminal; Edge and Chrome processes have no package
  identity, so the window property is the only source for PWAs.
- Match: exact, case-insensitive, ordinal. Accept the same `/regex/[i]` form as `url` (J #90),
  for example to cover every Chrome profile of one app. It counts as **one** matcher field
  (E #67). It is evaluated before `url`, because it costs microseconds and needs no UIA.
- **Not "app id only".** A URL rule is still the right default for web content: the same Gmail
  tuning should apply in a tab and in the PWA. AUMID values are opaque and differ per browser
  and per profile, so they are a precision tool, not the primary key.
- **F (#68) should add a way to show and copy a window's `appId`**, for example a diagnostic
  line or log entry. Users cannot be expected to compute these strings (see the shortening rule
  below).

## Evidence

### What the AUMID is for a PWA window

**Chromium (Chrome, Brave, Vivaldi, Chromium-based builds without Edge's MSIX layer).**
`BrowserWindowPropertyManager::UpdateWindowProperties`
([source](https://chromium.googlesource.com/chromium/src/+/main/chrome/browser/ui/views/frame/browser_window_property_manager_win.cc))
sets every browser window's AUMID. App windows (`TYPE_APP`, `TYPE_APP_POPUP`) get
`GetAppUserModelIdForApp(app_name, profile_path)`. All other windows get
`GetAppUserModelIdForBrowser(profile_path)`. The value is set through `ui::win::SetAppIdForWindow`
or `SetAppDetailsForWindow`. The comment there says: "Set the app user model id for this
application to that of the application name."

- `app_name` for an installed web app is `"_crx_" + app_id` (`GenerateApplicationNameFromAppId`,
  [web_app_helpers.cc](https://chromium.googlesource.com/chromium/src/+/main/chrome/browser/web_applications/web_app_helpers.cc)).
  For an uninstalled `--app=<url>` window it is `host + "_" + path` (`GenerateApplicationNameFromURL`).
- `app_id` = `crx_file::id_util::GenerateId(SHA256(manifest_id))`. That is a double SHA-256 whose first 16
  bytes are written as hex using the letters `a`–`p`
  ([app_id_helpers.cc](https://chromium.googlesource.com/chromium/src/+/main/chrome/browser/web_applications/app_id_helpers.cc),
  [id_util.cc](https://chromium.googlesource.com/chromium/src/+/main/components/crx_file/id_util.cc)).
  With no manifest `id`, the manifest id is the start URL. So **the app id depends only on the
  manifest id, and is the same in every Chromium browser and on every machine**.
- `GetAppUserModelIdImpl` joins `[base_app_id, app_name, profile_id]` with `.`
  ([shell_integration_win.cc](https://chromium.googlesource.com/chromium/src/+/main/chrome/browser/shell_integration_win.cc)).
  The `profile_id` is omitted for the default profile in the default user-data dir. Otherwise it
  is the sanitized `"<UserDataDirBasename>.<ProfileDir>"`, for example `UserData.Profile1`.
- `ShellUtil::BuildAppUserModelId` caps the whole id at `kMaxAppModelIdLength = 64`. It does this
  by giving each component `(64 - dots) / count` characters and **shortening longer components
  to their first half plus their last half**
  ([shell_util.cc](https://chromium.googlesource.com/chromium/src/+/main/chrome/installer/util/shell_util.cc),
  [util_constants.h](https://chromium.googlesource.com/chromium/src/+/main/chrome/installer/util/util_constants.h)).
  The 37-character `_crx_<id>` component is always shortened, so the full app id is never
  visible in the AUMID.
- `base_app_id`: `Chrome` / `ChromeBeta` / `ChromeDev` / `ChromeCanary`, `Chromium`
  ([google_chrome_install_modes.h](https://chromium.googlesource.com/chromium/src/+/main/chrome/install_static/google_chrome_install_modes.h),
  [chromium_install_modes.h](https://chromium.googlesource.com/chromium/src/+/main/chrome/install_static/chromium_install_modes.h)).
  For Brave it is `Brave`, `BraveBeta`, and so on
  ([brave-core chromium_install_modes.h](https://github.com/brave/brave-core/blob/master/chromium_src/chrome/install_static/chromium_install_modes.h)).
  Vivaldi was not checked.

Worked example: the Slack app id below, computed with the code above. Chrome was not installed
locally, so these values are derived, not observed.

| Browser / profile | AUMID |
|---|---|
| Chrome, default profile | `Chrome._crx_afklomdndmabknlanepfnboeed` |
| Chrome, `Profile 1` | `Chrome._crx_afklonepfnboeed.UserData.Profile1` |
| Brave, default profile | `Brave._crx_afklomdndmabknlanepfnboeed` |

**Edge (observed locally).** Edge goes further. It registers each installed PWA as its own
**hosted-app MSIX package**, and the PWA window's AUMID is that package's
`PackageFamilyName!App`:

| Window | Process | AUMID (window property) |
|---|---|---|
| Edge browser window | `msedge.exe` | `MSEdge` |
| Slack PWA | `msedge.exe` | `app.slack.com-1710C364_366e83yqjt2rw!App` |
| (Gmail PWA package, window not open) | | `mail.google.com-FCFEA3FA_vq8mrer2vmnwe!App` |
| VS Code (Electron, for contrast) | `Code.exe` | none |

The package manifest (`C:\Program Files\WindowsApps\app.slack.com-1710C364_…\AppxManifest.xml`,
read-only) declares the following:

- `<uap10:HostRuntimeDependency Name="Microsoft.MicrosoftEdge.Stable" …/>`
- `<Application Id="App" uap10:HostId="PWA" uap10:Parameters="--app-id=afklomdndmkdpmlaabknlanepfnboeed --ip-edge-aumid=Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe!MSEDGE">`
- An app-extension description with `profile-directory?Default;start-url?https://app.slack.com/client/T7FPSMV8F`.
- `Publisher="CN=app.slack.com, OID.2.25.311729368913984317654407730594956997722=1"`, which is
  unsigned (`SignatureKind: None`).

The `--app-id` is exactly Chromium's algorithm: `GenerateId(SHA256("https://app.slack.com/client/T7FPSMV8F"))`
= `afklomdndmkdpmlaabknlanepfnboeed`, computed locally. Microsoft documents this model:
"We are working with the new Microsoft Edge browser to take advantage of the Hosted App Model for
Progressive Web Apps (PWAs) … a PWA is its own independent app registered to the system even
though it is being hosted by Edge". It also says a dynamically registered hosted package "does
not need to be signed" ([Windows blog, Hosted App Model](https://blogs.windows.com/windowsdeveloper/2020/03/19/hosted-app-model/);
[Learn: hosted apps](https://learn.microsoft.com/en-us/windows/apps/develop/launch/hosted-apps)).
The PFN leaves out the version (Gmail is at `1.0.0.3`), so manifest updates do not change it.

**Store-installed PWAs.** PWABuilder's modern Windows package "uses the Hosted App Model to
install your PWA as a Windows app hosted by Edge". The classic package (before 10.0.19041)
"uses a bootstrapper app which instructs Edge to install and launch the PWA"
([PWABuilder: classic package](https://blog.pwabuilder.com/docs/what-is-a-classic-package/)).
On Windows 11 a Store PWA should therefore be a hosted MSIX like the ones above. Its AUMID would
be the **Store** package's PFN (`<Package ID>_<publisher hash>!App`), which differs from the PFN
of the same site installed from the browser. Not observed: no Store PWA was open.

**Microsoft's AUMID rules.** "A window-level AppUserModelID overrides a process-level
AppUserModelID". Windows in one process that need different taskbar grouping set it with
`SHGetPropertyStoreForWindow`. The limit is 128 characters with no spaces. "Applications cannot
retrieve a system-assigned AppUserModelID" ([Learn: AppUserModelIDs](https://learn.microsoft.com/en-us/windows/win32/shell/appids)).
`GetApplicationUserModelId` needs `PROCESS_QUERY_LIMITED_INFORMATION` and returns
`APPMODEL_ERROR_NO_APPLICATION` when "the process has no application identity"
([Learn](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getapplicationusermodelid)).
Locally, `msedge.exe` and `Code.exe` returned 15703 (no identity). `WindowsTerminal` returned
`Microsoft.WindowsTerminal_8wekyb3d8bbwe!App`, and SystemSettings returned
`windows.immersivecontrolpanel_cw5n1h2txyewy!…`. Its `ApplicationFrameWindow` also carries that
AUMID as a window property, so `appId` also tells apart UWP apps that all run under
`ApplicationFrameHost.exe`.

### Why not the command line

The running Edge main process had only `--profile-directory=Default`. A PWA launch is handed to
the already-running browser, and the app window lives in that process. Renderer processes do not
carry `--app-id` either. So `--app-id` on the command line is visible only when the PWA's launch
started the browser, and it never tells you which window is which. **Rejected.**

### Why not the window class or title

The class is `Chrome_WidgetWin_1` for browser windows, PWA windows and Electron apps alike. The
title is the page or app title and changes with page state (H #88 already rejected it). The
operator's titles contain the URL only because an extension adds it.

### Why not the shortcut or manifest on disk

These are useful only to **look up** what an AUMID means: the Edge `AppxManifest.xml`, Chrome's
Start-menu `.lnk` `System.AppUserModel.ID`, or `Web Applications` in the profile. They are not
needed at match time, because the window already carries the AUMID.

## URL read robustness for PWAs

| Situation | `url` (RootWebArea Value) | `appId` |
|---|---|---|
| Standalone PWA window, page loaded | Works. H observed `https://mail.google.com/mail/u/0/#inbox` on the Edge Gmail PWA. | Works. |
| **Minimized / never-rendered** window | **Fails.** Observed on the minimized Slack PWA: the views-level `ContentsWebView` Document (`AutomationId=view_1051`, no Value pattern) is present but has no children and no `RootWebArea`. | Works (window property). |
| Splash / loading | Empty, `about:blank`, or the pre-redirect URL until the first commit. H's rule: no Document or no value means no match. | Works from window creation (set in the window property manager's create path). |
| **Out-of-scope navigation** (custom-tab toolbar) | The page URL **becomes the out-of-scope URL**, and the rule stops matching. Chromium shows the custom tab bar when `!IsUrlInAppScope(url)`, or when the URL is not https and not a secure origin ([app_browser_controller.cc](https://chromium.googlesource.com/chromium/src/+/main/chrome/browser/ui/web_applications/app_browser_controller.cc) `ShouldShowCustomTabBar`). Edge documents that Store PWAs redirected to a locale domain show this bar ([Learn](https://learn.microsoft.com/en-us/microsoft-edge/progressive-web-apps/how-to/microsoft-store)). The bar is views UI outside `RootWebArea`, so the read itself still returns the page. | Unchanged: still the app. |
| **Tabbed mode** (`display_override: tabbed`) | Only the **active** tab's Document is exposed (H #88). The URL follows the active tab. Tabbed mode is a `has_tab_strip` app browser ([web_app_browser_controller.cc](https://chromium.googlesource.com/chromium/src/+/main/chrome/browser/ui/web_applications/web_app_browser_controller.cc)). | Unchanged: one window, one AUMID. |
| **Window-controls overlay** | Not observed. WCO only changes the frame (the web content extends into the title-bar area). The web content is still the `ContentsWebView` → `RootWebArea`, so no change is expected. | Unchanged. |
| Same site in a normal browser tab | Matches (often desired). | Does **not** match (`MSEdge` / `Chrome`). |

What to take from this: for hinting, the target is the foreground, rendered window, so the URL
read usually works. The real differences are **semantic**:

- `url` follows the content. It leaves the rule when the app navigates out of scope, and it
  matches the same site in a tab.
- `appId` follows the app window. It stays on through out-of-scope pages and loading, and never
  matches a tab.

Both meanings are useful, so both fields exist.

## Cost

- `appId`: `SHGetPropertyStoreForWindow` + `GetValue`. This took **about 0.013 ms** per read
  (200 reads, measured from PowerShell, with or without a value). The first call in a process
  costs about 3–4 ms (COM/shell load). The process fallback is one `OpenProcess` plus
  `GetApplicationUserModelId`, also in the microsecond range. At this cost there is no need to
  cache per HWND. Read it fresh each scan, like the other cheap fields.
- `url`: one UIA `FindFirst(Descendants, AutomationId=RootWebArea)` with the Value cached. That is
  **about 14 ms** (H). On the minimized Slack window the search took 6–15 ms and found nothing.

## Cross-browser summary

| | Edge | Chrome / Brave / Chromium | Firefox |
|---|---|---|---|
| PWA window AUMID | Hosted MSIX `PFN!App` (host-derived name + hash). Browser windows: `MSEdge`. | `<Base>._crx_<shortened app id>[.<profile id>]`. Browser windows: `GetAppUserModelIdForBrowser`. | No PWA install in release Firefox; not researched (out of scope for this ticket). |
| Same app, two profiles | Separate packages, presumably different PFNs (not verified) | Different suffix | — |
| Same app, two browsers | Different | Different base (`Chrome.` vs `Brave.`); same `_crx_` part | — |
| URL read | `RootWebArea` (H) | `RootWebArea` (H) | Document (H; probe I #89) |

So **one `url` rule covers every browser and profile, and an `appId` rule is per browser and
profile**. That is the main reason `url` stays the primary key. A regex `appId`
(`/^Chrome\._crx_afklomdndm/`) can span Chrome profiles if someone needs it.

## Matching design

- New matcher field `appId` (string). Its value is either an exact AUMID (case-insensitive) or
  `/regex/[i]` (same parsing and timeout as `url`, J #90). An empty or invalid value is a load error.
- Specificity: +1, like every field. No new tie-break is proposed. J's URL tie-break applies only
  when both rules have `url`. `exe + appId` versus `exe + url` ties at 2, then falls to
  user-over-built-in and file order. If that comes up in practice, the user adds a field.
- Evaluate it with the cheap fields (exe/class/FrameworkId/appId) before the lazy `url` read.
- If the window has no explicit AUMID and the process has no package identity, there is no value
  and the rule does not match. This mirrors `url`.
- Logging: AUMIDs are not secret but can contain hosts. Log them at debug level only, to stay
  consistent with H's "no full URLs" stance.

## Uncertain / not verified

- **Chrome, Brave and Vivaldi AUMIDs are derived from source, not observed** (none installed).
  Brave's base id is from brave-core source. Vivaldi was not checked.
- **Edge's MSIX registration is not documented by Microsoft as a stable contract.** It is
  observed behavior plus the 2020 blog. If Edge cannot register the package (older Windows,
  policy, failure), it presumably falls back to a Chromium-style `MSEdge._crx_…` AUMID. Not
  verified.
- How the Edge package name suffix (`1710C364`, `FCFEA3FA`) is derived is unknown. It may be a
  hash of the app id or profile, so it is unclear whether it is stable across reinstall, profiles
  or machines. The PFN's publisher hash is deterministic because it comes from the
  `CN=<host>, OID…` publisher.
- Store-installed PWA AUMID: inferred from PWABuilder and hosted-app docs, not observed.
- Window-controls overlay and splash timing: not observed. No such windows were open, and this
  ticket did not allow launching apps.
- The "`url` fails on minimized windows" result is one observation (Slack, minimized). It matters
  little for hinting, which targets visible windows, but it would matter for any background or
  taskbar feature.
