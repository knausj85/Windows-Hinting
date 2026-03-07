using System;
using System.Collections.Generic;
using System.Drawing;
using UIAutomationClient;
using HintOverlay.Models;

namespace HintOverlay.Services
{
    internal interface IUIAutomationService
    {
        IReadOnlyList<ClickableElement> FindClickableElements(IntPtr windowHandle);
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