using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowsHinting.Models;

namespace WindowsHinting.Forms
{

    internal sealed class OverlayForm : Form
    {
        private List<HintItem> _hints = new();
        private bool _enabled;
        private string _filterPrefix = "";

        private const int HOTKEY_ID = 1;
        private const int TASKBAR_HOTKEY_ID = 2;

        private const string BaseTitle = "Windows Hinting Overlay";
        private const string ActiveTitle = BaseTitle + " [Active]";

        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_DPICHANGED = 0x02E0;

        private Font _font;

        public event EventHandler? ToggleRequested;
        public event EventHandler? TaskbarToggleRequested;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowRectangles { get; set; } = false;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public HintPosition HintPosition { get; set; } = HintPosition.UpperLeft;

        private int _hotkeyModifiers;
        private int _hotkeyVirtualKey;
        private int _taskbarHotkeyModifiers;
        private int _taskbarHotkeyVirtualKey;

        public OverlayForm()
        {
            // Stable window title consumed by external tools (e.g. Talon Voice)
            // to detect the hints overlay. Not visible to end users because the
            // form has no border, is excluded from the taskbar, and uses
            // WS_EX_TOOLWINDOW (no Alt+Tab entry).
            Text = BaseTitle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            Bounds = SystemInformation.VirtualScreen;
            _font = CreateHintFont();

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

        public void SetActiveState(bool active)
        {
            Text = active ? ActiveTitle : BaseTitle;
        }

        public void SetHints(List<HintItem> hints)
        {
            Debug.WriteLine($"SetHints {hints.Count}");
            _hints = hints;

            Invalidate();
        }

        private static Font CreateHintFont()
        {
            var baseFont = SystemFonts.CaptionFont;
            return new Font(baseFont.FontFamily, baseFont.SizeInPoints, FontStyle.Bold, GraphicsUnit.Point);
        }

        private void RefreshHintFont()
        {
            var oldFont = _font;
            _font = CreateHintFont();
            oldFont.Dispose();
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
            if (!RegisterHotKey(Handle, HOTKEY_ID, modifiers, virtualKey))
            {
                throw new InvalidOperationException($"Failed to register global hotkey: {modifiers}+{virtualKey}");
            }
        }

        public void UnregisterGlobalHotkey()
        {
            UnregisterHotKey(Handle, HOTKEY_ID);
        }

        public void RegisterTaskbarHotkey(int modifiers, int virtualKey)
        {
            UnregisterTaskbarHotkey();
            _taskbarHotkeyModifiers = modifiers;
            _taskbarHotkeyVirtualKey = virtualKey;
            if (!RegisterHotKey(Handle, TASKBAR_HOTKEY_ID, modifiers, virtualKey))
            {
                throw new InvalidOperationException($"Failed to register taskbar hotkey: {modifiers}+{virtualKey}");
            }
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

                // label background size based on full label, positioned per HintPosition
                var size = g.MeasureString(h.Label, _font);
                float bgWidth = size.Width + 6;
                float bgHeight = size.Height + 2;

                float bgX, bgY;
                switch (HintPosition)
                {
                    case HintPosition.UpperLeft:
                        bgX = h.Rect.Left;
                        bgY = h.Rect.Top;
                        break;
                    case HintPosition.UpperCenter:
                        bgX = h.Rect.Left + (h.Rect.Width - bgWidth) / 2;
                        bgY = h.Rect.Top;
                        break;
                    case HintPosition.UpperRight:
                        bgX = h.Rect.Right - bgWidth;
                        bgY = h.Rect.Top;
                        break;
                    case HintPosition.Left:
                        bgX = h.Rect.Left;
                        bgY = h.Rect.Top + (h.Rect.Height - bgHeight) / 2;
                        break;
                    case HintPosition.Center:
                        bgX = h.Rect.Left + (h.Rect.Width - bgWidth) / 2;
                        bgY = h.Rect.Top + (h.Rect.Height - bgHeight) / 2;
                        break;
                    case HintPosition.Right:
                        bgX = h.Rect.Right - bgWidth;
                        bgY = h.Rect.Top + (h.Rect.Height - bgHeight) / 2;
                        break;
                    case HintPosition.LowerLeft:
                        bgX = h.Rect.Left;
                        bgY = h.Rect.Bottom - bgHeight;
                        break;
                    case HintPosition.LowerCenter:
                        bgX = h.Rect.Left + (h.Rect.Width - bgWidth) / 2;
                        bgY = h.Rect.Bottom - bgHeight;
                        break;
                    case HintPosition.LowerRight:
                        bgX = h.Rect.Right - bgWidth;
                        bgY = h.Rect.Bottom - bgHeight;
                        break;
                    default:
                        bgX = h.Rect.Left;
                        bgY = h.Rect.Top;
                        break;
                }
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            RefreshHintFont();
            ReRegisterHotkeys();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try
            {
                UnregisterGlobalHotkey();
                UnregisterTaskbarHotkey();
            }
            catch
            {
                // Best effort only
            }

            base.OnHandleDestroyed(e);
        }

        public void ReRegisterHotkeys()
        {
            TryRegisterGlobalHotkey();
            TryRegisterTaskbarHotkey();
        }

        private void TryRegisterGlobalHotkey()
        {
            try
            {
                if (_hotkeyVirtualKey != 0)
                {
                    RegisterHotKey(Handle, HOTKEY_ID, _hotkeyModifiers, _hotkeyVirtualKey);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to re-register global hotkey: {ex.Message}");
            }
        }

        private void TryRegisterTaskbarHotkey()
        {
            try
            {
                if (_taskbarHotkeyVirtualKey != 0)
                {
                    RegisterHotKey(Handle, TASKBAR_HOTKEY_ID, _taskbarHotkeyModifiers, _taskbarHotkeyVirtualKey);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to re-register taskbar hotkey: {ex.Message}");
            }
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

            if (m.Msg == WM_SETTINGCHANGE || m.Msg == WM_DPICHANGED)
            {
                RefreshHintFont();
            }

            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _font.Dispose();
            }

            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
