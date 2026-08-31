# Findings — auto-hint detection spike (#45)

Manual HITL sweep on a real Win11 machine (2026-08-31), driven live through the
probe on branch `prototype/auto-hint-detection`. Raw evidence:
[`captured-sweep-2026-08-31.log`](captured-sweep-2026-08-31.log) (490 lines).

## Verdict

The working hypothesis — **WinEvent `MENUSTART` as the trigger + UIA `MenuOpened`
for classification** — is **wrong for modern Win11 surfaces.** Across the whole
session:

- **UIA `MenuOpened` fired 0 times** for every shell surface (Start, Search,
  Notification Center, Control Center, tray overflow, jump list, Task View,
  Snap Assist, desktop context menu, calendar flyout).
- **`SYSTEM_MENUPOPUPSTART` fired 0 times** for any shell surface; `SYSTEM_MENUSTART`
  only fired for legacy Win32 hosts (WindowsTerminal, GitHub Desktop's native
  context popup).

**The reliable trigger for shell surfaces is `EVENT_SYSTEM_FOREGROUND`**, keyed by
**window class + title + host process**. Freshly-created surfaces additionally emit
`EVENT_OBJECT_SHOW` (open) and `EVENT_OBJECT_HIDE`/`DESTROY` (close). UIA menu
events are a **secondary** signal that only works for true menu objects (native
WPF/Win32 menus and Win11 XAML `ApplicationBar` menus), never for Electron in-DOM
menus or popup-hosted flyouts.

## Event → surface map (v1 catalog)

Latency was ~0 ms for almost everything (a few `OBJECT_SHOW` at 15–250 ms). All
surfaces on `explorer.exe` unless noted.

| Surface | Trigger event(s) | Window class | Title | Host process | Close signal |
|---|---|---|---|---|---|
| **Start menu** (Win) | `SYSTEM_FOREGROUND` | `Windows.UI.Core.CoreWindow` | `Search` | **SearchHost.exe** | (foreground away) |
| **Search** (Win+S) | `SYSTEM_FOREGROUND` (+ PopupHost precursor) | `Windows.UI.Core.CoreWindow` | `Search` | **SearchHost.exe** | (foreground away) |
| **Notification Center** (Win+N) | `SYSTEM_FOREGROUND` | `Windows.UI.Core.CoreWindow` | `Notification Center` | **ShellExperienceHost.exe** | (foreground away) |
| **Calendar flyout** (click clock) | `SYSTEM_FOREGROUND` | `Windows.UI.Core.CoreWindow` | `Notification Center` | ShellExperienceHost.exe | — |
| **Control Center** (Win+A) | `SYSTEM_FOREGROUND` **+ `OBJECT_SHOW`** | `ControlCenterWindow` | `Quick Settings` | **ShellHost.exe** | `OBJECT_HIDE` |
| **System-tray overflow** (^) | `SYSTEM_FOREGROUND` **+ `OBJECT_SHOW`** | `TopLevelWindowForOverflowXamlIsland` | `System tray overflow window.` | explorer | `OBJECT_HIDE` |
| **Taskbar jump list** (right-click pinned) | `SYSTEM_FOREGROUND` | `Windows.UI.Core.CoreWindow` | `Jump List for <app>` | **ShellExperienceHost.exe** | (foreground away) |
| **Task View** (Win+Tab) | `SYSTEM_FOREGROUND` + full `CREATE/SHOW/HIDE/DESTROY` | `XamlExplorerHostIslandWindow` | `Task View` | explorer | `OBJECT_HIDE`/`DESTROY` |
| **Snap Assist** (drag-snap) | `SYSTEM_FOREGROUND` + `CREATE/SHOW` | `XamlExplorerHostIslandWindow` | `Snap Assist` | `OBJECT_HIDE` |
| **Alt-Tab switcher** (bonus) | `SYSTEM_FOREGROUND` + `CREATE/SHOW` | `XamlExplorerHostIslandWindow` | `Task Switching` | `OBJECT_HIDE` |
| **Desktop context menu** (right-click desktop) | **`OBJECT_CREATE` + `OBJECT_SHOW`** (no menu events) | `Xaml_WindowedPopupClass` | `PopupHost` | explorer | `OBJECT_HIDE`/`DESTROY` |
| **File Explorer context menu** | `SYSTEM_MENUPOPUPSTART` **+ UIA `MenuOpened`** | `InputSiteWindowClass` (UIA `ApplicationBar`) | — | explorer | `MENUPOPUPEND` |

### Noise / gotchas observed

