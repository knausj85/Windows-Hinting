# AutoHintDetectionProbe — THROWAWAY

Detection spike for wayfinder ticket
[#45](https://github.com/knausj85/Windows-Hinting/issues/45) (parent map #44).
**Not** part of `Windows-Hinting.sln`, not referenced by the app, never merges.
Lives on branch `prototype/auto-hint-detection`.

## The question

What events signal that a transient shell surface appeared, and what concrete
per-surface classification rules follow from the data those events expose?
Detection + classification are entangled, so this resolves both at once, by hand,
on a real Win11 machine.

## Run

```powershell
dotnet run --project prototype/AutoHintDetectionProbe
```

It installs:

- **WinEvent hooks** — `SYSTEM_FOREGROUND`, the menu lifecycle
  (`MENUSTART`/`MENUEND`/`MENUPOPUPSTART`/`MENUPOPUPEND`), and object
  `CREATE`/`SHOW`/`HIDE`. The object stream is filtered to a window-class
  allow-list (the "cheap relevance filter" the map flagged as unspecified) —
  see `ObjectEventClassAllowList` in `Program.cs`.
- **Managed UIA** `MenuOpenedEvent` / `MenuClosedEvent`, subtree at the desktop
  root — the reliability test.

Every event logs: event name, **delivery latency**, hwnd, window class, title,
process, the **focused-element control type + name + parent** (the fields the
Talon `update_state` heuristic keys off), and a **proposed classification** so
you can eyeball whether the seed rules hold.

Output goes to the console **and** `auto-hint-detection-probe.log` in the working
directory.

## Drive it (the manual part)

Type a label + Enter to drop a `MARK` divider in the log right before you open a
surface, so events are easy to attribute. Then walk the checklist:

**v1 catalog** — Start menu · Search · Notification Center · Control Center ·
System-tray overflow · taskbar jump list (right-click a pinned app) · Task View ·
Snap Assist (Win+Z or drag-to-snap) · shell context menu (right-click desktop) ·
a Win11 XAML flyout.

**MenuOpened / MenuClosed reliability matrix** — open a menu bar and a context
menu in **each** of: VS Code · GitHub Desktop · Visual Studio · File Explorer.
Record whether UIA `MenuOpened`/`MenuClosed` actually fires, and any lag. Where it
stays silent, note which WinEvent (`MENUSTART` / `OBJECT_SHOW`) fired instead —
that is the fallback signal for that surface.

`Ctrl+C` to quit.

## What to capture back on #45

- Event→surface map for the v1 catalog (which events fire, latency, noise).
- The MenuOpened/MenuClosed app-reliability matrix + chosen fallback per surface.
- The refined per-surface classification rules (window class + focused-element /
  parent predicates), starting from the `Classify()` seed in `Program.cs`.

Paste the relevant `MARK`-delimited log sections as evidence.
