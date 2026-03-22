using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HintOverlay.Logging;
using HintOverlay.Models;

namespace HintOverlay
{
    internal sealed class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _trayIcon;
        private Icon? _currentIcon;
        private readonly DebugLogger? _debugLogger;
        private LogViewerForm? _logViewer;

        public event EventHandler? ToggleRequested;
        public event EventHandler? PreferencesRequested;
        public event EventHandler? ExitRequested;

        public TrayIconManager(ILogger logger)
        {
            _debugLogger = logger as DebugLogger;

            _currentIcon = CreateTrayIcon("H");
            _trayIcon = new NotifyIcon
            {
                Text = "Windows-Hinting",
                Visible = true,
                Icon = _currentIcon
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Preferences...", null, (s, e) => PreferencesRequested?.Invoke(this, EventArgs.Empty));

            // Logging submenu
            contextMenu.Items.Add(CreateLoggingMenu(logger));

            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty));

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => ToggleRequested?.Invoke(this, EventArgs.Empty);
        }

        private ToolStripMenuItem CreateLoggingMenu(ILogger logger)
        {
            var loggingMenu = new ToolStripMenuItem("Logging");

            // Log level submenu items
            var levelItems = new ToolStripMenuItem[4];
            var levels = new[] { LogLevel.Debug, LogLevel.Info, LogLevel.Warning, LogLevel.Error };

            for (int i = 0; i < levels.Length; i++)
            {
                var level = levels[i];
                var item = new ToolStripMenuItem(level.ToString())
                {
                    Checked = logger.MinimumLevel == level,
                    Tag = level
                };
                item.Click += (s, e) =>
                {
                    logger.MinimumLevel = level;
                    foreach (var li in levelItems)
                        li!.Checked = (LogLevel)li.Tag! == level;
                    logger.Info($"Log level changed to {level}");
                };
                levelItems[i] = item;
            }

            var levelMenu = new ToolStripMenuItem("Level");
            levelMenu.DropDownItems.AddRange(levelItems!);
            loggingMenu.DropDownItems.Add(levelMenu);

            // File logging toggle
            if (_debugLogger != null)
            {
                var fileLoggingItem = new ToolStripMenuItem("Log to File")
                {
                    Checked = _debugLogger.FileLoggingEnabled,
                    CheckOnClick = true
                };
                fileLoggingItem.CheckedChanged += (s, e) =>
                {
                    _debugLogger.FileLoggingEnabled = fileLoggingItem.Checked;
                    if (fileLoggingItem.Checked)
                        logger.Info($"File logging enabled: {_debugLogger.CurrentLogFilePath}");
                    else
                        logger.Info("File logging disabled");
                };
                loggingMenu.DropDownItems.Add(fileLoggingItem);

                // View log (live tail)
                var viewLogItem = new ToolStripMenuItem("View Log");
                viewLogItem.ShortcutKeys = Keys.Control | Keys.L;
                viewLogItem.ShowShortcutKeys = true;
                viewLogItem.Click += (s, e) => ShowLogViewer();
                loggingMenu.DropDownItems.Add(viewLogItem);

                loggingMenu.DropDownItems.Add(new ToolStripSeparator());

                // Open log folder
                var openLogFolder = new ToolStripMenuItem("Open Log Folder");
                openLogFolder.Click += (s, e) =>
                {
                    var logDir = DebugLogger.LogDirectoryPath;
                    Directory.CreateDirectory(logDir);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = logDir,
                        UseShellExecute = true
                    });
                };
                loggingMenu.DropDownItems.Add(openLogFolder);
            }

            return loggingMenu;
        }

        private void ShowLogViewer()
        {
            if (_debugLogger == null)
                return;

            if (_logViewer != null && !_logViewer.IsDisposed)
            {
                _logViewer.Activate();
                return;
            }

            _logViewer = new LogViewerForm(_debugLogger);
            _logViewer.FormClosed += (_, _) => _logViewer = null;
            _logViewer.Show();
        }

        public void SetClickAction(ClickAction action)
        {
            string letter = action switch
            {
                ClickAction.LeftClick => "L",
                ClickAction.RightClick => "R",
                ClickAction.DoubleClick => "D",
                ClickAction.MouseMove => "M",
                _ => "H"
            };
            UpdateIcon(letter);
        }

        public void ResetIcon()
        {
            UpdateIcon("H");
        }

        private void UpdateIcon(string letter)
        {
            var oldIcon = _currentIcon;
            _currentIcon = CreateTrayIcon(letter);
            _trayIcon.Icon = _currentIcon;
            if (oldIcon != null)
            {
                DestroyIcon(oldIcon.Handle);
                oldIcon.Dispose();
            }
        }

        private static Icon CreateTrayIcon(string letter)
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

                // Centered letter
                using var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Color.FromArgb(255, 255, 220, 50));
                var textSize = g.MeasureString(letter, font);
                float x = (size - textSize.Width) / 2f;
                float y = (size - textSize.Height) / 2f;
                g.DrawString(letter, font, brush, x, y);
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

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        public void Dispose()
        {
            _trayIcon?.Dispose();
            if (_currentIcon != null)
            {
                DestroyIcon(_currentIcon.Handle);
                _currentIcon.Dispose();
            }
        }
    }
}
