using System;
using System.Collections.Generic;

namespace WindowsHinting.Services
{
    public interface IWindowManager
    {
        IntPtr GetForegroundWindow();
        IntPtr GetTaskbarWindow();
        IReadOnlyList<IntPtr> GetTaskbarWindows();
        bool IsWindowValid(IntPtr hwnd);
    }
}
