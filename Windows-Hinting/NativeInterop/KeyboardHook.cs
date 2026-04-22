using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using WindowsHinting.Models;
using WindowsHinting.Services;

namespace WindowsHinting.NativeInterop
{
    internal sealed class KeyboardHook : IDisposable
    {
        private const int KEY_PRESSED = 0x8000;

        private UnhookWindowsHookExSafeHandle? _hookHandle;
        private readonly HOOKPROC _hookProc;
        private bool _disposed;

        public event EventHandler<KeyboardEventArgs>? KeyPressed;
        public event EventHandler<KeyboardEventArgs>? KeyReleased;

        public KeyboardHook()
        {
            _hookProc = HookCallback;
        }

        public void Install()
        {
            if (_hookHandle is { IsInvalid: false })
                return;

            _hookHandle = PInvoke.SetWindowsHookEx(
                WINDOWS_HOOK_ID.WH_KEYBOARD_LL,
                _hookProc,
                PInvoke.GetModuleHandle((string?)null),
                0);

            if (_hookHandle is null || _hookHandle.IsInvalid)
            {
                throw new InvalidOperationException("Failed to install keyboard hook");
            }
        }

        public void Uninstall()
        {
            if (_hookHandle is { IsInvalid: false })
            {
                _hookHandle.Dispose();
                _hookHandle = null;
            }
        }

        private LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                uint msg = (uint)wParam.Value;
                bool isKeyDown = msg == PInvoke.WM_KEYDOWN || msg == PInvoke.WM_SYSKEYDOWN;
                bool isKeyUp = msg == PInvoke.WM_KEYUP || msg == PInvoke.WM_SYSKEYUP;

                if (isKeyDown || isKeyUp)
                {
                    var modifiers = GetCurrentModifiers();
                    var args = new KeyboardEventArgs
                    {
                        VirtualKeyCode = vkCode,
                        Modifiers = modifiers,
                        Handled = false
                    };

                    if (isKeyDown)
                        KeyPressed?.Invoke(this, args);
                    else
                        KeyReleased?.Invoke(this, args);

                    if (args.Handled)
                        return (LRESULT)1; // Suppress the key
                }
            }

            return PInvoke.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private static KeyModifiers GetCurrentModifiers()
        {
            var mods = KeyModifiers.None;

            if ((PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_CONTROL) & KEY_PRESSED) != 0)
                mods |= KeyModifiers.Control;

            if ((PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_MENU) & KEY_PRESSED) != 0)
                mods |= KeyModifiers.Alt;

            if ((PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_SHIFT) & KEY_PRESSED) != 0)
                mods |= KeyModifiers.Shift;

            if ((PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_LWIN) & KEY_PRESSED) != 0 ||
                (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_RWIN) & KEY_PRESSED) != 0)
                mods |= KeyModifiers.Win;

            return mods;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Uninstall();
            _disposed = true;
        }
    }
}
