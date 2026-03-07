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
            // Create a simple 16x16 icon with a yellow 'H' on transparent background
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var font = new Font("Segoe UI", 10, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.Yellow))
                {
                    g.DrawString("H", font, brush, -2, -2);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            _trayIcon?.Dispose();
        }
    }
}
