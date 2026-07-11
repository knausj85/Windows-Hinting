using System.Collections.Generic;
using WindowsHinting.Configuration;
using WindowsHinting.Logging;

namespace WindowsHinting.Models
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
        public HotkeyConfiguration ScrollModeHotkey { get; set; } = new()
        {
            Enabled = true,
            Modifiers = 0x0003, // MOD_CONTROL | MOD_ALT
            VirtualKey = 0x53   // S key
        };
        public ClickActionShortcutOptions ClickActionShortcuts { get; set; } = new();
        public HintPosition HintPosition { get; set; } = HintPosition.UpperLeft;
        public AnimationOptions Animation { get; set; } = new();
        /// <summary>
        /// Area ratio threshold (0–100%) for containment deduplication.
        /// A smaller element covering at least this percentage of a larger
        /// container causes the container to be removed.
        /// </summary>
        public int OverlapThreshold { get; set; } = 25;

        /// <summary>
        /// Time in seconds before displayed hints are automatically hidden.
        /// Set to 0 to disable the auto-hide timeout. Default: 15 seconds.
        /// Note: Hints also auto-hide immediately when you switch to a different
        /// window (foreground window hints only; taskbar hints are unaffected).
        /// </summary>
        public int AutoHideTimeoutSeconds { get; set; } = 15;

        /// <summary>
        /// Maximum time in milliseconds to wait for a UI Automation scan to complete.
        /// If the scan takes longer, it is aborted and the overlay shows no hints.
        /// Set to 0 to disable the timeout. Default: 3000ms.
        /// </summary>
        public int ScanTimeoutMs { get; set; } = 2500;

        /// <summary>
        /// When true, the app checks the GitHub Releases appcast for a newer
        /// version shortly after startup. The manual "Check for updates..."
        /// tray command works regardless of this setting.
        /// </summary>
        public bool AutoCheckForUpdates { get; set; } = true;

        /// <summary>
        /// Optional forced logging settings. When any field is non-null the
        /// corresponding value is applied to the logger at startup, overriding
        /// the default (Info, file logging off). Useful for diagnostics: edit
        /// preferences.json to enable Debug + file logging without recompiling.
        /// </summary>
        public LoggingOptions Logging { get; set; } = new();

        public List<WindowRule>? WindowRules { get; set; }
    }

    internal sealed class LoggingOptions
    {
        /// <summary>
        /// If set, forces the logger's minimum level at startup. When null the
        /// default (<see cref="LogLevel.Info"/>) is used.
        /// </summary>
        public LogLevel? MinimumLevel { get; set; }

        /// <summary>
        /// If set, forces file logging on/off at startup. When null the default
        /// (off) is used.
        /// </summary>
        public bool? FileLoggingEnabled { get; set; }

        /// <summary>
        /// When true (default), NetSparkle diagnostic messages are routed through
        /// the application logger. Set to false in preferences.json to completely
        /// silence NetSparkle output.
        /// </summary>
        public bool NetSparkleEnabled { get; set; } = true;
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
        public int CtrlClickKey { get; set; } = 0x43;   // C key
        public int ShiftClickKey { get; set; } = 0x53;  // S key
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
