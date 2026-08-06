# Research: Overlay rendering alternatives

Resolves [#29](https://github.com/knausj85/Windows-Hinting/issues/29). Compares rendering stacks for the per-monitor, transparent, click-through hint overlays on Windows 11 with PerMonitorV2 DPI.

Candidates: current WinForms one-form-per-`Screen`, WPF, DirectComposition / composition windows, Win2D, WinUI 3.

Criteria: time-to-first-paint after the hotkey, transparency + click-through, per-monitor DPI correctness, multi-monitor lifecycle, text rendering quality at small sizes, agent-maintainability, and UIA invisibility of the overlay windows.

---

## 1. Current state (WinForms)

From `Windows-Hinting/Forms/OverlayForm.cs` and `Windows-Hinting/Forms/OverlayManager.cs`:

- **One borderless `Form` per `Screen`.** `OverlayManager.RebuildOverlays()` disposes and recreates the full set on construction and whenever `HintController.OnDisplaySettingsChanged` fires (driven by `WM_DISPLAYCHANGE`/`WM_SETTINGCHANGE` surfaced through `HotkeyWindow.DisplaySettingsChanged`). Hints are deactivated first, then overlays rebuilt — a simple, robust display-change lifecycle.
- **Window styles** (`CreateParams` override): `WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_NOACTIVATE`. So the overlay is: whole-window click-through, no Alt+Tab entry, layered, topmost, and never activates. `EnsureTopmost()` re-asserts `HWND_TOPMOST` with `SWP_NOACTIVATE` after showing.
- **Transparency is color-key, not per-pixel alpha.** `BackColor = TransparencyKey = Color.LimeGreen` — i.e. the layered window's `SetLayeredWindowAttributes(..., LWA_COLORKEY)` mode. Everything painted exactly LimeGreen is punched out; every other pixel is fully opaque. The `Color.FromArgb(alpha, ...)` values in `OnPaint` blend *toward the LimeGreen backing*, they do not produce true translucency over the desktop.
- **Text rendering:** GDI `TextRenderer` (ClearType) deliberately chosen over GDI+ `DrawString`, drawn on opaque near-black label chips (`FillRectangle` with a ~170-alpha black brush over the key color) with a pixel-sized bold font derived from `SystemFonts.CaptionFont` and the form's `DeviceDpi`. Font is rebuilt on `WM_DPICHANGED` / `WM_SETTINGCHANGE`.
- **DPI:** app manifest declares `PerMonitorV2,PerMonitor` (`Windows-Hinting/app.manifest`) and the csproj sets `ApplicationHighDpiMode=PerMonitorV2`. `AutoScaleMode.None`, `Bounds = Screen.Bounds` in physical pixels; all hint rects arrive in physical virtual-desktop pixels from UIA and are offset per screen. Pen widths and padding are scaled by `DeviceDpi / 96`.
- **External contract:** the window title `"Windows Hinting Overlay"` / `"... [Active]"` is consumed by external tools (Talon Voice) to detect the overlay. Any alternative stack must keep a discoverable, titled top-level HWND per overlay (or replace that contract).
- **UIA interaction today:** the element scan is rooted at the *foreground window's* HWND (`UIAutomationService` / `UIAutomationWrapper` call `ElementFromHandle(windowHandle)`), not at the desktop root. Overlays therefore never pollute the scan as long as they never become the foreground window — which `WS_EX_NOACTIVATE` guarantees. The overlay windows do still exist as top-level elements in a desktop-rooted UIA raw view (any top-level HWND does), so a future desktop-wide scan or `ElementFromPoint` usage is where stack choice matters (see §8).

### Known limitations of the current approach

- **No per-pixel alpha.** Color-key transparency gives hard edges. ClearType subpixel fringes and antialiased rectangle edges blend against LimeGreen before the key is punched out, so edge pixels that are not exactly the key color remain opaque — visible as green-tinged halos around glyph and chip edges over dark backgrounds. Translucent chips ("170 alpha black") are actually opaque dark-green, not see-through.
- **A stray LimeGreen pixel in a hint label would be punched through** (theoretical with the current palette, but an inherent color-key hazard).
- **SLWA layered windows keep a system-memory copy** for User32 hit-testing; final composition is still GPU-accelerated by DWM, so this costs little for a mostly static overlay. ([High-Performance Window Layering, MSDN Magazine](https://learn.microsoft.com/en-us/archive/msdn-magazine/2014/june/windows-with-c-high-performance-window-layering-using-the-windows-composition-engine))

What it does *well*: instant first paint (plain HWND + GDI, no framework spin-up), correct PerMonitorV2 behavior, trivially rebuildable on display change, real ClearType on the opaque chips, and a codebase any coding agent handles fluently.

---

## 2. Background: the three Win32 transparency mechanisms

All five stacks bottom out in one of three OS mechanisms ([Layered Windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features), [SetLayeredWindowAttributes](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes), [UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)):

| Mechanism | Alpha | Painting model | Used by |
|---|---|---|---|
| `WS_EX_LAYERED` + `SetLayeredWindowAttributes` (SLWA) | Color-key and/or single whole-window alpha; **no per-pixel alpha** (GDI drops alpha) | Normal `WM_PAINT`/GDI, OS redirects to a bitmap | WinForms `TransparencyKey`/`Opacity` (current) |
| `WS_EX_LAYERED` + `UpdateLayeredWindow` (ULW) | Full per-pixel alpha from an app-supplied premultiplied BGRA bitmap | No `WM_PAINT`; app pushes whole frames | WPF `AllowsTransparency` |
| `WS_EX_NOREDIRECTIONBITMAP` + DirectComposition | Full per-pixel alpha via GPU composition surfaces; no system-memory copy at all | DXGI flip-model swap chain presented to a DComp visual | DirectComposition/Win2D/WinUI 3 approaches |

The two layered modes are mutually exclusive per window ([SLWA remarks](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes)).

Click-through: on any layered window, color-keyed or zero-alpha areas already pass mouse input through; adding `WS_EX_TRANSPARENT` makes the *entire* window click-through regardless of content ([Layered Windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)). `WS_EX_NOREDIRECTIONBITMAP` windows hit-test uniformly (User32 never sees the pixels), so whole-window `WS_EX_TRANSPARENT` is the only click-through option there — which is exactly what this tool wants anyway ([MSDN Magazine article](https://learn.microsoft.com/en-us/archive/msdn-magazine/2014/june/windows-with-c-high-performance-window-layering-using-the-windows-composition-engine)). `HTTRANSPARENT` from `WM_NCHITTEST` is not a substitute: it only forwards input within the same thread ([WM_NCHITTEST](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nchittest)).

---

## 3. Option: WinForms (keep current stack)

- **Time-to-first-paint:** best-in-class. Plain HWND creation + GDI paint; forms are pre-created at startup and per display change, so the hotkey path is just `Invalidate()`.
- **Transparency/click-through:** color-key only (see §1 limitations). Click-through and no-activate fully supported and battle-tested here.
- **Per-monitor DPI:** solid. `HighDpiMode.PerMonitorV2` is first-class in modern .NET WinForms ([HighDpiMode](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.highdpimode), [WinForms high-DPI support](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/high-dpi-support-in-windows-forms)); this app bypasses auto-scaling anyway and works in physical pixels.
- **Multi-monitor lifecycle:** proven simple — dispose + recreate on `WM_DISPLAYCHANGE`.
- **Text quality:** real ClearType via GDI `TextRenderer` on the opaque chips; fringing only at chip/glyph boundary pixels against the key color ([ClearType antialiasing (GDI)](https://learn.microsoft.com/en-us/windows/win32/gdi/cleartype-antialiasing), [Raymond Chen on ClearType backgrounds](https://devblogs.microsoft.com/oldnewthing/20150129-00/?p=44803)).
- **Agent-maintainability:** excellent. WinForms + CsWin32 is abundant in training data; the whole overlay is ~360 lines of obvious code.
- **UIA:** WinForms exposes a modest accessibility tree (MSAA-bridged `ControlAccessibleObject`); a borderless form with no controls contributes almost nothing, and `WinFormsUtils`-level suppression (`AccessibleRole.None`, or not answering `WM_GETOBJECT`) is available if ever needed.

**Incremental upgrade within WinForms** (worth noting): switch the same window from SLWA to `UpdateLayeredWindow` — render hints into a GDI+ ARGB bitmap and push it with per-pixel alpha. Same HWND, same styles, same title contract, same lifecycle; fixes the halo/translucency issues; costs ClearType (per-pixel-alpha surfaces need grayscale AA or opaque chips) and ~100 lines of interop.

## 4. Option: WPF

- **Transparency:** `AllowsTransparency="True"` + `WindowStyle="None"` gives true per-pixel alpha via ULW ([Dwayne Need, "Transparent windows in WPF"](https://learn.microsoft.com/en-us/archive/blogs/dwayneneed/transparent-windows-in-wpf)).
- **Rendering path — the "software rendering" claim is outdated but replaced by a real cost:** since .NET 3.5 SP1-era fixes WPF renders layered windows on the GPU, then does a GPU→CPU readback (`GetRenderTargetData`) and calls `UpdateLayeredWindow` per dirty region. Dwayne Need measured up to ~30% CPU for a full-screen constantly-updating layered window; for a mostly static hint overlay this is negligible, but every prefix-filter repaint is a full readback of the dirty area ([same source](https://learn.microsoft.com/en-us/archive/blogs/dwayneneed/transparent-windows-in-wpf), [WPF perf improvements in 3.5 SP1](https://learn.microsoft.com/en-us/archive/blogs/jgoldb/whats-new-for-performance-in-wpf-in-net-3-5-sp1)). No evidence this pipeline changed in dotnet/wpf on modern .NET.
- **Click-through/no-activate:** not exposed by WPF; set `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` yourself via `WindowInteropHelper` + `SetWindowLong` — same P/Invoke as today, just less integrated than WinForms' `CreateParams`.
- **Per-monitor DPI:** built-in PerMonitorV2 since .NET Framework 4.6.2 / .NET Core 3; WPF handles `WM_DPICHANGED` and rescales content automatically ([microsoft/WPF-Samples PerMonitorDPI guide](https://github.com/microsoft/WPF-Samples/blob/main/PerMonitorDPI/readme.md)). Caveat for this app: WPF works in DIUs while the entire hint pipeline is physical-pixel-based — every hint rect needs a per-monitor divide by the DPI scale, and WPF's automatic rescale-on-DPI-change is something this app would fight rather than use (it wants physical bounds equal to `Screen.Bounds`).
- **Text quality:** **ClearType is disabled on transparent-background windows**; WPF falls back to grayscale AA, and `RenderOptions.ClearTypeHint` is explicitly documented as able to cause "rendering issues" when forced ([RenderOptions.ClearTypeHint](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.renderoptions.cleartypehint)). Use `TextOptions.TextFormattingMode="Display"` for pixel-snapped small text ([WPF text team guidance](https://learn.microsoft.com/en-us/archive/blogs/text/tips-for-improving-your-wpf-text-rendering-experience)). Net: small-label quality is *good but grayscale* — slightly softer than today's GDI ClearType chips.
- **Time-to-first-paint:** the framework tax is real ("don't expect it to be as fast as Win32 application or Winform. WPF simply load[s] more code off the disk" — [WPF perf team](https://learn.microsoft.com/en-us/archive/blogs/jgoldb/whats-new-for-performance-in-wpf-in-net-3-5-sp1); [official startup-time guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/application-startup-time)). Mitigable the same way as today: create the windows at startup, show/invalidate on hotkey. Cold start of the tray app gets slower; hotkey-to-paint need not.
- **Multi-monitor lifecycle:** no `Screen` class; use `SystemEvents.DisplaySettingsChanged` + Win32 `EnumDisplayMonitors` or WinForms' `Screen` via reference. Rebuild-on-change works the same.
- **Agent-maintainability:** very good — WPF is heavily represented in training data; XAML + code-behind for an overlay is idiomatic. Mixed physical/DIU coordinate math is the main foot-gun agents get wrong.
- **UIA:** WPF has a *full* UIA provider stack — every `Window` and element exposes rich `AutomationPeer`s ([UI Automation Overview / AutomationPeer](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview)). A WPF overlay injects a real UIA subtree at the desktop level; hint labels drawn via `DrawingVisual`/`OnRender` stay out of it, but any `TextBlock`-per-hint design would publish hundreds of text elements to UIA. Suppression requires overriding `OnCreateAutomationPeer` to return a null/empty peer. This is the stack most likely to *add* UIA noise if built naively.

## 5. Option: DirectComposition (`WS_EX_NOREDIRECTIONBITMAP` + D3D11/D2D/DWrite)

The technically ideal composition path, per Kenny Kerr's Microsoft-reviewed writeup ([High-Performance Window Layering](https://learn.microsoft.com/en-us/archive/msdn-magazine/2014/june/windows-with-c-high-performance-window-layering-using-the-windows-composition-engine)):

- **Transparency:** premultiplied per-pixel alpha with zero system-memory copies — DXGI swap chain (`CreateSwapChainForComposition`, `DXGI_ALPHA_MODE_PREMULTIPLIED`) on a DComp visual targeted at the HWND. "Pixel-perfect alpha blending on the desktop… incredibly fast."
- **Click-through:** whole-window only (User32 can't see composition pixels) — via `WS_EX_TRANSPARENT`, exactly matching this tool's requirement. `WS_EX_NOACTIVATE`/`WS_EX_TOOLWINDOW`/title contract all unchanged because it's still a plain Win32 HWND you create yourself.
- **Time-to-first-paint:** best possible after warm-up: no XAML framework; D3D/D2D device + swap chains created once at startup, hotkey path is a D2D draw + `Present` + `Commit`. Device-lost handling is the one new obligation.
- **Per-monitor DPI:** fully manual — which this app already is (physical pixels everywhere). On `WM_DPICHANGED`/display change, resize swap chain buffers and rescale the DirectWrite text format. No framework fighting you.
- **Text:** DirectWrite. Same rule as everywhere: rendering to a surface with alpha forces grayscale AA; drawing an opaque rect behind the text re-enables ClearType ([Direct2D supported pixel formats and alpha modes](https://learn.microsoft.com/en-us/windows/win32/direct2d/supported-pixel-formats-and-alpha-modes)). So hint chips can keep ClearType-quality text *and* gain alpha-blended chip edges — the best text outcome of any option. DirectWrite grayscale at small sizes is also excellent (it's what WinUI uses everywhere).
- **Multi-monitor lifecycle:** one HWND + swap chain per monitor, rebuilt on display change — same shape as today, plus swap-chain resize/recreate.
- **Agent-maintainability:** the weak point. No BCL wrapper; needs CsWin32 ([microsoft/CsWin32](https://github.com/microsoft/CsWin32) — already used by this repo), TerraFX ([terrafx.interop.windows](https://github.com/terrafx/terrafx.interop.windows)) or Vortice ([Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)). Expect several hundred lines of device/swap-chain/DComp plumbing plus device-lost paths. Coding agents handle D2D/DComp interop acceptably but with materially more review burden than WinForms/WPF; COM lifetime bugs are easy to introduce and hard to spot.
- **UIA:** best possible — a raw HWND with no accessibility framework exposes only a bare pane element (or nothing beyond the default OLEACC proxy); no content subtree at all. Also usable via the WinRT visual layer (`Windows.UI.Composition` + `ICompositorDesktopInterop`, Win10 1803+, [Using the Visual Layer with Win32](https://learn.microsoft.com/en-us/windows/uwp/composition/using-the-visual-layer-with-win32), [C# samples](https://github.com/microsoft/Windows.UI.Composition-Win32-Samples)).

## 6. Option: Win2D

- Win2D is a WinRT wrapper over Direct2D, now shipped for WinUI 3 / Windows App SDK as `Microsoft.Graphics.Win2D` ([microsoft/Win2D](https://github.com/microsoft/Win2D)).
- Its XAML controls (`CanvasControl`, `CanvasSwapChainPanel`) require a XAML tree; XAML-free use goes through `CanvasSwapChain`, which on WinUI 3 can present to an HWND ([Using Win2D without built-in controls](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/using-win2d-without-built-in-controls), [Win2D issue #915](https://github.com/microsoft/Win2D/issues/915)). Composition interop exists but requires dropping to the underlying `IDXGISwapChain` ([Win2D interop](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/interop)).
- **Net assessment:** for a transparent overlay, Win2D only replaces the Direct2D *drawing* layer of option §5 — the `WS_EX_NOREDIRECTIONBITMAP`/DComp/alpha plumbing remains yours — while adding a Windows App SDK runtime dependency to a tray app that currently has none. Text via `CanvasTextFormat` is DirectWrite with the same ClearType-on-alpha rules. It is a convenience layer that doesn't remove the hard part; not compelling here unless the app adopts WASDK for other reasons.

## 7. Option: WinUI 3 (Windows App SDK)

- **Transparent windows are an open feature gap.** The request for WPF-style `AllowsTransparency` is unresolved ([microsoft-ui-xaml #7276](https://github.com/microsoft/microsoft-ui-xaml/issues/7276)); the architectural reason is that XAML content lives in composition surfaces the HWND never sees, so the framework can't offer per-pixel transparency or hit-testing ([discussion #10746](https://github.com/microsoft/microsoft-ui-xaml/discussions/10746), [islands issue #2956](https://github.com/microsoft/microsoft-ui-xaml/issues/2956)). Community workarounds (WinUIEx, `SetWindowRgn` carving) exist; whole-window `WS_EX_TRANSPARENT` click-through works since it's still an HWND, but a genuinely transparent XAML background is the unsupported part.
- **Windowing niceties exist** (`OverlappedPresenter.IsAlwaysOnTop`, `AppWindow.IsShownInSwitchers` — [windowing overview](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/windowing/windowing-overview)) but don't compensate.
- **Startup:** heaviest of all candidates — WASDK runtime load + XAML parse before first paint; startup performance has its own best-practices doc and an active improvement backlog ([app startup performance](https://learn.microsoft.com/en-us/windows/apps/develop/performance/app-startup-performance), [perf discussion #11096](https://github.com/microsoft/microsoft-ui-xaml/discussions/11096)); multi-second cold starts are commonly reported ([Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/5776930/win-ui-3-0-windows-development-windows-app-sdk-iss)).
- **DPI:** PerMonitorV2 by default with auto-rescaling XAML, with known edge-case bugs ([#9327](https://github.com/microsoft/microsoft-ui-xaml/issues/9327)).
- **UIA:** like WPF, full automation peers on every element — a naive XAML hint overlay publishes a rich UIA subtree.
- **Agent-maintainability:** weakest — smallest training-data footprint, fast-moving APIs, and agents routinely trip over WASDK packaging/runtime issues.
- **Verdict:** the only candidate whose *core requirement* sits on an open bug. Not viable today.

## 8. UIA visibility constraint

The requirement: overlay windows must not pollute UIA scans of the target window.

1. **Every top-level HWND is a child of the UIA desktop root in the raw view** — no window style exempts it. `WS_EX_TOOLWINDOW` and `WS_EX_NOACTIVATE` affect taskbar/Alt+Tab/activation only ([extended window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)); filtering happens only in the control/content views via `IsControlElement`/`IsContentElement` ([UIA tree overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-treeoverview)). No documentation says DWM cloaking removes a window from the tree.
2. **Today's scan is naturally immune.** This app scans via `ElementFromHandle(foregroundHwnd)`; the overlay is never the foreground window (`WS_EX_NOACTIVATE`), so no stack choice breaks the *current* scan. The constraint bites for (a) other UIA clients (screen readers, other hinting tools) walking the desktop, and (b) any future desktop-rooted or point-based scan in this app.
3. **Point-based lookups skip the overlay.** UIA resolves `ElementFromPoint` by window hit-testing then `WM_GETOBJECT` ([WM_GETOBJECT](https://learn.microsoft.com/en-us/windows/win32/winauto/wm-getobject)), and window hit-testing ignores `WS_EX_TRANSPARENT` layered windows ([Layered Windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)). Well-supported inference, not a documented UIA guarantee — worth one verification pass with Accessibility Insights.
4. **Stack choice controls how *loud* the overlay is when enumerated.** A raw Win32/GDI/DComp HWND (or a bare WinForms form with no controls) exposes only a single default window element. WPF and WinUI 3 build a full `AutomationPeer` subtree automatically ([AutomationPeer](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.peers.automationpeer), [custom automation peers](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/custom-automation-peers)) — a naive per-hint `TextBlock` overlay would publish hundreds of text elements to every UIA client on the desktop. Suppression is possible (`AutomationProperties.AccessibilityView="Raw"`, overriding `OnCreateAutomationPeer`, WPF `IsOffscreenBehavior`) but is extra, easy-to-regress work.
5. **Cheapest robust defenses regardless of stack:** draw hints as pixels (owner-draw / `DrawingVisual` / D2D), not as UI elements; optionally answer `WM_GETOBJECT` with a minimal provider reporting `IsControlElement = IsContentElement = FALSE` ([UiaHostProviderFromHwnd](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiahostproviderfromhwnd)); and filter this process's own HWNDs/PID out of any future desktop-rooted scan.

## 9. What comparable tools use

| Tool | Overlay rendering stack | Notes |
|---|---|---|
| [win-vind](https://github.com/pit-ray/win-vind) (EasyClick hints) | Raw Win32 GDI — memory DC + `TextOutW` + `BitBlt` ([screen_textrender.cpp](https://github.com/pit-ray/win-vind/blob/master/src/util/screen_textrender.cpp), [hinter.cpp](https://github.com/pit-ray/win-vind/blob/master/src/bind/mouse/hinter.cpp)) | Closest analogue to this project; proves plain GDI hinting ships |
| [mouseable](https://github.com/wirekang/mouseable) | Raw Win32 `CreateWindowEx(WS_EX_TOOLWINDOW\|WS_EX_TOPMOST\|WS_EX_NOACTIVATE)` + GDI ([overlay/windows.go](https://github.com/wirekang/mouseable/blob/main/internal/overlay/windows.go)) | No layered per-pixel alpha found |
| PowerToys Find My Mouse / Mouse Highlighter / Crosshairs | `WS_EX_TRANSPARENT \| WS_EX_LAYERED \| WS_EX_TOOLWINDOW` host + **Windows Composition API** visuals ([FindMyMouse.cpp](https://github.com/microsoft/PowerToys/blob/main/src/modules/MouseUtils/FindMyMouse/FindMyMouse.cpp), [mouseutils devdocs](https://github.com/microsoft/PowerToys/blob/main/doc/devdocs/modules/mouseutils/readme.md)) | The first-party template for a click-through composition overlay |
| PowerToys Text Extractor | WPF full-screen `OCROverlay` per monitor ([textextractor devdoc](https://github.com/microsoft/PowerToys/blob/main/doc/devdocs/modules/textextractor.md)) | WPF used where the overlay is an *interactive* UI |
| PowerToys Mouse Jump | WinForms ([mouseutils devdocs](https://github.com/microsoft/PowerToys/blob/main/doc/devdocs/modules/mouseutils/readme.md)) | WinForms still shipping in first-party overlay tooling |
| PowerToys Peek | WinUI 3 / Windows App SDK ([Peek.UI.csproj](https://github.com/microsoft/PowerToys/blob/main/src/modules/peek/Peek.UI/Peek.UI.csproj)) | A normal app window, not a transparent overlay |
| Talon Voice (mouse grid / canvas) | Skia via Talon's proprietary `talon.canvas` host; host window internals closed-source ([community mouse_grid.py](https://github.com/talonhub/community/blob/main/core/mouse_grid/mouse_grid.py)) | Immediate-mode pixel drawing, no per-hint UI elements |
| Fluent Search (Screen Search) | .NET 8 + Avalonia (Skia compositor); implementation closed ([repo](https://github.com/adirh3/Fluent-Search)) | UIA + vision hybrid element detection |
| warpd | No Windows backend (X11/Xft, Wayland/cairo, macOS Cocoa only) ([repo](https://github.com/rvaiya/warpd)) | Not a Windows data point |
| GridMove | AutoHotkey GUI (AHK-managed layered windows) ([repo](https://github.com/jgpaiva/GridMove)) | |

**Pattern:** hint/highlight layers are drawn as *pixels* (GDI, Skia, or composition visuals) on raw click-through HWNDs. Nobody discoverable uses WPF or WinUI XAML for the hint layer itself; XAML stacks appear only for interactive overlay UIs. PowerToys' mouse utilities demonstrate the `WS_EX_TRANSPARENT|WS_EX_LAYERED|WS_EX_TOOLWINDOW` + Windows Composition combination in production.

## 10. Comparison table

| Criterion | WinForms (current, SLWA color-key) | WinForms + ULW (incremental) | WPF | DirectComposition | Win2D | WinUI 3 |
|---|---|---|---|---|---|---|
| Time-to-first-paint after hotkey | Excellent (pre-created HWNDs, GDI) | Excellent | Good if windows pre-created; framework cold-start tax on app launch | Best (GPU present, no framework) | Same as DComp + WASDK load | Worst (WASDK + XAML spin-up) |
| Transparency | Color-key only; hard edges, key-color halos | Full per-pixel alpha | Full per-pixel alpha (ULW readback per repaint) | Full per-pixel alpha, zero-copy | Full (via DComp plumbing you still write) | **Unsupported (open issue)** |
| Click-through / no-activate | Proven (`WS_EX_TRANSPARENT`/`NOACTIVATE`) | Same | Same styles via interop | Whole-window only — exactly what's needed | Same as DComp | `WS_EX_TRANSPARENT` works; transparent background doesn't |
| Per-monitor DPI (PerMonitorV2) | Solid; app already physical-pixel-native | Same | Built-in but DIU-based — fights this app's physical-pixel pipeline | Fully manual — matches app's model | Manual | Default, some open bugs |
| Multi-monitor lifecycle | Proven dispose/recreate on `WM_DISPLAYCHANGE` | Same | Same pattern, no `Screen` API | Same + swap-chain resize & device-lost handling | Same | Same, heavier windows |
| Text at small sizes | GDI ClearType on opaque chips (best today), keyed edges | Grayscale AA (or ClearType on opaque chips via GDI interop) | Grayscale AA only on transparent windows | DirectWrite: ClearType on opaque chips **and** alpha edges — best overall | DirectWrite (same) | DirectWrite grayscale |
| Agent-maintainability | Best | Good (small interop delta) | Very good | Moderate–weak (COM/DComp plumbing) | Weak (niche + WASDK) | Weakest |
| UIA footprint of overlay | Minimal (bare form, MSAA bridge) | Minimal | Rich UIA tree unless suppressed | Minimal (bare pane / none) | Minimal if no XAML | Rich UIA tree unless suppressed |
| Keeps Talon title contract | Yes | Yes | Yes (`Window.Title`) | Yes (own HWND) | Yes | Yes |

## 11. Facts for the decision

1. **The current WinForms approach is architecturally sound for every hard requirement** — instant paint, proven click-through/no-activate, correct PerMonitorV2, simple display-change rebuild, minimal UIA footprint, and the Talon title contract. Its only real deficiency is *visual*: color-key transparency means no per-pixel alpha (hard edges, key-color halos, fake translucency).
2. **WinUI 3 is disqualified**: transparent overlay windows are an open, unresolved feature gap ([microsoft-ui-xaml #7276](https://github.com/microsoft/microsoft-ui-xaml/issues/7276)), plus the heaviest startup and the richest unwanted UIA tree.
3. **Win2D adds a Windows App SDK dependency without removing the hard part** (the DComp/alpha plumbing); only worth revisiting if WASDK is adopted for other reasons.
4. **WPF works but is the odd fit**: per-pixel alpha comes with mandatory grayscale text on transparent windows, a GPU→CPU readback on every repaint, DIU coordinates fighting the app's physical-pixel pipeline, and an auto-generated UIA subtree that must be actively suppressed. Notably, no surveyed hinting tool uses WPF for its hint layer.
5. **If per-pixel alpha is wanted, there are two credible upgrade paths, in increasing effort:**
   - *WinForms + `UpdateLayeredWindow`*: same forms, same styles, same lifecycle and title contract; render hints to an ARGB bitmap and push it. Smallest diff to full alpha; text becomes grayscale AA (or keep GDI ClearType by compositing opaque chips into the ARGB surface).
   - *DirectComposition (`WS_EX_NOREDIRECTIONBITMAP` + D2D/DWrite)*: the technically best endpoint — zero-copy GPU alpha, fastest paint, best text (ClearType on opaque chips + alpha-blended edges), minimal UIA footprint — at the cost of several hundred lines of COM/DComp plumbing and device-lost handling, the option coding agents handle least reliably. This is what PowerToys' mouse overlays effectively use (Composition API on a click-through host).
6. **UIA invisibility is mostly independent of the stack** as long as hints are drawn as pixels, not UI elements: no style hides an HWND from the raw view; `WS_EX_TRANSPARENT` keeps it out of point hit-tests; XAML stacks (WPF/WinUI) are the only ones that inject rich subtrees by default.
7. **Time-to-first-paint is dominated by pre-creating windows, not by the stack**: every option can hit "instant" if overlays are created at startup/display-change and only invalidated on hotkey — but WPF/WinUI tax app cold-start, and only the Win32-family options (WinForms, DComp) keep the hotkey path free of framework machinery.
