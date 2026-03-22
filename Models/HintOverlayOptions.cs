using System.Collections.Generic;
using HintOverlay.Configuration;

namespace HintOverlay.Models
{
    internal sealed class HintOverlayOptions
    {
        public bool ShowRectangles { get; set; } = false;
        public HotkeyConfiguration Hotkey { get; set; } = new();
        public HotkeyConfiguration TaskbarHotkey { get; set; } = new()
        {
            Enabled = true,
            Modifiers = 0x0003, // MOD_CONTROL | MOD_ALT
            VirtualKey = 0x54   // T key
        };
        public ClickActionShortcutOptions ClickActionShortcuts { get; set; } = new();
        public HintPosition HintPosition { get; set; } = HintPosition.UpperLeft;
        public AnimationOptions Animation { get; set; } = new();
        public List<WindowRule>? WindowRules { get; set; }
    }

    internal sealed class HotkeyConfiguration
    {
        public bool Enabled { get; set; } = true;
        public int Modifiers { get; set; } = 0x0003; // MOD_CONTROL | MOD_ALT
        public int VirtualKey { get; set; } = 0x48; // H key
    }

    internal sealed class ClickActionShortcutOptions
    {
        public bool Enabled { get; set; } = true;
        public int LeftClickKey { get; set; } = 0x4C;   // L key
        public int RightClickKey { get; set; } = 0x52;  // R key
        public int DoubleClickKey { get; set; } = 0x44;  // D key
        public int MouseMoveKey { get; set; } = 0x4D;   // M key
    }
    
    internal sealed class AnimationOptions
    {
        public int FadeDurationMs { get; set; } = 150;
        public float InactiveOpacity { get; set; } = 0.3f;
    }

    [Flags]
    internal enum KeyModifiers
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Win = 8
    }

    internal enum HintPosition
    {
        UpperLeft,
        UpperCenter,
        UpperRight,
        Left,
        Center,
        Right,
        LowerLeft,
        LowerCenter,
        LowerRight
    }
}