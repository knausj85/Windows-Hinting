using System;
using System.Collections.Generic;
using WindowsHinting.Services.Native;

namespace WindowsHinting.Services
{
    internal sealed class WindowManager : IWindowManager
    {
        IntPtr IWindowManager.GetForegroundWindow() => NativeMethods.GetForegroundWindow();

        public IntPtr GetTaskbarWindow() => NativeMethods.FindWindow("Shell_TrayWnd", null);

        public IReadOnlyList<IntPtr> GetTaskbarWindows()
        {
            var taskbars = new List<IntPtr>();

            // Primary taskbar
            var primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero)
            {
                taskbars.Add(primary);
            }

            // Secondary taskbars (one per additional monitor)
            IntPtr current = IntPtr.Zero;
            while (true)
            {
                current = NativeMethods.FindWindowEx(IntPtr.Zero, current, "Shell_SecondaryTrayWnd", null);
                if (current == IntPtr.Zero)
                    break;
                taskbars.Add(current);
            }

            return taskbars;
        }

        public bool IsWindowValid(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd);
    }
}
