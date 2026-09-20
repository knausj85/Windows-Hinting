using System;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using WindowsHinting.Logging;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Message-only window for global hotkey registration and display-change notifications.
    /// Decouples hotkey lifetime from per-screen overlay forms.
    /// </summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int HOTKEY_ID = 1;
        private const int TASKBAR_HOTKEY_ID = 2;
        private const int MENUBAR_HOTKEY_ID = 3; // PROTOTYPE #78

        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        private readonly ILogger _logger;
        private int _hotkeyModifiers;
        private int _hotkeyVirtualKey;
        private int _taskbarHotkeyModifiers;
        private int _taskbarHotkeyVirtualKey;
        private int _menuBarHotkeyModifiers;   // PROTOTYPE #78
        private int _menuBarHotkeyVirtualKey;   // PROTOTYPE #78
        private bool _disposed;

        public event EventHandler? ToggleRequested;
        public event EventHandler? TaskbarToggleRequested;
        public event EventHandler? MenuBarToggleRequested; // PROTOTYPE #78
        public event EventHandler? DisplaySettingsChanged;

        public HotkeyWindow(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var cp = new CreateParams
            {
                Caption = "WindowsHinting_HotkeyWindow",
                Parent = HWND_MESSAGE  // Message-only window
            };

            CreateHandle(cp);
            _logger.Debug($"HotkeyWindow created with handle {Handle}");
        }

        public void RegisterGlobalHotkey(int modifiers, int virtualKey)
        {
            UnregisterGlobalHotkey();
            _hotkeyModifiers = modifiers;
            _hotkeyVirtualKey = virtualKey;

            if (!PInvoke.RegisterHotKey((HWND)Handle, HOTKEY_ID, (HOT_KEY_MODIFIERS)modifiers, (uint)virtualKey))
            {
                throw new InvalidOperationException($"Failed to register global hotkey: {modifiers}+{virtualKey}");
            }

            _logger.Debug($"Registered global hotkey: {modifiers}+{virtualKey}");
        }

        public void UnregisterGlobalHotkey()
        {
            if (_hotkeyVirtualKey != 0)
            {
                PInvoke.UnregisterHotKey((HWND)Handle, HOTKEY_ID);
                _logger.Debug("Unregistered global hotkey");
            }
        }

        public void RegisterTaskbarHotkey(int modifiers, int virtualKey)
        {
            UnregisterTaskbarHotkey();
            _taskbarHotkeyModifiers = modifiers;
            _taskbarHotkeyVirtualKey = virtualKey;

            if (!PInvoke.RegisterHotKey((HWND)Handle, TASKBAR_HOTKEY_ID, (HOT_KEY_MODIFIERS)modifiers, (uint)virtualKey))
            {
                throw new InvalidOperationException($"Failed to register taskbar hotkey: {modifiers}+{virtualKey}");
            }

            _logger.Debug($"Registered taskbar hotkey: {modifiers}+{virtualKey}");
        }

        public void UnregisterTaskbarHotkey()
        {
            if (_taskbarHotkeyVirtualKey != 0)
            {
                PInvoke.UnregisterHotKey((HWND)Handle, TASKBAR_HOTKEY_ID);
                _logger.Debug("Unregistered taskbar hotkey");
            }
        }

        // PROTOTYPE #78: dedicated menubar auto-hint hotkey.
        public void RegisterMenuBarHotkey(int modifiers, int virtualKey)
        {
            UnregisterMenuBarHotkey();
            _menuBarHotkeyModifiers = modifiers;
            _menuBarHotkeyVirtualKey = virtualKey;

            if (!PInvoke.RegisterHotKey((HWND)Handle, MENUBAR_HOTKEY_ID, (HOT_KEY_MODIFIERS)modifiers, (uint)virtualKey))
            {
                throw new InvalidOperationException($"Failed to register menubar hotkey: {modifiers}+{virtualKey}");
            }

            _logger.Debug($"Registered menubar hotkey: {modifiers}+{virtualKey}");
        }

        public void UnregisterMenuBarHotkey()
        {
            if (_menuBarHotkeyVirtualKey != 0)
            {
                PInvoke.UnregisterHotKey((HWND)Handle, MENUBAR_HOTKEY_ID);
                _logger.Debug("Unregistered menubar hotkey");
            }
        }

        public void ReRegisterHotkeys()
        {
            try
            {
                if (_hotkeyVirtualKey != 0)
                {
                    PInvoke.RegisterHotKey((HWND)Handle, HOTKEY_ID, (HOT_KEY_MODIFIERS)_hotkeyModifiers, (uint)_hotkeyVirtualKey);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to re-register global hotkey: {ex.Message}");
            }

            try
            {
                if (_taskbarHotkeyVirtualKey != 0)
                {
                    PInvoke.RegisterHotKey((HWND)Handle, TASKBAR_HOTKEY_ID, (HOT_KEY_MODIFIERS)_taskbarHotkeyModifiers, (uint)_taskbarHotkeyVirtualKey);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to re-register taskbar hotkey: {ex.Message}");
            }

            try
            {
                if (_menuBarHotkeyVirtualKey != 0)
                {
                    PInvoke.RegisterHotKey((HWND)Handle, MENUBAR_HOTKEY_ID, (HOT_KEY_MODIFIERS)_menuBarHotkeyModifiers, (uint)_menuBarHotkeyVirtualKey);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to re-register menubar hotkey: {ex.Message}");
            }
        }

        protected override void WndProc(ref Message m)
        {
            switch ((uint)m.Msg)
            {
                case PInvoke.WM_HOTKEY:
                    int hotkeyId = m.WParam.ToInt32();
                    if (hotkeyId == HOTKEY_ID)
                    {
                        _logger.Debug("Global hotkey triggered");
                        ToggleRequested?.Invoke(this, EventArgs.Empty);
                    }
                    else if (hotkeyId == TASKBAR_HOTKEY_ID)
                    {
                        _logger.Debug("Taskbar hotkey triggered");
                        TaskbarToggleRequested?.Invoke(this, EventArgs.Empty);
                    }
                    else if (hotkeyId == MENUBAR_HOTKEY_ID) // PROTOTYPE #78
                    {
                        _logger.Debug("Menubar hotkey triggered");
                        MenuBarToggleRequested?.Invoke(this, EventArgs.Empty);
                    }
                    return;

                case PInvoke.WM_DISPLAYCHANGE:
                case PInvoke.WM_SETTINGCHANGE:
                case PInvoke.WM_DPICHANGED:
                    _logger.Info($"Display settings changed (msg=0x{m.Msg:X})");
                    DisplaySettingsChanged?.Invoke(this, EventArgs.Empty);
                    break;
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                UnregisterGlobalHotkey();
                UnregisterTaskbarHotkey();
                UnregisterMenuBarHotkey(); // PROTOTYPE #78
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error unregistering hotkeys during dispose: {ex.Message}");
            }

            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }
}
