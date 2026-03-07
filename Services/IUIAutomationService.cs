using HintOverlay.Models;

namespace HintOverlay.Services
{
    internal interface IUIAutomationService
    {
        IReadOnlyList<ClickableElement> FindClickableElements(IntPtr windowHandle);
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