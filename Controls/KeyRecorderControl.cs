using System;
using System.Drawing;
using System.Windows.Forms;

namespace HintOverlay.Controls
{
    /// <summary>
    /// A text-box-style control that records a single key press.
    /// Click the control, then press the desired key.
    /// </summary>
    internal sealed class KeyRecorderControl : TextBox
    {
        private const string Prompt = "Click, then press a key…";

        /// <summary>Win32 virtual-key code of the recorded key.</summary>
        public int VirtualKey { get; private set; }

        public KeyRecorderControl()
        {
            ReadOnly = true;
            TextAlign = HorizontalAlignment.Center;
            BackColor = SystemColors.Window;
            Font = new Font("Segoe UI", 10F);
            Text = Prompt;
        }

        /// <summary>
        /// Programmatically sets the displayed key (e.g. when loading preferences).
        /// </summary>
        public void SetKey(int virtualKey)
        {
            VirtualKey = virtualKey;
            Text = virtualKey != 0 ? FormatKey(virtualKey) : Prompt;
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            Text = "Press a key…";
            BackColor = Color.FromArgb(255, 255, 240);
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            BackColor = SystemColors.Window;
            Text = VirtualKey != 0 ? FormatKey(VirtualKey) : Prompt;
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

            // Only accept A-Z keys
            int vk = (int)key;
            if (vk < 0x41 || vk > 0x5A)
            {
                System.Media.SystemSounds.Beep.Play();
                return true;
            }

            VirtualKey = vk;
            Text = FormatKey(vk);
            BackColor = SystemColors.Window;

            Parent?.SelectNextControl(this, true, true, true, true);
            return true;
        }

        private static string FormatKey(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A)
                return $"Shift + {(char)vk}";

            return ((Keys)vk).ToString();
        }
    }
}
