using System.Collections.Generic;
using HintOverlay.Configuration;

namespace HintOverlay.Models
{
    internal sealed class HintOverlayOptions
    {
        public bool ShowRectangles { get; set; } = false;
        public HotkeyConfiguration Hotkey { get; set; } = new();
        public AnimationOptions Animation { get; set; } = new();
        public List<WindowRule>? WindowRules { get; set; }
    }
    
    internal sealed class HotkeyConfiguration
    {
        public bool Enabled { get; set; } = true;
        public int Modifiers { get; set; } = 0x0003; // MOD_CONTROL | MOD_ALT
        public int VirtualKey { get; set; } = 0x48; // H key
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
}