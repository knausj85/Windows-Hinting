using System;
using System.Runtime.InteropServices;
using HintOverlay.Models;
using HintOverlay.Services;
using HintOverlay.Services.Native;

namespace HintOverlay.NativeInterop
{
    internal sealed class KeyboardHook : IDisposable
    {
        private IntPtr _hookHandle;
        private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
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
                
            _hookHandle = NativeMethods.SetWindowsHookEx(
                WindowsConstants.WH_KEYBOARD_LL, 
                _hookProc, 
                NativeMethods.GetModuleHandle(null), 
                0);
            
            if (_hookHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to install keyboard hook");
            }
        }
        
        public void Uninstall()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }
        
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                
                bool isKeyDown = wParam == (IntPtr)WindowsConstants.WM_KEYDOWN || 
                                wParam == (IntPtr)WindowsConstants.WM_SYSKEYDOWN;
                bool isKeyUp = wParam == (IntPtr)WindowsConstants.WM_KEYUP || 
                              wParam == (IntPtr)WindowsConstants.WM_SYSKEYUP;
                
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
            
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
        
        private KeyModifiers GetCurrentModifiers()
        {
            var mods = KeyModifiers.None;
            
            if ((NativeMethods.GetAsyncKeyState(WindowsConstants.VK_CONTROL) & WindowsConstants.KEY_PRESSED) != 0)
                mods |= KeyModifiers.Control;
            
            if ((NativeMethods.GetAsyncKeyState(WindowsConstants.VK_MENU) & WindowsConstants.KEY_PRESSED) != 0)
                mods |= KeyModifiers.Alt;
            
            if ((NativeMethods.GetAsyncKeyState(WindowsConstants.VK_SHIFT) & WindowsConstants.KEY_PRESSED) != 0)
                mods |= KeyModifiers.Shift;
            
            if ((NativeMethods.GetAsyncKeyState(WindowsConstants.VK_LWIN) & WindowsConstants.KEY_PRESSED) != 0 ||
                (NativeMethods.GetAsyncKeyState(WindowsConstants.VK_RWIN) & WindowsConstants.KEY_PRESSED) != 0)
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
