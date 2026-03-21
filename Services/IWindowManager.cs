using System;

namespace HintOverlay.Services
{
    public interface IWindowManager
    {
        IntPtr GetForegroundWindow();
        IntPtr GetTaskbarWindow();
        bool IsWindowValid(IntPtr hwnd);
    }
}