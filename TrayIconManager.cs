using System;
using System.Drawing;
using System.Windows.Forms;

namespace HintOverlay
{
    internal sealed class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _trayIcon;
        
        public event EventHandler? ToggleRequested;
        public event EventHandler? PreferencesRequested;
        public event EventHandler? ExitRequested;

        public TrayIconManager()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "HintOverlay",
                Visible = true,
                Icon = CreateTrayIcon()
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Preferences...", null, (s, e) => PreferencesRequested?.Invoke(this, EventArgs.Empty));
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty));

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => ToggleRequested?.Invoke(this, EventArgs.Empty);
        }

        private Icon CreateTrayIcon()
        {
            const int size = 32;
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                // Rounded background
                var bgRect = new Rectangle(1, 1, size - 2, size - 2);
                int radius = 6;
                using var path = CreateRoundedRect(bgRect, radius);
                using (var bgBrush = new SolidBrush(Color.FromArgb(230, 30, 30, 30)))
                {
                    g.FillPath(bgBrush, path);
                }
                using (var borderPen = new Pen(Color.FromArgb(180, 255, 220, 50), 1.5f))
                {
                    g.DrawPath(borderPen, path);
                }

                // Centered "H"
                using var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Color.FromArgb(255, 255, 220, 50));
                var textSize = g.MeasureString("H", font);
                float x = (size - textSize.Width) / 2f;
                float y = (size - textSize.Height) / 2f;
                g.DrawString("H", font, brush, x, y);
            }

            IntPtr hIcon = bmp.GetHicon();
            return Icon.FromHandle(hIcon);
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void Dispose()
        {
            _trayIcon?.Dispose();
        }
    }
}
