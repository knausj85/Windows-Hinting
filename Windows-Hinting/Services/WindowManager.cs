using System;
using System.Collections.Generic;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using WindowsHinting.Logging;

namespace WindowsHinting.Services
{
    internal sealed class WindowManager : IWindowManager
    {
        private readonly ILogger _logger;

        public WindowManager(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

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
                LogTaskbarWindow((HWND)primary, "primary");
            }

            // Secondary taskbars (one per additional monitor)
            HWND current = default;
            while (true)
            {
                current = PInvoke.FindWindowEx(default, current, "Shell_SecondaryTrayWnd", null);
                if (current == default)
                    break;
                taskbars.Add(current);
                LogTaskbarWindow(current, "secondary");
            }

            _logger.Debug($"GetTaskbarWindows discovered {taskbars.Count} taskbar window(s)");
            return taskbars;
        }

        public bool IsWindowValid(IntPtr hwnd) => hwnd != IntPtr.Zero && PInvoke.IsWindow((HWND)hwnd);

        // Diagnostic helper: logs class name, window rect, and owning monitor for a
        // taskbar HWND so multi-monitor / secondary-taskbar discovery can be verified.
        private void LogTaskbarWindow(HWND hwnd, string label)
        {
            try
            {
                string className = GetWindowClassName(hwnd);
                string rectText = PInvoke.GetWindowRect(hwnd, out RECT rect)
                    ? $"({rect.left},{rect.top})-({rect.right},{rect.bottom}) {rect.right - rect.left}x{rect.bottom - rect.top}"
                    : "(GetWindowRect failed)";
                HMONITOR monitor = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
                _logger.Debug($"Taskbar [{label}] hwnd=0x{(long)(nint)hwnd:X} class='{className}' rect={rectText} monitor=0x{(long)(nint)monitor:X}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to log taskbar window info: {ex.Message}");
            }
        }

        private static unsafe string GetWindowClassName(HWND hwnd)
        {
            const int maxLength = 256;
            Span<char> buffer = stackalloc char[maxLength];
            fixed (char* pBuffer = buffer)
            {
                int len = PInvoke.GetClassName(hwnd, pBuffer, maxLength);
                return len > 0 ? new string(pBuffer, 0, len) : string.Empty;
            }
        }
    }
}
