using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
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
        private const int WM_HOTKEY = 0x0312;
        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_DPICHANGED = 0x02E0;
        private const int WM_DISPLAYCHANGE = 0x007E;

        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        private readonly ILogger _logger;
        private int _hotkeyModifiers;
        private int _hotkeyVirtualKey;
        private int _taskbarHotkeyModifiers;
        private int _taskbarHotkeyVirtualKey;
        private bool _disposed;

        public event EventHandler? ToggleRequested;
        public event EventHandler? TaskbarToggleRequested;
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

            if (!RegisterHotKey(Handle, HOTKEY_ID, modifiers, virtualKey))
            {
                throw new InvalidOperationException($"Failed to register global hotkey: {modifiers}+{virtualKey}");
            }

            _logger.Debug($"Registered global hotkey: {modifiers}+{virtualKey}");
        }

        public void UnregisterGlobalHotkey()
        {
            if (_hotkeyVirtualKey != 0)
            {
                UnregisterHotKey(Handle, HOTKEY_ID);
                _logger.Debug("Unregistered global hotkey");
            }
        }

        public void RegisterTaskbarHotkey(int modifiers, int virtualKey)
        {
            UnregisterTaskbarHotkey();
            _taskbarHotkeyModifiers = modifiers;
            _taskbarHotkeyVirtualKey = virtualKey;

            if (!RegisterHotKey(Handle, TASKBAR_HOTKEY_ID, modifiers, virtualKey))
            {
                throw new InvalidOperationException($"Failed to register taskbar hotkey: {modifiers}+{virtualKey}");
            }

            _logger.Debug($"Registered taskbar hotkey: {modifiers}+{virtualKey}");
        }

        public void UnregisterTaskbarHotkey()
        {
            if (_taskbarHotkeyVirtualKey != 0)
            {
                UnregisterHotKey(Handle, TASKBAR_HOTKEY_ID);
                _logger.Debug("Unregistered taskbar hotkey");
            }
        }

        public void ReRegisterHotkeys()
        {
            try
            {
                if (_hotkeyVirtualKey != 0)
                {
                    RegisterHotKey(Handle, HOTKEY_ID, _hotkeyModifiers, _hotkeyVirtualKey);
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
                    RegisterHotKey(Handle, TASKBAR_HOTKEY_ID, _taskbarHotkeyModifiers, _taskbarHotkeyVirtualKey);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to re-register taskbar hotkey: {ex.Message}");
            }
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_HOTKEY:
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
                    return;

                case WM_DISPLAYCHANGE:
                case WM_SETTINGCHANGE:
                case WM_DPICHANGED:
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
