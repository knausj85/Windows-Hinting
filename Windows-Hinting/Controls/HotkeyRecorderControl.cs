using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowsHinting.Controls
{
    /// <summary>
    /// Determines whether the control records a full modifier+key combination
    /// or a single letter key.
    /// </summary>
    internal enum RecorderMode
    {
        /// <summary>Records any modifier + key combination (e.g. Ctrl+Alt+H).</summary>
        HotkeyCombination,

        /// <summary>Records a single A-Z key (displayed as "Shift + X").</summary>
        SingleKey
    }

    /// <summary>
    /// A text-box-style control that records a keyboard shortcut when focused.
    /// Click the control, then press the desired key (or key combination).
    /// Set <see cref="Mode"/> to control which input style is used.
    /// </summary>
    internal sealed class HotkeyRecorderControl : TextBox
    {
        private const int MOD_ALT = 0x0001;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;

        private const string HotkeyPrompt = "Click here, then press a shortcut…";
        private const string SingleKeyPrompt = "Click, then press a key…";

        private RecorderMode _mode = RecorderMode.HotkeyCombination;

        /// <summary>
        /// Controls whether the recorder accepts full modifier+key combinations
        /// or a single A-Z key press. Defaults to <see cref="RecorderMode.HotkeyCombination"/>.
        /// </summary>
        public RecorderMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                Text = Prompt;
            }
        }

        private string Prompt => _mode == RecorderMode.SingleKey ? SingleKeyPrompt : HotkeyPrompt;

        /// <summary>Win32 hotkey modifier flags (MOD_ALT | MOD_CONTROL | MOD_SHIFT).</summary>
        public int HotkeyModifiers { get; private set; }

        /// <summary>Win32 virtual-key code of the primary key.</summary>
        public int HotkeyVirtualKey { get; private set; }

        /// <summary>
        /// Convenience alias for <see cref="HotkeyVirtualKey"/>, useful in
        /// <see cref="RecorderMode.SingleKey"/> mode.
        /// </summary>
        public int VirtualKey
        {
            get => HotkeyVirtualKey;
            private set => HotkeyVirtualKey = value;
        }

        /// <summary>Raised when the control gains focus and begins listening for a key combination.</summary>
        public event EventHandler? RecordingStarted;

        /// <summary>Raised when the control loses focus and stops listening.</summary>
        public event EventHandler? RecordingStopped;

        public HotkeyRecorderControl()
        {
            ReadOnly = true;
            TextAlign = HorizontalAlignment.Center;
            BackColor = SystemColors.Window;
            Font = new Font("Segoe UI", 10F);
            Text = Prompt;
        }

        /// <summary>
        /// Programmatically sets the displayed hotkey (e.g. when loading preferences).
        /// </summary>
        public void SetHotkey(int modifiers, int virtualKey)
        {
            HotkeyModifiers = modifiers;
            HotkeyVirtualKey = virtualKey;
            Text = FormatDisplay(modifiers, virtualKey);
        }

        /// <summary>
        /// Convenience overload for <see cref="RecorderMode.SingleKey"/> mode.
        /// Sets modifiers to zero.
        /// </summary>
        public void SetKey(int virtualKey)
        {
            SetHotkey(0, virtualKey);
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            Text = _mode == RecorderMode.SingleKey ? "Press a key…" : "Press a shortcut…";
            BackColor = Color.FromArgb(255, 255, 240);
            RecordingStarted?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            BackColor = SystemColors.Window;

            Text = HotkeyVirtualKey != 0
                ? FormatDisplay(HotkeyModifiers, HotkeyVirtualKey)
                : Prompt;

            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!Focused)
                return base.ProcessCmdKey(ref msg, keyData);

            var key = keyData & Keys.KeyCode;

            // Ignore standalone modifier presses
            if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu
                    or Keys.LWin or Keys.RWin)
                return true;

            if (_mode == RecorderMode.SingleKey)
                return ProcessSingleKey(key);

            return ProcessHotkeyCombination(keyData, key);
        }

        private bool ProcessSingleKey(Keys key)
        {
            int vk = (int)key;

            // Only accept A-Z keys in single-key mode
            if (vk < 0x41 || vk > 0x5A)
            {
                System.Media.SystemSounds.Beep.Play();
                return true;
            }

            HotkeyModifiers = 0;
            HotkeyVirtualKey = vk;
            Text = FormatDisplay(0, vk);
            BackColor = SystemColors.Window;

            // Move focus away without advancing to the next recorder
            Parent?.Focus();
            return true;
        }

        private bool ProcessHotkeyCombination(Keys keyData, Keys key)
        {
            int mods = 0;
            if ((keyData & Keys.Control) != 0) mods |= MOD_CONTROL;
            if ((keyData & Keys.Alt) != 0) mods |= MOD_ALT;
            if ((keyData & Keys.Shift) != 0) mods |= MOD_SHIFT;

            HotkeyModifiers = mods;
            HotkeyVirtualKey = (int)key;
            Text = FormatDisplay(mods, HotkeyVirtualKey);
            BackColor = SystemColors.Window;

            // Move focus away without advancing to the next recorder
            Parent?.Focus();
            return true;
        }

        private string FormatDisplay(int mods, int vk)
        {
            if (_mode == RecorderMode.SingleKey)
                return FormatSingleKey(vk);

            return FormatHotkey(mods, vk);
        }

        private static string FormatSingleKey(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A)
                return $"Shift + {(char)vk}";

            return ((Keys)vk).ToString();
        }

        private static string FormatHotkey(int mods, int vk)
        {
            var parts = new System.Collections.Generic.List<string>(4);

            if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mods & MOD_ALT) != 0) parts.Add("Alt");
            if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");

            parts.Add(KeyName(vk));

            return string.Join(" + ", parts);
        }

        private static string KeyName(int vk)
        {
            var key = (Keys)vk;

            // Letters
            if (vk is >= 0x41 and <= 0x5A)
                return ((char)vk).ToString();

            // Digits
            if (vk is >= 0x30 and <= 0x39)
                return ((char)vk).ToString();

            // Function keys
            if (vk is >= 0x70 and <= 0x87)
                return $"F{vk - 0x6F}";

            // Common named keys
            return key switch
            {
                Keys.Space => "Space",
                Keys.Enter or Keys.Return => "Enter",
                Keys.Escape => "Esc",
                Keys.Tab => "Tab",
                Keys.Back => "Backspace",
                Keys.Delete => "Delete",
                Keys.Insert => "Insert",
                Keys.Home => "Home",
                Keys.End => "End",
                Keys.PageUp => "Page Up",
                Keys.PageDown => "Page Down",
                Keys.Up => "↑",
                Keys.Down => "↓",
                Keys.Left => "←",
                Keys.Right => "→",
                Keys.OemSemicolon => ";",
                Keys.Oemplus => "=",
                Keys.Oemcomma => ",",
                Keys.OemMinus => "-",
                Keys.OemPeriod => ".",
                Keys.OemQuestion => "/",
                Keys.Oemtilde => "`",
                Keys.OemOpenBrackets => "[",
                Keys.OemPipe => "\\",
                Keys.OemCloseBrackets => "]",
                Keys.OemQuotes => "'",
                _ => key.ToString()
            };
        }
    }
}
