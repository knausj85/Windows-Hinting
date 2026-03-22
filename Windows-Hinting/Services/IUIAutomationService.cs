using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using UIAutomationClient;
using WindowsHinting.Models;

namespace WindowsHinting.Services
{
    public interface IUIAutomationService : IDisposable
    {
        IReadOnlyList<ClickableElement> FindClickableElements(IntPtr windowHandle);
        Task<IReadOnlyList<ClickableElement>> FindClickableElementsAsync(IntPtr windowHandle);
    }

    public class ClickableElement
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

    internal interface IPreferencesService
    {
        HintOverlayOptions Load();
        void Save(HintOverlayOptions options);
    }

    internal sealed class KeyboardEventArgs : EventArgs
    {
        public int VirtualKeyCode { get; init; }
        public KeyModifiers Modifiers { get; init; }
        public bool Handled { get; set; }
    }
}
