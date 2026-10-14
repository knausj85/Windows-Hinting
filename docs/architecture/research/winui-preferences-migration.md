# Should only the preferences UI move to WinUI 3?

Research note for the architecture-modernization effort (`modernize` branch, spec in
[`docs/architecture/`](../README.md)). Question from the user: *"Evaluate moving just the
preferences UI to the WinUI framework. The WinForms preferences look dated."*

**Date:** 2026-10-14. Codebase claims are cited as `path:line` against the `modernize` branch.
External claims are cited to primary sources: Microsoft Learn, the `microsoft/WindowsAppSDK` and
`microsoft/WindowsAppSDK-Samples` repos. The one community source used is labelled as such.

---

## Bottom line (TL;DR)

**Don't move the preferences UI to WinUI 3 now. Keep decision
[#40](https://github.com/knausj85/Windows-Hinting/issues/40) (modern WinForms) and fix the
"dated" look in phase 08 using .NET 10's now-stable WinForms dark mode plus a real layout
redesign.** If that still looks dated, the fallback is **WPF with the .NET 9+ Fluent theme**,
not WinUI. WPF is the only "modern look" option that adds no new runtime, no new process and no
installer changes.

The reasons, in order of weight:

1. **There's no supported, low-ceremony way to put WinUI 3 inside this process.** The Windows App
   SDK's in-process path (XAML Islands / `DesktopWindowXamlSource`) left experimental in 1.4 with
   the caveat *"only tested for use in C++ apps … does not include any convenient wrapper elements
   for use in WPF or WinForms"* ([WinAppSDK 1.4 notes][wasdk14]). Microsoft's only C# WinForms
   sample needs a custom `XamlApp`, a manual `DispatcherQueueController`, a message-loop
   pre-translate hook, a separate WinUI class library for the XAML, and a project setting its own
   comment calls *"a bit of a hack"* ([sample Program.cs][isl-prog], [sample csproj][isl-csproj]).
   A top-level WinUI `Window` alongside a WinForms message loop isn't documented at all. WinUI
   windows share the thread of the WinUI app that created them ([multiple windows][multiwin]).
2. **The deployment cost hits both shipping channels.**
   - The **MSI** is framework-dependent with a hand-enumerated WiX file list
     (`Windows-Hinting.Installer/ExampleComponents.wxs:4-99`). WinUI would need either the Windows
     App SDK runtime installer (plus the VC++ redistributable) chained into setup
     ([deploy unpackaged][deploy-unpkg]), or the "significantly larger" self-contained payload
     ([unpackage WinUI][unpkg-winui]).
   - The **portable** build is `PublishSingleFile` (`.github/workflows/build.yml:373,504`). WinUI
     supports single-file only when both unpackaged and self-contained, and then extracts its
     dependencies to a temp directory at first launch ([unpackage WinUI][unpkg-winui]).
3. **The architecture spec already rejected it, for reasons that still hold.** #40 weighed WPF,
   WinUI 3 and WebView2 and concluded that *"a rarely-opened settings window for a tray utility
   does not justify a second UI stack or new runtime dependencies"* (issue #40 resolution, item 1;
   reflected in `docs/architecture/target-architecture.md:141`). The new information since #40
   cuts the other way: WinForms dark mode left preview in .NET 10 ([WinForms .NET 10][wf10]), so
   the cheap modernization path is better now than it was then.
4. **The UIAccess host is an untested risk.** The installed exe runs with `uiAccess="true"`
   (`Windows-Hinting/app.manifest:16`), which the spec calls inviolable
   (`target-architecture.md:170`). UIAccess binaries must be signed and live in a secure location
   ([UAC settings][uac]). I found no Microsoft documentation of WinAppSDK being loaded into, or
   bootstrapped from, a UIAccess process. Putting WinUI in-process would need a spike before any
   commitment.

**If WinUI is still wanted later,** the only shape worth considering is a **separate,
non-UIAccess settings exe**. It fits the spec's file-based design well: the settings UI only
writes `preferences.json`, and the host's watcher applies the change
(`target-architecture.md:132-135`). It still costs a cross-process "suspend hotkeys while
recording" signal, a second signed binary, and the runtime deployment above. Estimated at roughly
3–5 agent sessions plus installer and CI work, against roughly 1 for the WinForms path.

---

## 1. Where the preferences UI is today

