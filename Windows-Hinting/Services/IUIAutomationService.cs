using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32.UI.Accessibility;
using WindowsHinting.Models;

namespace WindowsHinting.Services
{
    internal interface IUIAutomationService : IDisposable
    {
        IReadOnlyList<ClickableElement> FindClickableElements(IntPtr windowHandle);
        Task<IReadOnlyList<ClickableElement>> FindClickableElementsAsync(IntPtr windowHandle);
        Task<IReadOnlyList<ClickableElement>> FindClickableElementsAsync(IntPtr windowHandle, int timeoutMs);
    }

    internal sealed class ClickableElement
    {
        public IUIAutomationElement Element { get; set; } = null!;
        public Rectangle Bounds { get; set; }
    }

    internal interface IKeyboardHookService
    {
        event EventHandler<KeyboardEventArgs>? KeyPressed;
        event EventHandler<KeyboardEventArgs>? KeyReleased;
        void Start();
        void Stop();
        bool IsActive { get; }
    }

    internal sealed class KeyboardEventArgs : EventArgs
    {
        public int VirtualKeyCode { get; init; }
        public KeyModifiers Modifiers { get; init; }
        public bool Handled { get; set; }
    }
}
