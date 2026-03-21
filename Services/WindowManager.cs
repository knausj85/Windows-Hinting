using System;
using HintOverlay.Services.Native;

namespace HintOverlay.Services
{
    internal sealed class WindowManager : IWindowManager
    {
        IntPtr IWindowManager.GetForegroundWindow() => NativeMethods.GetForegroundWindow();

        public IntPtr GetTaskbarWindow() => NativeMethods.FindWindow("Shell_TrayWnd", null);

        public bool IsWindowValid(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd);
    }
}