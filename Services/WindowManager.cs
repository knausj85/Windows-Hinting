using System;
using System.Runtime.InteropServices;

namespace HintOverlay.Services
{
    internal sealed class WindowManager : IWindowManager
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        IntPtr IWindowManager.GetForegroundWindow() => GetForegroundWindow();

        public bool IsWindowValid(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd);
    }
}