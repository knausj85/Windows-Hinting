using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using UIAutomationClient;
using WindowsHinting.Models;

namespace WindowsHinting.Services
{
    public interface IUIAutomationService : IDisposable
    {
        IReadOnlyList<ClickableElement> FindClickableElements(IntPtr windowHandle);
        // PROTOTYPE #77: scan an element's own subtree directly (for menu/popup elements
        // handed to us by a UIA event, which often have no native HWND to resolve from).
        IReadOnlyList<ClickableElement> FindClickableElements(IUIAutomationElement root);
        // PROTOTYPE #77 (change #9): find Menu/MenuBar CONTAINERS under a window and return every
        // clickable element inside them — menu items regardless of their own control type
        // (native menus use Button for the bar, CheckBox/RadioButton for toggles).
        IReadOnlyList<ClickableElement> FindMenuClickableElements(IUIAutomationElement windowRoot);
        Task<IReadOnlyList<ClickableElement>> FindClickableElementsAsync(IntPtr windowHandle);
        Task<IReadOnlyList<ClickableElement>> FindClickableElementsAsync(IntPtr windowHandle, int timeoutMs);
    }

    public class ClickableElement
    {
        public IUIAutomationElement Element { get; set; } = null!;
        public Rectangle Bounds { get; set; }
        public int ControlType { get; set; }   // cached UIA control type id (0 if unknown)
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
