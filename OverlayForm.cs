using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace HintOverlay
{

    internal sealed class OverlayForm : Form
    {
        private List<HintItem> _hints = new();
        private bool _enabled;
        private string _filterPrefix = "";

        private const int HOTKEY_ID = 1;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_ALT = 0x0001;
        private const int VK_H = 0x48;

        private readonly Font _font = new("Segoe UI", 9, FontStyle.Bold);

        // animation
        private readonly System.Windows.Forms.Timer _animTimer;
        private const float FadeLerp = 0.22f; // easing factor per frame (16ms)

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

            _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _animTimer.Tick += (_, __) => AnimateStep();
        }

        public void SetEnabled(bool enabled)
        {
            Debug.WriteLine($"SetEnabled {enabled}");
            _enabled = enabled;

            if (!enabled)
            {
                _filterPrefix = "";
                _hints.Clear();
                _animTimer.Stop();
            }

            Invalidate();
        }

        public void SetHints(List<HintItem> hints)
        {
            Debug.WriteLine($"SetHints {hints.Count}");
            _hints = hints;

            // ensure animation progresses toward current targets
            StartAnimationIfNeeded();
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

        public void StartAnimationIfNeeded()
        {
            if (!_enabled || _hints.Count == 0) return;
            _animTimer.Start();
        }

        private void AnimateStep()
        {
            if (!_enabled || _hints.Count == 0)
            {
                _animTimer.Stop();
                return;
            }

            bool anyAnimating = false;
            foreach (var h in _hints)
            {
                var diff = h.TargetOpacity - h.CurrentOpacity;
                if (Math.Abs(diff) > 0.01f)
                {
                    h.CurrentOpacity += diff * FadeLerp;
                    anyAnimating = true;
                }
                else
                {
                    h.CurrentOpacity = h.TargetOpacity;
                }
            }

            Invalidate();

            if (!anyAnimating)
                _animTimer.Stop();
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
                int alpha = (int)(255 * Math.Clamp(h.CurrentOpacity, 0f, 1f));

                using var pen = new Pen(Color.FromArgb(alpha, 255, 255, 0), 2);
                using var labelBg = new SolidBrush(Color.FromArgb((int)(170 * Math.Clamp(h.CurrentOpacity, 0f, 1f)), 0, 0, 0));
                using var labelFg = new SolidBrush(Color.FromArgb(alpha, 255, 255, 0));
                using var labelHi = new SolidBrush(Color.FromArgb(alpha, 0, 255, 255)); // highlight

                // rectangle outline
                g.DrawRectangle(pen, h.Rect);

                // label background size based on full label
                var size = g.MeasureString(h.Label, _font);
                var bg = new RectangleF(h.Rect.Left, h.Rect.Top, size.Width + 6, size.Height + 2);
                g.FillRectangle(labelBg, bg);

                // draw label with highlighted matching prefix
                float x = h.Rect.Left + 3;
                float y = h.Rect.Top + 1;

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
                    x += g.MeasureString(match, _font).Width;
                }

                g.DrawString(suffix, _font, labelFg, x, y);
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
