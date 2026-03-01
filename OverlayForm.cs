
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;

namespace HintOverlay
{
    internal sealed class OverlayForm : Form
    {
        private List<HintItem> _hints = new();
        private bool _enabled;

        private const int HOTKEY_ID = 1;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_ALT = 0x0001;
        private const int VK_H = 0x48;

        private readonly Pen _pen = new(Color.Yellow, 2);
        private readonly Brush _labelBg = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
        private readonly Brush _labelFg = new SolidBrush(Color.Yellow);
        private readonly Font _font = new("Segoe UI", 9, FontStyle.Bold);

        public event EventHandler? ToggleRequested;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            Bounds = SystemInformation.VirtualScreen;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);

            BackColor = Color.LimeGreen;
            TransparencyKey = Color.LimeGreen;
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (enabled) 
                Show();
            else 
                Hide();

            if (!enabled) _hints.Clear();
            Invalidate();
        }

        public void SetHints(List<HintItem> hints)
        {
            _hints = hints;
            Invalidate();
        }

        public void RegisterGlobalHotkey()
        {
            RegisterHotKey(Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_H);
        }

        public void UnregisterGlobalHotkey()
        {
            UnregisterHotKey(Handle, HOTKEY_ID);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_enabled) return;

            var g = e.Graphics;

            foreach (var h in _hints)
            {
                g.DrawRectangle(_pen, h.Rect);
                var size = g.MeasureString(h.Label, _font);
                var bg = new RectangleF(h.Rect.Left, h.Rect.Top, size.Width + 6, size.Height + 2);
                g.FillRectangle(_labelBg, bg);
                g.DrawString(h.Label, _font, _labelFg, h.Rect.Left + 3, h.Rect.Top + 1);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TRANSPARENT = 0x20;
                const int WS_EX_TOOLWINDOW = 0x80;
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_LAYERED = 0x80000;

                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY)
            {
                ToggleRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
