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

Recommendation: ~~**(1)** for v1.~~ **Superseded by Round 2 (below): `UIA_Window_WindowOpenedEventId`
disambiguates them** — Start raises a WindowOpened for `name='Start'` /
`StartMenuExperienceHost.exe`, Search for `name='Search'` / `SearchHost.exe`.
Prefer disambiguating over merging.

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
- Electron in-DOM menus (VS Code) → ~~**no reliable OS signal.**~~ **Superseded by
  Round 3: UIA `FocusChanged` exposes the `MenuItem` elements by name** (the only
  channel that does). Not a single "opened" event, but a usable signal.

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
- **Add one COM `UIA_Window_WindowOpenedEventId` subscription** (subtree at root)
  **solely to disambiguate the Start/Search launcher** — see Round 2. Do not use
  it as the primary trigger (it lags the WinEvent by ~800 ms and is silent for
  Control Center / popup-hosted menus), and do not use `WindowClosed` for close
  detection (firehose of null-source events).

---

## Round 2 — native COM UIA events (revisit, per follow-up)

Tested whether four events the managed client can't express are viable in the
**problem apps** (VS Code, GitHub Desktop): `UIA_Window_WindowOpenedEventId`,
`UIA_Window_WindowClosedEventId`, `UIA_MenuModeStartEventId`,
`UIA_MenuModeEndEventId`. Subscribed via the native `CUIAutomation` COM client
(the managed `System.Windows.Automation` exposes none of these). Raw evidence:
[`captured-sweep-round2-2026-08-31.log`](captured-sweep-round2-2026-08-31.log).

**Build note:** the COM `<COMReference>` (tlbimp) forces a **full-framework MSBuild**
build — `dotnet build`/`dotnet run` fail with MSB4803. See README / csproj.

### Tallies (whole round-2 session)
| Event | Count | Verdict |
|---|---|---|
| `MenuModeStart` | **0** | **Dead** — never fired for *any* app, incl. native Visual Studio. Not viable. |
| `MenuModeEnd` | 2 | GitHub Desktop only, **null source, no matching Start**. Useless. |
| `Win.WindowOpened` | 4 | Fires with **clean data** for real top-level windows. **The one win.** |
| `Win.WindowClosed` | 25 | Mostly null-source background noise; unusable as a close signal. |

### The problem apps — not rescued
- **VS Code (Electron, in-DOM menus):** `MenuModeStart/End` never fired;
  `Window_*` never fired for its menus. The in-DOM menu creates **no OS window and
  enters no menu mode** — there is genuinely no OS-level signal. Conclusive.
- **GitHub Desktop (Electron, native context popup):** `MenuModeStart` never fired
  (`MenuModeEnd` did, null, no start). The only usable *open* signal stays the
  WinEvent `SYSTEM_MENUSTART`/`MENUPOPUPSTART` (native popup) from Round 1.

### The genuine win — `Win.WindowOpened` resolves Start vs Search
Round 1 concluded Start and Search were indistinguishable (both
`CoreWindow`/"Search"/SearchHost via `SYSTEM_FOREGROUND`). **`Win.WindowOpened`
tells them apart:**
- Start (Win key) → `WindowOpened ctl=Window name='Start' class='Windows.UI.Core.CoreWindow' proc=StartMenuExperienceHost.exe`
- Search (Win+S) → `WindowOpened ctl=Window name='Search' class='Windows.UI.Core.CoreWindow' proc=SearchHost.exe`

So the launcher can be split by **window name + host process** after all. Caveats:
it arrives **~800 ms after** the `SYSTEM_FOREGROUND` (slower), and it is **silent
for Control Center (`ControlCenterWindow`) and the PopupHost desktop context menu**
— so it is a targeted supplement, not a replacement. `WindowOpened` also fires for
`XamlExplorerHostIslandWindow` but with an empty name (title still comes from the
WinEvent path).