- **`Xaml_WindowedPopupClass` / "PopupHost" is heavily overloaded.** It is the
  desktop context menu *and* a transient precursor that flashes before Start /
  Search / NC / Control Center / calendar. A bare PopupHost cannot be classified
  on its own — needs a secondary discriminator (its UIA content, or "is a
  CoreWindow foreground about to follow?").
- **`XamlExplorerHostIslandWindow` hosts ≥3 surfaces** (Task View, Snap Assist,
  Task Switching), disambiguated **only by title** — and **the title is empty on
  `OBJECT_CREATE`**, populated only by `SYSTEM_FOREGROUND`/`OBJECT_SHOW` (~100 ms
  later). **Classify on FOREGROUND/SHOW, never on CREATE.**
- **Same host, different surface:** ShellExperienceHost hosts both Notification
  Center and jump lists (discriminate by title); SearchHost hosts both Start and
  Search (see below).
- Duplicate + slightly out-of-order events are normal (e.g. a `MenuClosed` arrived
  6 ms *before* its `MenuOpened`). Consumers must be **idempotent**.
- `EVENT_OBJECT_DESTROY` (0x8001) is a firehose and must be inside the class
  allow-list filter (fixed mid-sweep — it initially bypassed it).

## The Start-vs-Search ambiguity (unresolved by window props)

Pressing **Win** (Start) and **Win+S** (Search) produce an **identical** window:
`Windows.UI.Core.CoreWindow` / title `Search` / `SearchHost.exe`. They cannot be
told apart by class, title, or process. Options for the spec:
1. **Merge them** into one "launcher" catalog entry (simplest; hint the same way).
2. Disambiguate via the **UIA parent chain** (Talon's seed keyed on
   `parent.parent.name == "Start"`) — needs a delayed UIA probe, unverified here.
3. Disambiguate by **which trigger fired** (hotkey/taskbar button) — not available
   from WinEvent alone.

Recommendation: **(1)** for v1.

## MenuOpened / MenuClosed reliability matrix

| App | Menu implementation | UIA `MenuOpened` | UIA `MenuClosed` | `MENU(POPUP)START` | Usable signal |
|---|---|---|---|---|---|
| **Visual Studio** | native WPF/Win32 | ✅ full data (`MenuBar`/`Menu`, `ContextMenu`) | ✅ full data | ✅ rich focus (`MenuItem`/`MenuBar`) | **UIA or WinEvent — both good** |
| **File Explorer** | Win11 XAML `ApplicationBar` | ✅ (`Menu`/`ApplicationBar`) | ⚠️ mixed (some null/`Idle`) | ✅ rich focus (`MenuItem "Cut"`) | **WinEvent `MENUPOPUPSTART` + focused element** |
| **GitHub Desktop** | Electron, native OS popup | ⚠️ fires but source = Chromium `Pane`/`Chrome_WidgetWin_1` (useless) | ❌ null | ✅ `MENUSTART`/`MENUPOPUPSTART` fire | **WinEvent only; UIA source is garbage** |
| **VS Code** | Electron, in-DOM menu | ❌ never | ⚠️ fires, **null source** | ❌ END only, no START | **none reliable — hardest case** |

**Chosen fallback per menu type:**
- Native menus (Visual Studio) → UIA `MenuOpened` is clean; WinEvent is a fine backup.
- Win11 XAML menus (File Explorer) → **WinEvent `SYSTEM_MENUPOPUPSTART`** + read the
  focused element (`MenuItem` + parent `Menu`). UIA `MenuOpened` also works.
- Electron native popups (GitHub Desktop) → **WinEvent `MENUSTART`/`MENUPOPUPSTART`**;
  ignore UIA source (it's the Chromium pane).
- Electron in-DOM menus (VS Code) → **no reliable OS signal.** Out of scope for v1;
  would need app-specific handling. Flag as a known gap.

## Classification-rule refinements (vs the Talon `update_state` seed)

The ported seed (`Classify()` in `Program.cs`) mostly held, with these corrections
the live data forced:

1. **Key on window *title*, not the focused-element name.** The seed matched
   Notification Center on `focused.name == "Notification Center"`; the focus
   snapshot **races the foreground event** and was frequently stale (often showed
   the *previous* window). NC misclassified as AMBIGUOUS when opened via the
   calendar because focus was empty. → Match NC/jump-list/etc. on the **window
   title**; only consult the focused element after a **~50–100 ms delay**
   (exactly why the Talon prototype uses a cron delay).
2. **Add `TopLevelWindowForOverflowXamlIsland`** as the system-tray-overflow class
   (the seed only checked the title string).
3. **`XamlExplorerHostIslandWindow` → disambiguate by title only, on FOREGROUND/SHOW**
   (title is empty at CREATE). Add `Task Switching` (Alt-Tab) if in scope.
4. **Desktop/modern context menu = `Xaml_WindowedPopupClass`/"PopupHost" via
   `OBJECT_CREATE`+`SHOW`**, not a menu event — matches the seed's `on_win_open`
   PopupHost branch, and confirms menu WinEvents do **not** fire for it.
5. **Start ≡ Search** — collapse to one entry (see above).

## Recommended detection architecture for the spec (`SurfaceWatch`)

- One `SetWinEventHook` over `SYSTEM_FOREGROUND..MENUPOPUPEND` (0x0003–0x0007) +
  one over `OBJECT_CREATE..HIDE` (0x8000–0x8003), `WINEVENT_OUTOFCONTEXT` on a
  thread with a message pump.
- **Primary classifier: on `SYSTEM_FOREGROUND` and `OBJECT_SHOW`**, switch on
  window class → title → host process (the table above becomes the catalog data).
- **Cheap relevance filter is mandatory** for the object range — a window-class
  allow-list (see `ObjectEventClassAllowList`) cut the firehose to a trickle.
- **Debounce ~50 ms** before enumerating (matches the surface's own show latency
  and lets the title/UIA tree settle); guard against the duplicate/out-of-order
  and PopupHost-precursor churn with per-hwnd idempotency.
- UIA menu events (`MenuOpened`) are worth subscribing to **only** as an
  augmentation for native + XAML-ApplicationBar menus; do not depend on them.
