using System;
using System.Runtime.InteropServices;

namespace HintOverlay.Services.Native
{
    internal static class NativeMethods
    {
        // user32.dll - Window Management
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        // user32.dll - Keyboard Hook
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        // kernel32.dll
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        // Delegates
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    }

    internal static class WindowsConstants
    {
        // Window Messages
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        // Hook Types
        public const int WH_KEYBOARD_LL = 13;

        // Virtual Key Codes
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;     // Alt key
        public const int VK_SHIFT = 0x10;
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;

        // Key State Masks
        public const int KEY_PRESSED = 0x8000;
    }
}