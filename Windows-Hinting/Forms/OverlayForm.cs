using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WindowsHinting.Logging;
using WindowsHinting.Models;

namespace WindowsHinting.Forms
{

    internal sealed class OverlayForm : Form
    {
        private List<HintItem> _hints = new();
        private bool _enabled;
        private string _filterPrefix = "";

        private const string BaseTitle = "Windows Hinting Overlay";
        private const string ActiveTitle = BaseTitle + " [Active]";

        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_DPICHANGED = 0x02E0;
        private readonly ILogger _logger;
        private readonly Screen _screen;
        private Font _font;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowRectangles { get; set; } = false;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public HintPosition HintPosition { get; set; } = HintPosition.UpperLeft;

        public OverlayForm(Screen screen, ILogger logger)
        {
            _screen = screen ?? throw new ArgumentNullException(nameof(screen));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Stable window title consumed by external tools (e.g. Talon Voice)
            // to detect the hints overlay. Not visible to end users because the
            // form has no border, is excluded from the taskbar, and uses
            // WS_EX_TOOLWINDOW (no Alt+Tab entry).
            Text = BaseTitle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;

            // Disable WinForms auto-scaling: we set Bounds in physical pixels
            // to match the target Screen exactly, so we don't want the framework
            // re-scaling our layout after the form's DPI is established.
            AutoScaleMode = AutoScaleMode.None;

            StartPosition = FormStartPosition.Manual;
            Bounds = _screen.Bounds;
            _font = CreateHintFont();

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);

            BackColor = Color.LimeGreen;
            TransparencyKey = Color.LimeGreen;
        }

        public void SetEnabled(bool enabled)
        {
            _logger.Debug($"SetEnabled {enabled}");
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
            // Filter hints to those intersecting this screen's bounds
            var screenBounds = _screen.Bounds;
            var filtered = new List<HintItem>();
            var dropped = new List<HintItem>();

            foreach (var hint in hints)
            {
                if (hint.Rect.IntersectsWith(screenBounds))
                {
                    filtered.Add(hint);
                }
                else
                {
                    dropped.Add(hint);
                }
            }

            // Diagnostic: when hints exist but get dropped here, the intersection
            // test (WinForms Screen.Bounds vs UIA physical-pixel bounds) is the
            // suspect under mixed DPI. Log this screen's geometry + sample drops.
            _logger.Debug($"SetHints screen='{_screen.DeviceName}' primary={_screen.Primary} bounds=({screenBounds.Left},{screenBounds.Top}) {screenBounds.Width}x{screenBounds.Height} dpi={DeviceDpi}: {hints.Count} total, {filtered.Count} kept, {dropped.Count} dropped");
            int sampleCount = Math.Min(dropped.Count, 5);
            for (int i = 0; i < sampleCount; i++)
            {
                var r = dropped[i].Rect;
                _logger.Debug($"  dropped hint '{dropped[i].Label}' rect=({r.Left},{r.Top}) {r.Width}x{r.Height}");
            }

            _hints = filtered;

            Invalidate();
        }

        private Font CreateHintFont()
        {
            // Use a pixel-sized font so it renders crisply at any DPI without
            // relying on WinForms' point-to-pixel conversion (which depends on
            // whichever DPI context the form was created under).
            //
            // Base size: the system caption font in points, converted to pixels
            // at this form's current DPI (DeviceDpi reflects the owning monitor
            // under PerMonitorV2).
            var baseFont = SystemFonts.CaptionFont;
            float dpi = DeviceDpi > 0 ? DeviceDpi : 96f;
            float pixelSize = baseFont.SizeInPoints * dpi / 72f;
            return new Font(baseFont.FontFamily, pixelSize, FontStyle.Bold, GraphicsUnit.Pixel);
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
            _logger.Debug($"SetFilterPrefix '{prefix}'");
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

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_enabled) return;

            var g = e.Graphics;

            // Prefer GDI TextRenderer (ClearType) over GDI+ DrawString for crisp text at high DPI.
            // Set high-quality smoothing for rectangles; TextRenderer handles text independently.
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            const TextFormatFlags textFlags =
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix;

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
                var labelFgColor = Color.FromArgb(alpha, 255, 255, 0);
                var labelHiColor = Color.FromArgb(alpha, 0, 255, 255); // highlight

                // rectangle outline (optional based on preference)
                if (ShowRectangles)
                {
                    // Scale pen width by DPI for visibility on 4K monitors
                    float penWidth = Math.Max(2f, DeviceDpi / 96f * 2f);
                    using var pen = new Pen(Color.FromArgb(alpha, 255, 255, 0), penWidth);
                    // Hint bounds are absolute virtual-desktop pixels; convert to this
                    // form's client coordinates (client 0,0 == _screen.Bounds origin).
                    var outlineRect = h.Rect;
                    outlineRect.Offset(-_screen.Bounds.X, -_screen.Bounds.Y);
                    g.DrawRectangle(pen, outlineRect);
                }

                // label background size based on full label, positioned per HintPosition
                var labelSize = TextRenderer.MeasureText(g, h.Label, _font, Size.Empty, textFlags);

                // Scale padding by DPI so it matches visual proportions at 4K
                int padX = Math.Max(3, (int)Math.Round(DeviceDpi / 96f * 3f));
                int padY = Math.Max(1, (int)Math.Round(DeviceDpi / 96f * 1f));

                int bgWidth = labelSize.Width + padX * 2;
                int bgHeight = labelSize.Height + padY * 2;

                int bgX, bgY;
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

                // Clamp label to the full monitor bounds (not the working area).
                // Taskbar hints live inside the taskbar strip, which is outside the
                // working area; clamping to WorkingArea would force every taskbar
                // label to the same spot just above the taskbar and collapse the
                // selected vertical HintPosition. The overlay is topmost, so labels
                // render over the taskbar at the chosen position while staying on-screen.
                var screenBounds = _screen.Bounds;
                bgX = Math.Max(screenBounds.Left, Math.Min(bgX, screenBounds.Right - bgWidth));
                bgY = Math.Max(screenBounds.Top, Math.Min(bgY, screenBounds.Bottom - bgHeight));

                // bgX/bgY were computed and clamped in absolute virtual-desktop
                // coordinates; convert to this form's client coordinates for drawing
                // (client 0,0 == _screen.Bounds origin). GDI TextRenderer ignores
                // Graphics world transforms, so coordinates are offset explicitly.
                var bg = new Rectangle(bgX - _screen.Bounds.X, bgY - _screen.Bounds.Y, bgWidth, bgHeight);
                g.FillRectangle(labelBg, bg);

                // draw label with highlighted matching prefix
                int x = bg.X + padX;
                int y = bg.Y + padY;

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
                    TextRenderer.DrawText(g, match, _font, new Point(x, y), labelHiColor, textFlags);
                    var matchSize = TextRenderer.MeasureText(g, match, _font, Size.Empty, textFlags);
                    x += matchSize.Width;
                }

                TextRenderer.DrawText(g, suffix, _font, new Point(x, y), labelFgColor, textFlags);
            }
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public void EnsureTopmost()
        {
            PInvoke.SetWindowPos(
                (HWND)Handle,
                (HWND)HWND_TOPMOST,
                0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE
                    | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
                    | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                    | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
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
        }

        protected override void WndProc(ref Message m)
        {
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
    }
}
