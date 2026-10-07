# Hinting Windows shell surfaces via extension mechanisms (not accessibility)

**Question:** Can we get *more reliable / richer* element geometry (bounding rects for overlay placement) and activation for the built-in Windows **shell** surfaces — File Explorer, Start Menu, Taskbar, Settings, context menus — through **extension mechanisms** (shell extension handlers, deskbands, Win32 control messages, injection/hooks, WinUI internals) rather than through the **accessibility** stack (UIA/MSAA) the app already uses?

**Date:** 2026-09-25

---

## Bottom line up front

**No — there is no non-accessibility extension route that beats UIA broadly for these surfaces, and for the modern (Win11) surfaces UIA is the *only* programmatic geometry source that exists.** The shell's "extension" mechanisms fall into two buckets, and neither is a general geometry/activation engine for hinting: (1) *feature-injection* extensions (shell extension handlers, deskbands, band objects, namespace extensions) **add commands or panes** to Explorer — they do **not** expose the on-screen rectangles of Explorer's own rendered items, which is what hint placement needs; (2) *low-level scraping* mechanisms (Win32 `LVM_*` ListView messages, DirectUI, DLL injection) either **do not apply** to the modern controls or are **undocumented / injection-based / AV-flagged / version-brittle**.

The only genuinely useful *non-UIA* wins are narrow and legacy: (a) the **desktop icon grid** is still a real `SysListView32` where `LVM_GETITEMRECT`/`LVM_GETITEMCOUNT`/`LVM_HITTEST` give true per-item rects and hit-testing (cross-process, with memory marshalling); and (b) the **Explorer file list** exposes documented, supported **activation** (and item enumeration) via the shell automation COM chain `IShellWindows → IShellBrowser → IShellView → IFolderView`, though its *geometry* is weak (position of an item's upper-left corner, meaningful only in free-arranged icon views, not Details/List view). Everything Win11-modern — Start Menu (`StartMenuExperienceHost`), the XAML taskbar, Settings, the new WinUI Explorer chrome (address bar, command bar, the incoming WinUI file view and Properties dialog) — is XAML/WinUI with **no Win32 window-message geometry API at all**; geometry there is exposed **only** through UIA's `AutomationPeer`/`BoundingRectangle`. Notably, Microsoft's own inspection tools (Inspect.exe, Accessibility Insights) read these surfaces exclusively through UIA, and modern-Explorer alternatives like Files rebuild the folder view on `Windows.Storage`/`IShellItem` rather than scraping Explorer's UI.

**Recommendation:** keep UIA as the primary engine. Consider only two *narrow, optional* non-UIA fallbacks: `IFolderView::SelectItem`/`IContextMenu` for more reliable **file-item activation** in Explorer, and `LVM_*` messages for the **desktop icon** surface if that surface matters. Do **not** invest in shell extension handlers, deskbands, DUI scraping, or injection for hinting.

---

## Per-mechanism findings

### 1. Shell namespace / shell-automation COM chain (`IShellWindows → IShellBrowser → IShellView → IFolderView`)

- **What it is.** The documented, supported way to talk to an *already-open* Explorer window from another process. You enumerate open shell windows via `IShellWindows` (`ShellWindows` coclass), match the target `HWND` through `IWebBrowserApp::get_HWND`, `QueryInterface` for `IServiceProvider`, request `SID_STopLevelBrowser`/`IShellBrowser`, call `QueryActiveShellView` for `IShellView`, then `QueryInterface` for `IFolderView`. This is Raymond Chen's canonical walkthrough and is still the accepted route. ([Old New Thing — Querying information from an Explorer window](https://devblogs.microsoft.com/oldnewthing/20040720-00/?p=38393); [IFolderView interface](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview))
- **Geometry — weak.** `IFolderView::GetItemPosition(pidl, POINT*)` returns only *the item's upper-left corner as a POINT*, not a rectangle, and it is meaningful only in **free-arranged icon views**. The authoritative desktop-icon sample requires you to "turn off Auto arrange icons and Align icons to grid" before positions are usable — in **Details/List/report view the shell owns placement and per-item positions are not meaningfully reported**. You can approximate an item box by pairing the POINT with `GetSpacing`/`GetDefaultSpacing` (item width/height incl. whitespace), and coordinates are in the view's own space (needs scroll-offset + client-to-screen mapping), but there is **no supported per-row rectangle for the common Details view**. ([IFolderView::GetItemPosition](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-getitemposition); [Old New Thing — Manipulating the positions of desktop icons](https://devblogs.microsoft.com/oldnewthing/20130318-00/?p=4933))
- **Activation — good.** `IFolderView::SelectItem(index, SVSI_* flags)` (e.g. `SVSI_SELECT | SVSI_ENSUREVISIBLE | SVSI_FOCUSED`, or `SVSI_EDIT` to rename), `SelectAndPositionItems`, plus item enumeration (`Item`, `ItemCount`, `Items` → PIDLs). For "open/invoke," you get the item's PIDL and invoke its default verb via `IContextMenu`/`IShellFolder::GetUIObjectOf`, or use the scriptable `IShellFolderViewDual::SelectItem`. This is documented and supported. ([IFolderView methods](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview); [IShellFolderViewDual::SelectItem](https://learn.microsoft.com/en-us/windows/win32/api/shldisp/nf-shldisp-ishellfolderviewdual-selectitem))
- **Coverage.** The **Explorer file list only** — and, importantly, it works on **both legacy and Win11 Explorer** because the main file view is still the classic `CDefView`/DUI shell view (see §5). It does **not** reach the Explorer *chrome* (address bar, command bar/ribbon, navigation pane, tabs), the Start Menu, the taskbar, or Settings — those are not shell folder views.
- **Robustness.** Documented since Windows XP, stable, cross-process, no injection. The weak point is geometry, not stability.
- **vs. UIA.** Better/cleaner for *file activation and enumeration by PIDL*; **worse for geometry** (UIA gives a real `BoundingRectangle` per item in any view mode; this gives only an icon-view POINT).

### 2. Shell extension handlers (IExplorerCommand, context-menu / column / thumbnail / property / preview handlers, `IShellFolder` namespace extensions, `IExplorerPaneVisibility`)

- **What it is.** The sanctioned way to *extend* Explorer: add context-menu verbs, ribbon/command entries (`IExplorerCommand`), extra columns, thumbnails, property/preview panes, or whole virtual namespaces (`IShellFolder`). ([Creating Shell Extension Handlers](https://learn.microsoft.com/en-us/windows/win32/shell/handlers); [IExplorerCommand](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorercommand))
- **Geometry / activation — wrong tool.** These handlers are invoked *by* the shell to contribute behavior; they receive item PIDLs, not the geometry of Explorer's already-rendered rows/tiles/buttons. There is **no handler interface that reports the screen rectangle of an existing UI element**. `IExplorerPaneVisibility` only shows/hides stock panes. So this bucket cannot place hints over Explorer's controls.
- **Coverage / robustness.** Documented and supported, but only relevant if we wanted to *add* our own commands (e.g. a right-click verb), not to hint existing controls. Note Win11 is actively moving this surface to WinUI (e.g. the new WinUI Properties dialog), which is itself a source of extension breakage. ([Windows Latest — WinUI Properties dialog](https://www.windowslatest.com/2026/07/22/first-look-windows-11-is-replacing-windows-95-era-properties-dialog-now-with-dark-mode-and-winui/))
- **vs. UIA.** Orthogonal — different purpose. Not a geometry/hinting mechanism.

### 3. Deskbands / band objects / taskbar toolbars

- **What it is.** COM band objects hosted by the shell/taskbar (`IDeskBand`, desk bands, tool bands, Explorer bars). ([Band Objects](https://learn.microsoft.com/en-us/windows/win32/shell/band-objects))
- **Status — dead end.** Deprecated since Windows 7/8 (Microsoft explicitly stopped recommending them), and the **Windows 11 XAML-Islands taskbar removed deskband support entirely** — in some builds the deskband API is removed, not merely unused. ([Microsoft — Band Objects (deprecation noted)](https://learn.microsoft.com/en-us/windows/win32/shell/band-objects); [WindowsAppSDK Discussion #2320 — Desk Band Support](https://github.com/microsoft/WindowsAppSDK/discussions/2320); [EverythingToolbar #723 — no Win11 deskband host](https://github.com/srwi/EverythingToolbar/issues/723))
- **Geometry / activation.** A band gets its *own* rectangle on the taskbar; it gives nothing about *taskbar buttons, tray icons, Start, or file items*. Useless for hinting shell elements.
- **vs. UIA.** Strictly worse and disappearing.

### 4. Win32 ListView control messages to legacy shell list-views (`LVM_GETITEMCOUNT`, `LVM_GETITEMRECT`, `LVM_GETITEMPOSITION`, `LVM_HITTEST`, `LVM_GETITEMINDEXRECT`)

- **What it is.** Standard common-control messages that return exact per-item bounding rectangles and hit-test results — *for a genuine `SysListView32`*. ([LVM_GETITEMRECT](https://learn.microsoft.com/en-us/windows/win32/controls/lvm-getitemrect); [LVM_GETITEMINDEXRECT](https://learn.microsoft.com/en-us/windows/win32/Controls/lvm-getitemindexrect); [List-View overview](https://learn.microsoft.com/en-us/windows/win32/controls/list-view-controls-overview))
- **Where it applies today.**
  - **The desktop icon grid IS still a real `SysListView32`** (`Progman` → `SHELLDLL_DefView` → `SysListView32` captioned "FolderView"). `LVM_GETITEMRECT`/`COUNT`/`HITTEST` give true rects and hit-testing here. ([desktop hierarchy discussion, traces to `SysListView32`](https://groups.google.com/g/comp.os.ms-windows.programmer.win32/c/Fr12kM4IgmA)) — **this is the one real non-UIA geometry win.**
  - Some legacy dialogs and a few old shell surfaces still use `SysListView32`.
- **Where it does NOT apply.** The **modern Explorer *window* file list is a `DirectUIHWND` (DUI), not a `SysListView32`** (see §5) — `LVM_*` messages do nothing there. Win11 taskbar/Start/Settings are XAML — no ListView at all.
- **Robustness caveat.** These messages take *pointers*; sending them **cross-process** requires allocating memory in the target with `VirtualAllocEx` + `WriteProcessMemory` and reading back with `ReadProcessMemory` — a marshalling dance that is fragile, requires `PROCESS_VM_*` rights, and is the same pattern malware uses (so it can trip EDR heuristics). Documented messages, undocumented/fragile cross-process usage. ([Read SysListView32 from another process — technique](https://chentiangemalc.wordpress.com/2019/02/04/read-syslistview32-control-from-another-process-with-powershell/); [WriteProcessMemory](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-writeprocessmemory))
- **vs. UIA.** For the desktop surface, `LVM_*` is arguably *more precise and faster* than UIA (direct rects/hit-test); for everything else it is inapplicable.

### 5. DirectUI / `DirectUIHWND` (DUI)

- **What it is.** Microsoft's internal DirectUI engine. Since Windows 7, `CDefView` (the Explorer file-list view) was reimplemented in DirectUI; the file list you see in an Explorer *window* is a `DirectUIHWND`, not a `SysListView32`. This is **undocumented** and has **no public window-message API** for enumerating items or reading their rectangles. ([File Explorer — DirectUI/DefView history](https://en.wikipedia.org/wiki/File_Explorer); developer discussions of `DirectUIHWND` confirm no message API, e.g. [platformsdk.shell thread](https://groups.google.com/g/microsoft.public.platformsdk.shell/c/kScZFuAE0Hk))
- **Geometry / activation.** None via messages. DUI exposes its content to the outside world **only through UIA** (DirectUI implements UIA providers). So "scraping DUI" *is* using UIA.
- **Coverage / robustness.** Undocumented internal engine; anything reverse-engineered here is version-brittle. Not a sanctioned path.
- **vs. UIA.** UIA is the intended and only supported window into DUI content.

### 6. DLL injection / `SetWindowsHookEx` into `explorer.exe` / `StartMenuExperienceHost.exe`

- **What it is.** Loading your code into the shell process to call in-process APIs (real ListView handles, XAML tree, private interfaces) directly.
- **Geometry / activation.** In principle the richest access (in-process you can read anything), but:
- **Robustness — poor / unsupported.**
  - Injection into `explorer.exe` and especially the **packaged/XAML host processes** (`StartMenuExperienceHost`, `ShellExperienceHost`, the XAML-Islands taskbar) is **undocumented and unsanctioned**; these run as packaged apps with different integrity/containerization, and Microsoft's stated direction is more isolation, not less. ([Start Menu / XAML host packaging & isolation — KB5072911 notes XAML-dependent shell apps](https://support.microsoft.com/en-us/topic/kb5072911-explorer-the-start-menu-and-other-xaml-dependent-apps-might-not-start-or-close-unexpectedly-on-some-enterprise-devices-d2d30684-4e2b-47f5-9899-a00a8e0acb09))
  - Cross-process code injection is the archetypal malware behavior; **AV/EDR and Windows Defender flag it**, and PPL/tamper-protection layers are explicitly designed to block unsigned injection. ([Bypassing PPL in Userland — itm4n](https://itm4n.github.io/bypassing-ppl-in-userland-again/); [EDR detection of injection — Palo Alto Unit 42](https://unit42.paloaltonetworks.com/dll-hijacking-techniques/))
  - Breaks on Windows feature updates that reshuffle shell internals.
- **vs. UIA.** UIA achieves cross-process reads *without* injection and is fully supported. Injection trades all of that away for access we largely get via UIA anyway.

### 7. WinUI / XAML `ItemsView` internals (Win11 modern surfaces)

- **What it is.** Win11's Start Menu, the XAML-Islands taskbar, Settings, the new WinUI Properties dialog, and the incoming WinUI file view are XAML/WinUI apps. XAML controls are **not** HWND-per-element and have **no Win32 window-message geometry API**; the XAML visual tree is process-private. ([Windows UI Library](https://en.wikipedia.org/wiki/Windows_UI_Library); [Windows 11 taskbar rebuilt in XAML Islands](https://winaero.com/microsoft-to-remove-some-popular-taskbar-customization-options-in-windows-11/); [Windows Latest — WinUI migration direction](https://www.windowslatest.com/2026/07/30/a-major-windows-11-ui-redesign-is-coming-microsoft-is-dumping-legacy-code-for-winui/))
- **Geometry / activation.** Exposed to other processes **only through UIA** — each XAML control publishes an `AutomationPeer` with `BoundingRectangle` and control patterns (`Invoke`, `Toggle`, `SelectionItem`, etc.). There is no non-accessibility programmatic surface. ([WinUI AutomationPeer example — TreeViewListAutomationPeer](https://learn.microsoft.com/en-us/windows/winui/api/microsoft.ui.xaml.automation.peers.treeviewlistautomationpeer))
- **Robustness.** UIA on these surfaces is the supported path, though quality varies (Start/taskbar peers are sometimes sparse or slow — a known UIA weakness, but the *only* option).
- **vs. UIA.** There is nothing to compare — UIA *is* the mechanism for these surfaces.

### 8. UIA baseline (for contrast — what the app already uses)

- **Geometry.** `IUIAutomationElement::CurrentBoundingRectangle` returns a real **screen-coordinate rectangle** per element, in every view mode, across processes — exactly what hint placement needs. The app already does this (`UIAutomationService` + `BoundingRectangle` in the activator/overlay pipeline).
- **Activation.** `InvokePattern`, `TogglePattern`, `SelectionItemPattern`, `SetFocus`, with a synthesized mouse-click fallback — mirrors the app's existing `ElementActivatorChain`.
- **Coverage.** The widest of any single mechanism: legacy and modern Explorer, Start, taskbar, Settings, context menus, Alt-Tab, tray — anything that implements a UIA provider.
- **Robustness.** Documented, supported, cross-process, no injection; it is what Microsoft's own Inspect.exe and Accessibility Insights use and nothing else. ([Inspect — Accessibility tools](https://learn.microsoft.com/en-us/windows/win32/winauto/inspect-objects); [Accessibility Insights for Windows](https://accessibilityinsights.io/docs/windows/overview/)) Its weaknesses (sparse/slow peers on some Win11 XAML surfaces, `StructureChanged` timing) are the ones the project already tracks — but **no extension mechanism removes them**, because for those surfaces UIA is the only door.

---

## Per-surface summary

| Surface | Best non-UIA mechanism | Geometry (rects)? | Activation? | Supported / status | UIA still the practical choice? |
|---|---|---|---|---|---|
| **Explorer — legacy file list (`SysListView32` era / some dialogs)** | `LVM_GETITEMRECT`/`COUNT`/`HITTEST` (cross-proc marshalling) | **Yes**, exact rects | Yes (`LVM`/`IFolderView`) | Documented msgs; cross-proc use fragile | Comparable; `LVM` more precise where a real ListView exists |
| **Explorer — modern window file list (Win11, `DirectUIHWND`/`CDefView`)** | `IFolderView` (select/enumerate) | **No** rects (POINT, icon-view only) | **Yes** (`SelectItem` + `IContextMenu`) | `IFolderView` documented/supported | **Yes** for geometry; `IFolderView` useful for activation |
| **Explorer chrome (address bar, command bar/ribbon, nav pane, tabs)** | none (WinUI/DUI) | No | No | — | **Yes — UIA only** |
| **Start Menu (`StartMenuExperienceHost`, XAML)** | none (packaged XAML) | No | No | injection unsupported | **Yes — UIA only** |
| **Taskbar (Win11 XAML Islands)** | deskband (dead) → none | No | No | deskband removed | **Yes — UIA only** |
| **Desktop icon grid (`SHELLDLL_DefView` → `SysListView32`)** | `LVM_*` messages | **Yes**, exact rects + hit-test | Yes | Documented msgs; cross-proc marshalling | `LVM` is a real win here |
| **Settings (WinUI/UWP)** | none (XAML) | No | No | — | **Yes — UIA only** |
| **Context menus (classic `#32768` vs WinUI Win11)** | classic menus: `MN_*`/menu API; WinUI: none | Classic: partial | Classic: yes | mixed | Mostly UIA; classic menu API niche |

---

## Recommendation for this project

**Stay on UIA as the primary engine.** The research does not surface any extension mechanism that is *broadly* more reliable or richer than UIA for shell hinting, and for every Win11-modern surface (Start, taskbar, Settings, Explorer chrome, the incoming WinUI file view) UIA is the *only* programmatic geometry path in existence. The app's current design — `UIAutomationService` for discovery + `BoundingRectangle` for overlay geometry + the `InvokePattern/TogglePattern/SelectionItemPattern/SetFocus/MouseClick` activator chain — is already aligned with what Microsoft's own tools do, and no extension route eliminates UIA's known weak spots on those surfaces.

Consider only these **narrow, optional, per-surface fallbacks**, each behind a capability/profile flag (consistent with the app-profile layer the project is already contemplating):

1. **`IFolderView` for Explorer file-item *activation* (not geometry).** Where UIA `Invoke`/`SelectionItem` on the modern DUI file list is flaky, the documented `IShellWindows → IShellBrowser → IShellView → IFolderView` chain gives robust *select + default-verb invoke* by PIDL. Geometry would still come from UIA (`BoundingRectangle`), since `GetItemPosition` is icon-view-only. **Low risk, fully supported, worth a small prototype only if UIA activation on Explorer files proves unreliable.**
2. **`LVM_*` messages for the *desktop icon* surface** (and any residual `SysListView32` dialog), if hinting the desktop matters. This is the one place non-UIA geometry is strictly better (true rects + hit-test). Cost: cross-process memory marshalling and EDR-adjacency; keep it isolated and optional.

**Do not** invest in: shell extension handlers or namespace extensions (they add commands, not geometry), deskbands/band objects (removed in Win11), DirectUI scraping (undocumented, UIA is its only public face), or DLL injection/hooks into `explorer.exe`/`StartMenuExperienceHost` (unsupported, AV/EDR-flagged, tamper-protection/PPL barriers, update-brittle). If richer *data* about Explorer contents were ever needed, the industry pattern (e.g. Files) is to read the namespace directly via `Windows.Storage`/`IShellItem` and render your own view — a replacement, not a hinting augmentation, and out of scope for an overlay tool.

---

## Open questions / would-need-hands-on verification

- **`IFolderView::GetItemPosition` in Details view on Win11:** confirm empirically whether it returns usable coordinates or zero/garbage in report view (docs and the desktop-icon sample imply icon-view-only, but this was not verified against a live Win11 Details-view window).
- **`IShellWindows`/`IFolderView` against the *current* Win11 (26xxx) Explorer:** confirm the chain still binds now that tabs/chrome are WinUI — the file list is believed to still be classic `CDefView`/DUI, but verify on the installed build (10.0.26200).
- **When Microsoft ships the announced WinUI *file view*** (not just chrome), the `IFolderView` activation route and any `LVM`/DUI assumptions for the Explorer window may break; re-test at that point.
- **Desktop `SysListView32` cross-process `LVM_*`** under current Defender/EDR: verify `WriteProcessMemory`/`ReadProcessMemory` marshalling isn't blocked or flagged on a protected machine.
- **UIA peer quality on Start/taskbar** on this exact build: measure whether `BoundingRectangle`/`Invoke` coverage is complete enough for hinting, since that (not an extension route) is the real ceiling.
- **Classic vs. WinUI context menus:** classic `#32768` menus have a Win32 menu API; the Win11 WinUI context menu does not — confirm which the target scenarios hit.

---

## Sources

- Old New Thing — Querying information from an Explorer window: https://devblogs.microsoft.com/oldnewthing/20040720-00/?p=38393
- Old New Thing — Manipulating the positions of desktop icons: https://devblogs.microsoft.com/oldnewthing/20130318-00/?p=4933
- IFolderView interface: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview
- IFolderView::GetItemPosition: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-getitemposition
- IShellFolderViewDual::SelectItem: https://learn.microsoft.com/en-us/windows/win32/api/shldisp/nf-shldisp-ishellfolderviewdual-selectitem
- IShellView2::SelectAndPositionItem: https://learn.microsoft.com/en-us/windows/desktop/api/shobjidl_core/nf-shobjidl_core-ishellview2-selectandpositionitem
- Creating Shell Extension Handlers: https://learn.microsoft.com/en-us/windows/win32/shell/handlers
- IExplorerCommand: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorercommand
- Band Objects (deskbands — deprecation): https://learn.microsoft.com/en-us/windows/win32/shell/band-objects
- WindowsAppSDK Discussion #2320 — Desk Band Support: https://github.com/microsoft/WindowsAppSDK/discussions/2320
- EverythingToolbar #723 — no Win11 deskband host: https://github.com/srwi/EverythingToolbar/issues/723
- LVM_GETITEMRECT: https://learn.microsoft.com/en-us/windows/win32/controls/lvm-getitemrect
- LVM_GETITEMINDEXRECT: https://learn.microsoft.com/en-us/windows/win32/Controls/lvm-getitemindexrect
- List-View controls overview: https://learn.microsoft.com/en-us/windows/win32/controls/list-view-controls-overview
- WriteProcessMemory: https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-writeprocessmemory
- Reading SysListView32 from another process (technique): https://chentiangemalc.wordpress.com/2019/02/04/read-syslistview32-control-from-another-process-with-powershell/
- Desktop window hierarchy → SysListView32 "FolderView": https://groups.google.com/g/comp.os.ms-windows.programmer.win32/c/Fr12kM4IgmA
- File Explorer (DirectUI/DefView history): https://en.wikipedia.org/wiki/File_Explorer
- DirectUIHWND discussion (no message API): https://groups.google.com/g/microsoft.public.platformsdk.shell/c/kScZFuAE0Hk
- Windows UI Library (WinUI): https://en.wikipedia.org/wiki/Windows_UI_Library
- WinUI AutomationPeer (BoundingRectangle via UIA): https://learn.microsoft.com/en-us/windows/winui/api/microsoft.ui.xaml.automation.peers.treeviewlistautomationpeer
- Windows 11 XAML-Islands taskbar & removed customizations: https://winaero.com/microsoft-to-remove-some-popular-taskbar-customization-options-in-windows-11/
- Windows Latest — Windows 11 dumping legacy code for WinUI: https://www.windowslatest.com/2026/07/30/a-major-windows-11-ui-redesign-is-coming-microsoft-is-dumping-legacy-code-for-winui/
- Windows Latest — WinUI Properties dialog: https://www.windowslatest.com/2026/07/22/first-look-windows-11-is-replacing-windows-95-era-properties-dialog-now-with-dark-mode-and-winui/
- KB5072911 — Explorer/Start/XAML-dependent shell apps: https://support.microsoft.com/en-us/topic/kb5072911-explorer-the-start-menu-and-other-xaml-dependent-apps-might-not-start-or-close-unexpectedly-on-some-enterprise-devices-d2d30684-4e2b-47f5-9899-a00a8e0acb09
- itm4n — Bypassing PPL in Userland (injection barriers): https://itm4n.github.io/bypassing-ppl-in-userland-again/
- Palo Alto Unit 42 — DLL hijacking / EDR detection: https://unit42.paloaltonetworks.com/dll-hijacking-techniques/
- Inspect (accessibility tool, UIA-based): https://learn.microsoft.com/en-us/windows/win32/winauto/inspect-objects
- Accessibility Insights for Windows (UIA-based): https://accessibilityinsights.io/docs/windows/overview/
- Files (files-community) — modern file manager on Windows.Storage/IShellItem: https://github.com/files-community/Files
