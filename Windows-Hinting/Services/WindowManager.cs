using System;
using System.Collections.Generic;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace WindowsHinting.Services
{
    internal sealed class WindowManager : IWindowManager
    {
        IntPtr IWindowManager.GetForegroundWindow() => PInvoke.GetForegroundWindow();

        public IntPtr GetTaskbarWindow() => PInvoke.FindWindow("Shell_TrayWnd", null);

        public IReadOnlyList<IntPtr> GetTaskbarWindows()
        {
            var taskbars = new List<IntPtr>();

            // Primary taskbar
            var primary = PInvoke.FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero)
            {
                taskbars.Add(primary);
            }

            // Secondary taskbars (one per additional monitor)
            HWND current = default;
            while (true)
            {
                current = PInvoke.FindWindowEx(default, current, "Shell_SecondaryTrayWnd", null);
                if (current == default)
                    break;
                taskbars.Add(current);
            }

            return taskbars;
        }

        public bool IsWindowValid(IntPtr hwnd) => hwnd != IntPtr.Zero && PInvoke.IsWindow((HWND)hwnd);
    }
}
