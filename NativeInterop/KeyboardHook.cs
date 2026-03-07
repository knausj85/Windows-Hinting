using System;
using System.Runtime.InteropServices;
using HintOverlay.Models;
using HintOverlay.Services;

namespace HintOverlay.NativeInterop
{
    internal sealed class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        
        private IntPtr _hookHandle;
        private readonly LowLevelKeyboardProc _hookProc;
        private bool _disposed;
        
        public event EventHandler<KeyboardEventArgs>? KeyPressed;
        public event EventHandler<KeyboardEventArgs>? KeyReleased;
        
        public KeyboardHook()
        {
            _hookProc = HookCallback;
        }
        
        public void Install()
        {
            if (_hookHandle != IntPtr.Zero)
                return;
                
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
            
            if (_hookHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to install keyboard hook");
            }
        }
        
        public void Uninstall()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }
        
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                
                bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
                bool isKeyUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;
                
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
                        return (IntPtr)1; // Suppress the key
                }
            }
            
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
        
        private KeyModifiers GetCurrentModifiers()
        {
            var mods = KeyModifiers.None;
            
            if ((GetAsyncKeyState(0x11) & 0x8000) != 0) // VK_CONTROL
                mods |= KeyModifiers.Control;
            if ((GetAsyncKeyState(0x12) & 0x8000) != 0) // VK_MENU (Alt)
                mods |= KeyModifiers.Alt;
            if ((GetAsyncKeyState(0x10) & 0x8000) != 0) // VK_SHIFT
                mods |= KeyModifiers.Shift;
            
            return mods;
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                Uninstall();
                _disposed = true;
            }
        }
        
        // P/Invoke declarations
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
        
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