| Aspect | Today (`modernize` branch) |
|---|---|
| Runtime / TFM | .NET 10, `net10.0-windows`, SDK-style csproj (`Windows-Hinting/Windows-Hinting.csproj:1,6`); SDK pinned to 10.0.100 (`global.json`). **.NET Framework is not a factor.** |
| UI stack | WinForms only (`UseWindowsForms`, `Windows-Hinting.csproj:19`); PerMonitorV2 DPI (`:24`, `app.manifest`). |
| Process / threading | Single STA UI thread: `[STAThread] Main` → mutex → Generic Host → `Application.Run()` with no main form (`Windows-Hinting/Program.cs:14-39`). Tray icon, overlay forms, hotkey window, log viewer and About box all live on this thread. The spec makes the UI thread the single intent-serialization point (`target-architecture.md:64-75`). |
| Launch | Tray menu "Preferences..." → `PreferencesRequested` (`Services/TrayIconManager.cs:58`) → `HintController.OnPreferencesRequested` creates `PreferencesDialog` and calls **modal** `ShowDialog()` (`HintController.cs:766,787`). |
| Form | `Preferences/PreferencesDialog.cs` (727 lines), hand-built code with no designer file: `TabControl` with General / Hotkeys / Window Rules tabs plus OK/Cancel (`PreferencesDialog.cs:89-121`). |
| Persistence | `PreferencesService` serializes `HintOverlayOptions` to `%AppData%\Windows-Hinting\preferences.json` with System.Text.Json (`Services/PreferencesService.cs:12-63`). The OK path also writes the registry run-key directly (`PreferencesDialog.cs:691`). |
| Host coupling | (a) Hotkey recording raises `HotkeyRecordingStarted/Stopped` so the host unregisters and re-registers its global hotkeys (`HintController.cs:767-779`). (b) Live preview of hint position (`HintController.cs:782`). The recorder is a `TextBox` subclass using `ProcessCmdKey` (`Controls/HotkeyRecorderControl.cs:26,123`). |
| Distribution | Installed: signed, per-machine, framework-dependent MSI with UIAccess (`target-architecture.md:170-172`; WiX file list in `ExampleComponents.wxs`). Portable: x64/x86, self-contained **single-file**, no UIAccess (`build.yml:337-376,468-507`; `target-architecture.md:176`). |

### What the modernize spec already plans

- **Phase 07** replaces the persistence plumbing: versioned `preferences.json` + `rules/`, a
  `PreferencesStore` adapter, and **one propagation path** in which watcher → validate →
  `OptionsApplied` handles every change (`phases/phase-07-preferences-infrastructure.md`). The
  settings UI gets *no privileged write path*. It just saves files
  (`target-architecture.md:132-135`).
- **Phase 08** rebuilds the window. It becomes a **modern-WinForms**, singleton, non-modal,
  instant-apply window with a section contract and registry (General, Hotkeys, Click actions,
  Updates). The Window Rules tab goes away, and "Start with Windows" becomes a file field
  (`phases/phase-08-preferences-ui.md`).
- **Decision #40** chose that stack and explicitly rejected WPF, WinUI 3 and WebView2 (issue #40,
  resolution item 1).

No other phase touches the settings UI or the host TFM. Phase 10 (CsWin32) only touches
`Interop.Uia`. Phase 06 keeps the WinForms overlay per decision #34 (`README.md` decision record).

The phase-08 design is framework-agnostic: sections load from typed options, raise `Changed`, and
the shell saves files. That's what makes a later UI-stack swap cheap and contained, whichever
stack is picked now.

---

## 2. Can WinUI 3 run from this WinForms process?

The current stable release is **Windows App SDK 2.5.1** (2026-09-16). 2.0 is the "Current" line
with end of servicing on 2027-04-29, and 1.8 is in maintenance until 2026-09-24
([release channels][channels]). Expect a roughly yearly major-version upgrade.

### Option A: XAML Island inside a WinForms `Form` (in-process)

- `DesktopWindowXamlSource` *"enables non-WASDK desktop applications to host any control that
  derives from Microsoft.UI.Xaml.UIElement in a UI element that is associated with a window
  handle (HWND). This API can be used by desktop applications built using WPF, Windows Forms, and
  the Windows API (Win32)."* ([DesktopWindowXamlSource][dwxs])
