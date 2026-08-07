# Manual smoke test

The committed manual verification script required by the
[testing decision (#36)](https://github.com/knausj85/Windows-Hinting/issues/36). It covers the
inviolable hint workflow and everything that is manual-only (rendering, tray, hooks, updater).

Each migration phase's definition of done names the sections that must pass. The **full script** is
the pre-release ritual and the done-criterion for phases that touch the host broadly.

**Setup:** a Debug or Release build of the branch under test, on a machine with **at least two
monitors** (one is acceptable only when the phase's changes cannot affect multi-monitor layout —
note it in the PR). Default preferences unless a section says otherwise.

## 1. Core hint workflow (inviolable)

1. Launch the app. Expect: tray icon appears; no windows open; no hint overlay visible.
2. Focus a window with clickable elements (e.g. File Explorer). Press the hint hotkey
   (default `Ctrl+Alt+H`). Expect: labeled hints appear over the window's interactive elements.
3. With hints showing, type a label's first character. Expect: hints not matching the prefix
   disappear; matching hints remain, with the typed prefix visually distinguished.
4. Type the rest of a label. Expect: the hinted element activates (button clicks, link opens);
   hints disappear; the target window keeps/receives focus.
5. Press the hotkey again, then press the dismiss key (`Esc`). Expect: hints disappear, nothing
   activates.
6. While typing a prefix, verify the keystrokes do **not** reach the underlying application
   (input capture works while hints are active).

## 2. Multi-monitor

1. Arrange a window spanning or on a secondary monitor (mixed DPI if available).
2. Trigger hints. Expect: hints render on every monitor where the target window has elements,
   positioned correctly (no offset drift on high-DPI screens).

## 3. Foreground-change dismissal

1. Trigger hints on a window.
2. Alt-Tab (or click) to a different window without selecting a hint. Expect: hints dismiss
   automatically.

## 4. Taskbar mode

1. Trigger taskbar hints (taskbar hotkey or tray menu).
2. Expect: hints on taskbar buttons; selecting one activates that taskbar item.
3. With window hints showing, trigger taskbar mode. Expect: window hints are replaced by taskbar
   hints (toggle the shown source → off; toggle the other → switch).

## 5. Click actions

1. Trigger hints; select a label using each configured click-action modifier (e.g. right-click,
   double-click variants per preferences). Expect: the corresponding action occurs on the element.

## 6. Tray and lifecycle

1. Open the tray menu. Expect: entries render, hints can be toggled from the menu, About and
   preferences open.
2. Exit via the tray menu. Expect: process exits cleanly; overlay windows and hooks are gone
   (hotkey no longer does anything).

## 7. Preferences apply

1. Open preferences; change the hint hotkey. Expect: the new hotkey shows hints; the old one does
   not — without an app restart.
2. *(After phase 07)* Hand-edit `preferences.json` while the app runs. Expect: the change applies
   within a few seconds (watcher path).
3. *(After phase 07)* Corrupt `preferences.json` (e.g. truncate it) and restart the app. Expect:
   file renamed to `.bak`, defaults in effect, tray notification explains what happened.

## 8. Logging and log viewer

1. Enable file logging in preferences; trigger hints once. Expect: a log file appears under
   `%AppData%\Windows-Hinting\logs` with fresh entries.
2. Open the log viewer from the tray. Expect: recent events visible; live entries appear as you
   toggle hints.

## 9. Updates (Release builds only)

1. On a build wired to an update channel with a newer version available: expect the update is
   detected and — after phase 09 — downloads in the background, then presents a single
   "restart to install?" prompt. Declining leaves the app running normally.
2. Portable build: verify the portable self-update path replaces the exe and relaunches.

## 10. UIAccess (installed builds only)

1. Install the signed MSI; launch from Program Files.
2. Open the Start menu and trigger hints. Expect: hints render **above** the Start menu / other
   elevated surfaces (UIAccess working).
