using System;

namespace WindowsHinting.Services
{
    public interface IWindowManager
    {
        IntPtr GetForegroundWindow();
        IntPtr GetTaskbarWindow();
        bool IsWindowValid(IntPtr hwnd);
    }
}