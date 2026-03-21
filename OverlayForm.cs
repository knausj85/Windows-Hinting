using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HintOverlay
{

    internal sealed class OverlayForm : Form
    {
        private List<HintItem> _hints = new();
        private bool _enabled;
        private string _filterPrefix = "";

        private const int HOTKEY_ID = 1;
        private const int TASKBAR_HOTKEY_ID = 2;

        private readonly Font _font = new("Segoe UI", 9, FontStyle.Bold);

        public event EventHandler? ToggleRequested;
        public event EventHandler? TaskbarToggleRequested;

        public bool ShowRectangles { get; set; } = false;

        private int _hotkeyModifiers;
        private int _hotkeyVirtualKey;

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
            Debug.WriteLine($"SetEnabled {enabled}");
            _enabled = enabled;

            if (!enabled)
            {
                _filterPrefix = "";
                _hints.Clear();
            }

            Invalidate();
        }

        public void SetHints(List<HintItem> hints)
        {
            Debug.WriteLine($"SetHints {hints.Count}");
            _hints = hints;

            Invalidate();
        }

        public void SetFilterPrefix(string prefix)
        {
            Debug.WriteLine($"SetFilterPrefix '{prefix}'");
            if (string.IsNullOrEmpty(prefix))
            {
                _filterPrefix = string.Empty;
            }
            else
            {
                _filterPrefix = prefix;
            }

            Invalidate(); // redraw text highlight immediately
        }

        public void RegisterGlobalHotkey(int modifiers, int virtualKey)
        {
            UnregisterGlobalHotkey();
            _hotkeyModifiers = modifiers;
            _hotkeyVirtualKey = virtualKey;
            RegisterHotKey(Handle, HOTKEY_ID, modifiers, virtualKey);
        }

        public void UnregisterGlobalHotkey()
        {
            UnregisterHotKey(Handle, HOTKEY_ID);
        }

        public void RegisterTaskbarHotkey(int modifiers, int virtualKey)
        {
            UnregisterTaskbarHotkey();
            RegisterHotKey(Handle, TASKBAR_HOTKEY_ID, modifiers, virtualKey);
        }

        public void UnregisterTaskbarHotkey()
        {
            UnregisterHotKey(Handle, TASKBAR_HOTKEY_ID);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_enabled) return;

            var g = e.Graphics;

            int matches = 0;
            foreach (var h in _hints)
            {
                if (h.TargetOpacity == 0f)
                {
                    continue;
                }
                matches++;

                int alpha = (int)(255 * Math.Clamp(h.CurrentOpacity, 0f, 1f));

                using var labelBg = new SolidBrush(Color.FromArgb((int)(170 * Math.Clamp(h.CurrentOpacity, 0f, 1f)), 0, 0, 0));
                using var labelFg = new SolidBrush(Color.FromArgb(alpha, 255, 255, 0));
                using var labelHi = new SolidBrush(Color.FromArgb(alpha, 0, 255, 255)); // highlight

                // rectangle outline (optional based on preference)
                if (ShowRectangles)
                {
                    using var pen = new Pen(Color.FromArgb(alpha, 255, 255, 0), 2);
                    g.DrawRectangle(pen, h.Rect);
                }

                // label background size based on full label, centered in rect
                var size = g.MeasureString(h.Label, _font);
                float bgWidth = size.Width + 6;
                float bgHeight = size.Height + 2;
                float bgX = h.Rect.Left + (h.Rect.Width - bgWidth) / 2;
                float bgY = h.Rect.Top + (h.Rect.Height - bgHeight) / 2;
                var bg = new RectangleF(bgX, bgY, bgWidth, bgHeight);
                g.FillRectangle(labelBg, bg);

                // draw label with highlighted matching prefix
                float x = bgX + 3;
                float y = bgY + 1;

                string match = "";
                string suffix = h.Label;

                if (!string.IsNullOrEmpty(_filterPrefix) &&
                    h.Label.StartsWith(_filterPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    int n = Math.Min(_filterPrefix.Length, h.Label.Length);
                    match = h.Label.Substring(0, n);
                    suffix = h.Label.Substring(n);
                }

                if (!string.IsNullOrEmpty(match))
                {
                    g.DrawString(match, _font, labelHi, x, y);
                }

                var matchSize = TextRenderer.MeasureText(
                match,
                _font,
                Size.Empty,
                TextFormatFlags.NoPadding);

                x += matchSize.Width;
                g.DrawString(suffix, _font, labelFg, x, y);
            }
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        public void EnsureTopmost()
        {
            SetWindowPos(
                Handle,
                HWND_TOPMOST,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }
        
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            EnsureTopmost();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();
                if (hotkeyId == HOTKEY_ID)
                {
                    ToggleRequested?.Invoke(this, EventArgs.Empty);
                }
                else if (hotkeyId == TASKBAR_HOTKEY_ID)
                {
                    TaskbarToggleRequested?.Invoke(this, EventArgs.Empty);
                }
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
