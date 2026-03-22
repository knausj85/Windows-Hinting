# Windows-Hinting

A keyboard-driven accessibility overlay for Windows. Displays hint labels on clickable UI elements so you can activate them by typing, without reaching for the mouse. Works well with [Talon Voice](https://talonvoice.com/), Stream Deck, and other automation tools.

## How It Works

1. Press the global hotkey to activate hints.
2. Labeled tags appear on every clickable element in the foreground window.
3. Type a label (e.g., `A`, `AB`) to narrow down to a single element.
4. Press `Space` to activate it, or use a `Shift+key` shortcut to activate with a specific click type.

## Global Hotkeys

| Shortcut | Action |
|---|---|
| `Ctrl+Alt+H` | Toggle hints for the foreground window |
| `Ctrl+Alt+T` | Toggle hints for the taskbar |

These hotkeys are configurable via the Preferences dialog (right-click the tray icon → Preferences).

## Hint Navigation

While hints are active:

| Key | Action |
|---|---|
| `A`–`Z` | Append to the hint filter |
| `Backspace` | Remove last filter character |
| `Escape` | Clear the filter |
| `Space` | Commit selection (default activation via UI Automation) |

## Click Action Shortcuts

While hints are active, use `Shift+key` to toggle the pending click action. The tray icon updates to reflect the current mode. Press `Space` to commit the selection with the pending action.

| Shortcut | Action | Description |
|---|---|---|
| `Shift+L` | Left Click | Simulates a left mouse click |
| `Shift+R` | Right Click | Simulates a right mouse click (e.g., context menu) |
| `Shift+D` | Double Click | Simulates a double left click |
| `Shift+M` | Mouse Move | Moves the cursor to the element center |
| `Shift+C` | Ctrl+Click | Holds Ctrl while left clicking (e.g., open in new tab, multi-select) |
| `Shift+S` | Shift+Click | Holds Shift while left clicking (e.g., extend selection) |

Pressing the same shortcut again resets back to the default action. These shortcut keys are configurable in the preferences.

## Tray Icon

The system tray icon reflects the current state:

| Icon | Meaning |
|---|---|
| **H** | Default / idle |
| **L** | Left Click mode |
| **R** | Right Click mode |
| **D** | Double Click mode |
| **M** | Mouse Move mode |
| **C** | Ctrl+Click mode |
| **S** | Shift+Click mode |

Right-click the tray icon for Preferences, Logging, and Exit.

## Named Pipe Interface

External applications can control Windows-Hinting via the `WindowsHinting_Pipe` named pipe.

| Command | Description |
|---|---|
| `TOGGLE` | Toggle hints for the foreground window |
| `TOGGLETASKBAR` | Toggle hints for the taskbar |
| `SELECT <label>` | Activate hint with default action |
| `SELECT <label> <action>` | Activate hint with a specific action (`LEFT`, `RIGHT`, `DOUBLE`, `MOVE`, `CTRL`, `SHIFT`) |
| `DEACTIVATE` | Dismiss hints |

**C# client example:**
```csharp
using var client = new HintOverlayClient();
client.Toggle();
client.SelectHint("A");               // Default activation
client.SelectHint("B", "RIGHT");      // Right click
client.SelectHint("C", "CTRL");       // Ctrl+click
```

See [Named Pipe docs](Windows-Hinting/docs/README_NAMED_PIPE_INTERFACE.md) for more details including PowerShell, Python, and C++ examples.
