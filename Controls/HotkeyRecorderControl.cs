using System;
using System.Drawing;
using System.Windows.Forms;

namespace HintOverlay.Controls
{
    /// <summary>
    /// A text-box-style control that records a keyboard shortcut when focused.
    /// Click the control, then press the desired key combination.
    /// </summary>
    internal sealed class HotkeyRecorderControl : TextBox
    {
        private const int MOD_ALT = 0x0001;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;

        private const string Prompt = "Click here, then press a shortcut…";

        /// <summary>Win32 hotkey modifier flags (MOD_ALT | MOD_CONTROL | MOD_SHIFT).</summary>
        public int HotkeyModifiers { get; private set; }

        /// <summary>Win32 virtual-key code of the primary key.</summary>
        public int HotkeyVirtualKey { get; private set; }

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
            Text = FormatHotkey(modifiers, virtualKey);
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            Text = "Press a shortcut…";
            BackColor = Color.FromArgb(255, 255, 240);
            RecordingStarted?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            BackColor = SystemColors.Window;

            if (HotkeyVirtualKey != 0)
                Text = FormatHotkey(HotkeyModifiers, HotkeyVirtualKey);
            else
                Text = Prompt;

            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Intercept all key combinations, including ones normally consumed
            // by the form (Tab, Enter, Escape, etc.) while the control is focused.
            if (!Focused)
                return base.ProcessCmdKey(ref msg, keyData);

            var key = keyData & Keys.KeyCode;

            // Ignore standalone modifier presses
            if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu
                    or Keys.LWin or Keys.RWin)
                return true;

            int mods = 0;
            if ((keyData & Keys.Control) != 0) mods |= MOD_CONTROL;
            if ((keyData & Keys.Alt) != 0) mods |= MOD_ALT;
            if ((keyData & Keys.Shift) != 0) mods |= MOD_SHIFT;

            HotkeyModifiers = mods;
            HotkeyVirtualKey = (int)key;
            Text = FormatHotkey(mods, HotkeyVirtualKey);
            BackColor = SystemColors.Window;

            // Move focus away so the user sees the result
            Parent?.SelectNextControl(this, true, true, true, true);
            return true;
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