- Support level: islands left experimental in **1.4**, but *"Currently XAML Islands are only
  tested for use in C++ apps. This release does not include any convenient wrapper elements for
  use in WPF or WinForms."* ([WinAppSDK 1.4 notes][wasdk14]). I found no later release note that
  withdraws this caveat. The 2.x notes mention only island bug fixes, such as an AV during
  re-entrant **UI Automation** queries at island teardown (2.3.1) and popup scaling (2.1.3)
  ([WinAppSDK 2.x notes][wasdk20]).
- The official C# sample, `Samples/Islands/cs-winforms-unpackaged` ([sample tree][isl-tree]),
  shows the full ceremony:
  - `DispatcherQueueController.CreateOnCurrentThread()` before anything else.
  - A custom `XamlApp` that registers the XAML metadata provider of a **separate WinUI class
    library**. Without it you get a `XamlParseException`.
  - `WindowsAppSdkHelper.EnableContentPreTranslateMessageInEventLoop()` so WinUI sees messages
    before the WinForms loop.
  - `controller.ShutdownQueue()` after `Application.Run` ([Program.cs][isl-prog]).
- The sample csproj requires all of the following ([csproj][isl-csproj]):
  - TFM changed to `net8.0-windows10.0.17763.0` (*"the TargetFramework that specifies
    WindowsAppSDK's minimum Windows version"*).
  - Explicit `Platforms` x64/x86/ARM64 and matching RuntimeIdentifiers.
  - `WindowsPackageType=None`.
  - `ImportFrameworkWinFXTargets=true`, described as *"a bit of a hack to prevent
    Microsoft.WinFX.targets from being imported"*.
- Known rough edge, now fixed: creating two XAML containers (a `Window` plus a
  `DesktopWindowXamlSource`) in a WinForms islands app froze or crashed until 1.5
  ([WindowsAppSDK#4246][i4246]).

**Fit for this repo:** this changes the **host** project's TFM and platform matrix, and it
touches `Program.cs` startup and shutdown on the one thread the spec makes sacred for intent
serialization (`target-architecture.md:69`). It also loads WinAppSDK into the **UIAccess**
process, which is undocumented territory. That's a lot of blast radius for a rarely opened
window.

### Option B: a top-level WinUI `Window` on the WinForms thread, or on a second thread

- WinUI windows *"share the same UI processing thread (including the event dispatcher) from which
  they were created"* ([multiple windows][multiwin]). That thread is the WinUI `Application`'s
  thread.
- Microsoft documents no supported way to start a WinUI `Application` inside, or alongside, an
  existing WinForms or WPF message loop. A WindowsAppSDK discussion (**community, not official**)
  reports it working only with deferred `Application.Start`, separate threads and hand-rolled
  initialization ([discussion #2926][d2926]).

**Not recommended.** It's unsupported, and it would put a second UI thread into a host whose
threading model is built around exactly one.

### Option C: a separate settings process (WinUI 3 app, unpackaged)

- This is the supported, ordinary WinUI shape. An unpackaged app sets `WindowsPackageType=None`
  and the auto-initializer bootstraps the runtime ([unpackage WinUI][unpkg-winui]).
- **It fits the modernize spec unusually well.** The settings UI has *no privileged write path;
  it just saves files*, and the host's watcher → `OptionsApplied` path applies every change,
  including hand edits (`target-architecture.md:132-135`). A separate exe that only writes
  `preferences.json` needs nothing else from the host for any setting except one.
- **The exception is hotkey recording.** Phase 08 keeps exactly one host interaction: suspend
  global hotkey registration while recording (`phase-08-preferences-ui.md`, Work item 3; today
  that's `HintController.cs:767-779`). Across processes this needs a new IPC signal, such as a
  named event or a registered window message. That works against decision #33's "no external
  control surface; pipe deleted" (`README.md` decision record), even though the signal would be
  tiny and local.
- The rest is plumbing: a "focus the existing instance" single-instance handshake, process launch
  from the tray, a second signed binary, and a second project in CI and WiX. The child process
  doesn't need UIAccess, so the UIAccess risk disappears.

---

## 3. Target framework and .NET Framework

- **.NET Framework isn't a blocker here.** The host is already .NET 10 (`Windows-Hinting.csproj:6`).
  For reference, WinAppSDK requires .NET 6+ for C# ([deploy unpackaged][deploy-unpkg]).
- WinAppSDK projects need a **Windows-versioned TFM** at or above the SDK minimum, for example
  `net8.0-windows10.0.17763.0` ([sample csproj][isl-csproj]) or `…10.0.19041.0`
  ([unpackage WinUI][unpkg-winui]). Here that would be `net10.0-windows10.0.17763.0` or later.
  WinAppSDK supports Windows 10 1809+ ([WinAppSDK overview][wasdk-ovw]).
- Supported architectures are x64, x86 and ARM64 only ([sample csproj][isl-csproj];
  [deploy unpackaged][deploy-unpkg]). That matches the current x64 MSI and x64/x86 portable
  builds.

---

## 4. Deployment cost

| Channel | Today | With WinUI (framework-dependent) | With WinUI (self-contained) |
|---|---|---|---|
| **MSI (installed, UIAccess)** | Framework-dependent .NET 10. WiX lists each DLL by hand (`ExampleComponents.wxs:53-99`). | Chain `WindowsAppRuntimeInstall.exe` (per-arch, silent `--quiet`) **plus** the VC++ redistributable, or install the runtime MSIX packages from the MSI; detect via `PackageManager` ([deploy unpackaged][deploy-unpkg]). | The runtime's files are copied next to the exe and must be added to the WiX file list. Output is *"significantly larger … each app update carries the full runtime payload"* ([unpackage WinUI][unpkg-winui]). .NET must be self-contained too to be "fully self-contained" ([self-contained deploy][sc-deploy]). |
| **Portable (single-file, self-updating)** | `PublishSingleFile`, self-contained (`build.yml:373,504`). | **Not possible**: framework-dependent WinUI apps don't support `PublishSingleFile` ([unpackage WinUI][unpkg-winui]). | Supported since 1.5 with `WindowsAppSDKSelfContained`, `EnableMsixTooling` and `IncludeAllContentForSelfExtract`, but dependencies are **extracted to a temp dir at first launch** ([unpackage WinUI][unpkg-winui]). That's a behavior change for a portable tool. |
| **MSIX** | Not used. | Not an option: MSIX packaging is ruled out by the UIAccess / per-machine MSI constraint (`target-architecture.md:170-172`). | n/a |

Other cost notes:

- Runtime servicing becomes a new obligation. Support requires *"using the latest Windows App SDK
  patch update"* ([release channels][channels]).
- In a separate-process design, a framework-dependent settings exe means a user without the
  runtime gets a broken Preferences menu item. Self-contained avoids that at the size cost above.

For comparison, **WPF adds nothing to deployment**. The .NET Desktop Runtime the MSI already
depends on runs *"desktop apps, such as a Windows Presentation Foundation (WPF) or Windows Forms
app"* ([.NET install][dotnet-install]). WPF also works with `PublishSingleFile`, which Microsoft
itself points to as the alternative when WinUI's extraction model isn't acceptable
([unpackage WinUI][unpkg-winui]).

---

## 5. Alternatives

### 5a. Modern WinForms on .NET 10 (the current plan, #40 / phase 08)

- **Dark mode is now stable.** *"Windows Forms has fully integrated dark mode support … The
  Application.SetColorMode(SystemColorMode) API is no longer considered experimental."*
  ([WinForms .NET 10][wf10]). Today's app doesn't call it (no `SetColorMode` in
  `Windows-Hinting/`), so following the system theme is an easy, visible win.
- Custom-drawn controls that wrap Win32 common controls must opt in with
  `ControlStyles.ApplyThemingImplicitly` in `CreateParams` ([WinForms .NET 10][wf10]). That's
  relevant to `HotkeyRecorderControl`.
- Non-modal async display (`Form.ShowAsync`) is stable in .NET 10 ([WinForms .NET 10][wf10]). It
  suits phase 08's singleton non-modal window.
- Most of the "dated" look is layout, not toolkit: a hand-coded `TabControl` with group boxes and
  OK/Cancel (`PreferencesDialog.cs:89-121`). Phase 08 already replaces that with a sidebar or tab
  section shell, instant apply and reset affordances.
- **Cost:** about what phase 08 already budgets, plus a small dark-mode pass. **Risk:** low. **New
  dependencies:** none.
- **Ceiling:** it won't look like Windows 11 Settings. There's no Fluent control set, Mica or
  NavigationView.

### 5b. WPF with the Fluent theme (.NET 9+), in-process

- .NET 9 shipped a Fluent theme with *"integrated light and dark modes, and a system accent color
  support"*, applied with `ThemeMode="System"` in XAML on the `Application` **or a single
  `Window`** ([WPF .NET 9][wpf9]). Setting `ThemeMode` *in code* is still experimental (WPF0001),
  but XAML-declared theming isn't ([WPF .NET 9][wpf9]).
- A modeless WPF `Window` opened from a WinForms message loop is a long-supported interop path.
  `ElementHost.EnableModelessKeyboardInterop` *"installs a message filter in the Windows
  Forms-based application"* so the WPF window gets keyboard input ([EnableModelessKeyboardInterop][emki]).
  It's the same STA thread, so it fits the one-UI-thread model.
- Deployment: no change (see §4). No UIAccess unknowns beyond normal WPF.
- **Cost:** phase 08 plus about 1–2 sessions: `UseWPF`, sections as WPF `UserControl`s, a WPF
  hotkey recorder, and the interop call. **Risk:** low to medium (WPF and WinForms mixing in one
  project, Fluent theme maturity). **Ceiling:** close to the Windows 11 look.

### 5c. WinUI 3

Covered in §2 and §4. It has the best visual ceiling (native Windows 11 controls, Mica). The
in-process path is either unsupported or sample-grade, and the out-of-process path adds IPC, a
second binary and runtime deployment to both channels.

### 5d. Third-party Fluent libraries

I didn't evaluate any. The task asked only for notable ones, and none is first-party. Any of them
would add a non-Microsoft dependency to a signed UIAccess binary, which #40's "zero new deployment
weight" stance argues against.

---

## 6. Known WinUI limitations and gotchas relevant here

- **Tray-only app:** the host has no main window (`Program.cs:39`). WinUI's model assumes the
  `Application` owns the UI thread and its windows ([multiple windows][multiwin]). Only Option C
  (separate exe) avoids that mismatch cleanly.
- **Threading:** islands need a `DispatcherQueueController` on the thread and message
  pre-translation wired into the host loop ([sample Program.cs][isl-prog]). That's new
  responsibility on the thread the spec makes the single serialization point.
- **Build:** a separate WinUI class library is needed for XAML in the islands sample
  ([sample Program.cs][isl-prog]). CI is MSBuild-bound for COM interop until phase 10
  (`target-architecture.md:202-205`; `build.yml:351-363`), so adding WinAppSDK's build targets to
  that pipeline is untested here.
- **Single-file:** temp-dir extraction for self-contained single-file, and no support at all when
  framework-dependent ([unpackage WinUI][unpkg-winui]).
- **UI Automation:** the host is itself a UIA client that scans foreground windows. An island
  inside the host process did have an AV on re-entrant UIA queries during teardown, fixed in 2.3.1
  ([WinAppSDK 2.x notes][wasdk20]). That's a reminder that in-process hosting couples the settings
  UI to the scanner's process.
- **DPI:** no blocker. The host is already PerMonitorV2 (`Windows-Hinting.csproj:24`).
- **Servicing cadence:** a new stable major roughly every 6 months, with "Current" support for
  about a year ([release channels][channels]).

---

## 7. Recommendation and sequencing

**Recommendation: stay on WinForms for phase 08 and modernize it properly. Revisit with WPF
Fluent, not WinUI, only if the phase-08 result still looks dated.**

| Option | Rough effort | Deployment change | Risk | Visual ceiling |
|---|---|---|---|---|
| Phase 08 WinForms + `SetColorMode(System)` | ~1 session (already planned) + small dark-mode pass | None | Low | Clean, but not Windows 11 Fluent |
| WPF Fluent window, in-process | Phase 08 + ~1–2 sessions | None | Low–medium | Near Windows 11 |
| WinUI 3 separate settings exe | ~3–5 sessions + WiX/CI/signing work | Runtime installer or self-contained payload in MSI; extraction-based single-file for portable | Medium | Native Windows 11 |
| WinUI 3 island in host | ~3–5 sessions + spike | Same as above, plus host TFM/platform changes | High (sample-grade support; UIAccess untested) | Native Windows 11 |

Sequencing against the modernize phases:

1. **Phases 00–07: unaffected.** Nothing here should change them. In particular, phase 07's
   "settings UI just writes files" contract is what keeps every option open, so keep it strict.
2. **Phase 08: make the stack choice here.** Execute #40 as written, adding
   `Application.SetColorMode(SystemColorMode.System)` and a deliberate visual pass (sidebar
   navigation, spacing, reset affordances). Keep the section contract free of WinForms types where
   practical, so sections could be re-implemented in another stack without touching the shell's
   save logic.
3. **After phase 08 (optional, a separate ticket):** if the result is still judged dated, run a
   timeboxed **WPF Fluent spike**: one section in a `ThemeMode="System"` WPF window opened via
   `EnableModelessKeyboardInterop`. If it lands, amend #40 with a one-line decision update. Only
   reconsider WinUI if Microsoft ships a supported WinForms/WPF island wrapper, or if the app
   adopts the WinAppSDK runtime for another reason, which would make the deployment cost
   already paid.
4. **If the user wants WinUI despite this:** reopen #40 **before** phase 08 starts. Choose
   Option C (separate exe), add a design decision for the hotkey-suspend IPC (it amends #33's "no
   external surface"), and add installer and CI work items to phase 08, or split it into 08a
   (shell + sections) and 08b (WinUI packaging).

---

## Sources

[wasdk14]: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-4
[wasdk20]: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0?pivots=stable
[channels]: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels
[wasdk-ovw]: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/
[dwxs]: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.hosting.desktopwindowxamlsource
[isl-tree]: https://github.com/microsoft/WindowsAppSDK-Samples/tree/main/Samples/Islands
[isl-prog]: https://github.com/microsoft/WindowsAppSDK-Samples/blob/main/Samples/Islands/cs-winforms-unpackaged/Program.cs
[isl-csproj]: https://github.com/microsoft/WindowsAppSDK-Samples/blob/main/Samples/Islands/cs-winforms-unpackaged/WinFormsWithIslandApp.csproj
[i4246]: https://github.com/microsoft/WindowsAppSDK/issues/4246
[d2926]: https://github.com/microsoft/WindowsAppSDK/discussions/2926
[multiwin]: https://learn.microsoft.com/en-us/windows/apps/develop/ui/multiple-windows
[deploy-unpkg]: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps
[unpkg-winui]: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app
[sc-deploy]: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps
[uac]: https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/settings-and-configuration
[wf10]: https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net100
[wpf9]: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90
[emki]: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.integration.elementhost.enablemodelesskeyboardinterop
[dotnet-install]: https://learn.microsoft.com/en-us/dotnet/core/install/windows

- Windows App SDK 1.4 release notes (XAML Islands status): <https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-4>
- Windows App SDK 2.0 / 2.5.x release notes: <https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0?pivots=stable>
- Windows App SDK release channels and servicing: <https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels>
- Windows App SDK overview: <https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/>
- `DesktopWindowXamlSource` API: <https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.hosting.desktopwindowxamlsource>
- WinForms island sample (Program.cs, csproj): <https://github.com/microsoft/WindowsAppSDK-Samples/tree/main/Samples/Islands/cs-winforms-unpackaged>
- WindowsAppSDK issue #4246 (two XAML containers crash, fixed in 1.5): <https://github.com/microsoft/WindowsAppSDK/issues/4246>
- WindowsAppSDK discussion #2926 (community; WinUI window from WPF): <https://github.com/microsoft/WindowsAppSDK/discussions/2926>
- WinUI multiple windows: <https://learn.microsoft.com/en-us/windows/apps/develop/ui/multiple-windows>
- Deploy unpackaged apps (runtime installer, VC++ redist, .NET 6+): <https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps>
- Distribute an unpackaged WinUI 3 app (single-file, self-contained trade-offs): <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app>
- Self-contained deployment: <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps>
- UAC settings (UIAccess secure locations and signing): <https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/settings-and-configuration>
- What's new in WinForms for .NET 10 (dark mode stable): <https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net100>
- What's new in WPF for .NET 9 (Fluent theme, ThemeMode): <https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90>
- `ElementHost.EnableModelessKeyboardInterop`: <https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.integration.elementhost.enablemodelesskeyboardinterop>
- Install .NET on Windows (Desktop Runtime covers WPF + WinForms): <https://learn.microsoft.com/en-us/dotnet/core/install/windows>
- Decision #40 (preferences UI stack): <https://github.com/knausj85/Windows-Hinting/issues/40>
