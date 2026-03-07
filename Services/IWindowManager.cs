using System;

namespace HintOverlay.Services
{
    public interface IWindowManager
    {
        IntPtr GetForegroundWindow();
        bool IsWindowValid(IntPtr hwnd);
    }
}