### Net
- **Drop `MenuModeStart`/`MenuModeEnd`** from consideration — dead on modern Win11.
- **Adopt `UIA_Window_WindowOpenedEventId`** narrowly, to disambiguate Start/Search
  (updates the Round-1 "merge them" call to "disambiguate them").
- ~~The problem apps (Electron) remain a known v1 gap.~~ **See Round 3 — UIA
  `FocusChanged` closes most of the Electron gap.**

---

## Round 3 — `EVENT_OBJECT_FOCUS` + UIA `FocusChanged` (revisit, per follow-up)

Question: do focus-changed events (a) fix the **stale synchronous focus snapshot**
that dogged Rounds 1–2, and (b) give a signal in **VS Code's in-DOM Electron menu**
where every window/menu event failed? Added `EVENT_OBJECT_FOCUS` (0x8005) WinEvent
+ managed UIA `FocusChanged`. Raw evidence:
[`captured-sweep-round3-2026-08-31.log`](captured-sweep-round3-2026-08-31.log).

### Result — both, yes. This is the round that closes the Electron gap.

**UIA `FocusChanged` rescues the VS Code in-DOM menu** — it is the *only* channel
that exposes the menu items. As focus moves through the menu (arrow keys), it fires
with full data:
- Menu bar → `MenuItem name='File' / 'Edit' / 'View' / 'Go' / 'Run' / 'Terminal'`
  (class `action-menu-item monaco-submenu-item`).
- File dropdown → `MenuItem name='New Text File Ctrl+N' / 'New Window Ctrl+Shift+N' …`
  (class `action-menu-item`); container `Menu name='More' class='actions-container'`.
- Context menu → `MenuItem name='Go to Definition F12'`.

It also **fixes the stale-focus problem**: `FocusChanged` delivers the *correct*
focused element at event time — `Edit "Search box"` for Start, `MenuItem "View" /
"Sort by"` (class `AppBarButton`) for the Win11 desktop context menu (which in
Round 1 gave *only* a `PopupHost` object-show with no item detail). No polling,
no 50 ms delay needed for the element itself.

### `WINEVENT OBJECT_FOCUS` is the coarser cousin
It fires (64× in the session) but its hwnd is always the **host window**
(`Chrome_RenderWidgetHostHWND` for Electron; `InputSiteWindowClass` / `CoreWindow`
for shell). It says "focus moved inside this window" but does **not** surface the
specific `MenuItem` without decoding `idObject`/`idChild` via `AccessibleObjectFromEvent`.
**Prefer UIA `FocusChanged` for the element; treat `OBJECT_FOCUS` as a cheap
"something focus-changed here" backstop.**

### Caveats
- **High volume.** Desktop focus alone bounces through `Progman` →
  `SHELLDLL_DefView` → `SysListView32` and back; every keystroke can fire. A
  consumer **must filter** — by control type (`Menu`/`MenuItem`) and/or target
  process — and dedupe (the probe suppresses consecutive duplicates and still
  logged 54 `FocusChanged` / 64 `OBJECT_FOCUS` in a short run).
- **Not an "opened" event.** `FocusChanged` reports items *as focus lands on them*,
  one at a time — it does not announce "a menu opened." For auto-hint, use it two
  ways: (1) focus landing on a `Menu`/`MenuItem` control type inside the target =
  "a menu is active" trigger; (2) then enumerate the menu's children via the UIA
  tree rather than waiting for focus to step through each. This is exactly the
  Talon seed's `on_element_focus` model — Round 3 validates it.

### Net
- **Adopt UIA `FocusChanged`** as the element-level signal: it fixes stale focus
  everywhere and is the **only** viable hook for Electron in-DOM menus (VS Code).
- Keep `EVENT_OBJECT_FOCUS` only as a coarse backstop; UIA is richer.
- Revises Round 1/2: the Electron in-DOM menu is **no longer a hard gap** — items
  are reachable via `FocusChanged` (open-detection still weaker than native).
