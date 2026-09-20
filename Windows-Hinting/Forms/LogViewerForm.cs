using System;
using System.Drawing;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using WindowsHinting.Logging;

namespace WindowsHinting.Forms
{
    internal sealed class LogViewerForm : Form
    {
        private readonly DebugLogger _logger;
        private readonly RichTextBox _logBox;
        private readonly ToolStrip _toolbar;
        private readonly ToolStripTextBox _filterBox;
        private readonly ToolStripButton _btnAutoScroll;
        private readonly ToolStripButton _btnClear;
        private readonly ToolStripButton _btnWordWrap;
        private readonly ToolStripButton _btnUiaOnly;
        private bool _autoScroll = true;
        private bool _uiaOnly;
        private string _filterText = "";
        // PROTOTYPE #78: buffer of all received entries so toggling a filter can re-render existing lines.
        private readonly System.Collections.Generic.List<(LogLevel Level, string Message)> _entries = new();
        private const int MaxEntries = 5000;

        private static readonly Color BackColor_ = Color.FromArgb(30, 30, 30);
        private static readonly Color ForeColorDefault = Color.FromArgb(204, 204, 204);
        private static readonly Color DebugColor = Color.FromArgb(140, 140, 140);
        private static readonly Color InfoColor = Color.FromArgb(86, 186, 255);
        private static readonly Color WarningColor = Color.FromArgb(255, 200, 60);
        private static readonly Color ErrorColor = Color.FromArgb(255, 85, 85);
        // PROTOTYPE #78: UIA/ExpandCollapse probe lines get a unique, vivid color for at-a-glance scanning.
        private static readonly Color UiaColor = Color.FromArgb(255, 121, 198);

        public LogViewerForm(DebugLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            Text = "Windows-Hinting — Log Viewer";
            Size = new Size(900, 520);
            MinimumSize = new Size(500, 300);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
            BackColor = BackColor_;
            KeyPreview = true;

            // Toolbar
            _toolbar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = ForeColorDefault,
                Renderer = new DarkToolStripRenderer()
            };

            _btnAutoScroll = new ToolStripButton("⤓ Auto-scroll")
            {
                Checked = _autoScroll,
                CheckOnClick = true,
                ForeColor = ForeColorDefault,
                ToolTipText = "Auto-scroll to latest log entry"
            };
            _btnAutoScroll.CheckedChanged += (_, _) => _autoScroll = _btnAutoScroll.Checked;

            _btnWordWrap = new ToolStripButton("↩ Word Wrap")
            {
                Checked = false,
                CheckOnClick = true,
                ForeColor = ForeColorDefault,
                ToolTipText = "Toggle word wrap"
            };
            _btnWordWrap.CheckedChanged += (_, _) => _logBox.WordWrap = _btnWordWrap.Checked;

            _btnUiaOnly = new ToolStripButton("🔎 UIA only")
            {
                Checked = false,
                CheckOnClick = true,
                ForeColor = UiaColor,
                ToolTipText = "Show only [UIA] diagnostic events"
            };
            _btnUiaOnly.CheckedChanged += (_, _) => { _uiaOnly = _btnUiaOnly.Checked; RefreshView(); };

            _btnClear = new ToolStripButton("✕ Clear")
            {
                ForeColor = ForeColorDefault,
                ToolTipText = "Clear the log view"
            };
            _btnClear.Click += (_, _) => { _entries.Clear(); _logBox.Clear(); };

            var filterLabel = new ToolStripLabel("Filter:")
            {
                ForeColor = ForeColorDefault
            };

            _filterBox = new ToolStripTextBox
            {
                Size = new Size(200, 25),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = ForeColorDefault,
                ToolTipText = "Type to filter log messages (Ctrl+F)"
            };
            _filterBox.TextChanged += (_, _) => { _filterText = _filterBox.Text; RefreshView(); };

            _toolbar.Items.Add(_btnAutoScroll);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(_btnWordWrap);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(_btnUiaOnly);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(_btnClear);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(filterLabel);
            _toolbar.Items.Add(_filterBox);

            // Log output
            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = BackColor_,
                ForeColor = ForeColorDefault,
                Font = new Font("Cascadia Mono", 9.5f, FontStyle.Regular, GraphicsUnit.Point,
                    0, // gdiCharSet
                    false),
                WordWrap = false,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Both
            };

            // Fall back if Cascadia Mono isn't installed
            if (_logBox.Font.Name != "Cascadia Mono")
                _logBox.Font = new Font("Consolas", 9.5f);

            Controls.Add(_logBox);
            Controls.Add(_toolbar);

            _logger.LogMessageWritten += OnLogMessageWritten;
        }

        private void OnLogMessageWritten(object? sender, LogMessageEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(() => AppendLogLine(e.Level, e.Message));
            }
            catch (ObjectDisposedException)
            {
                // Form closed between check and invoke
            }
        }

        // PROTOTYPE #78: any UIA-tagged line wins the unique color, regardless of level.
        private static Color ColorFor(LogLevel level, string message) =>
            message.Contains("[UIA]", StringComparison.Ordinal)
                ? UiaColor
                : level switch
                {
                    LogLevel.Debug => DebugColor,
                    LogLevel.Info => InfoColor,
                    LogLevel.Warning => WarningColor,
                    LogLevel.Error => ErrorColor,
                    _ => ForeColorDefault
                };

        // Whether a line survives the active filters (UIA-only toggle + text filter).
        private bool Passes(string message)
        {
            if (_uiaOnly && !message.Contains("[UIA]", StringComparison.Ordinal)) return false;
            if (!string.IsNullOrEmpty(_filterText) &&
                !message.Contains(_filterText, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private void AppendLogLine(LogLevel level, string message)
        {
            _entries.Add((level, message));
            if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
            if (Passes(message)) RenderLine(level, message);
        }

        private void RenderLine(LogLevel level, string message)
        {
            // Suppress repainting while we modify the text
            PInvoke.SendMessage((HWND)_logBox.Handle, PInvoke.WM_SETREDRAW, 0, 0);
            try
            {
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.SelectionLength = 0;
                _logBox.SelectionColor = ColorFor(level, message);
                _logBox.AppendText(message + Environment.NewLine);

                // Cap at ~10,000 lines using O(1) line-count estimation
                int lineCount = _logBox.GetLineFromCharIndex(_logBox.TextLength) + 1;
                if (lineCount > 10_000)
                {
                    _logBox.SelectionStart = 0;
                    _logBox.SelectionLength = _logBox.GetFirstCharIndexFromLine(2000);
                    _logBox.SelectedText = "";
                }

                if (_autoScroll)
                {
                    _logBox.SelectionStart = _logBox.TextLength;
                    _logBox.ScrollToCaret();
                }
            }
            finally
            {
                PInvoke.SendMessage((HWND)_logBox.Handle, PInvoke.WM_SETREDRAW, 1, 0);
                _logBox.Invalidate();
            }
        }

        // Re-render the whole view from the buffer — used when a filter (UIA-only / text) changes so the
        // filter applies to lines ALREADY received, not just future ones.
        private void RefreshView()
        {
            if (IsDisposed || !IsHandleCreated) return;
            PInvoke.SendMessage((HWND)_logBox.Handle, PInvoke.WM_SETREDRAW, 0, 0);
            try
            {
                _logBox.Clear();
                foreach (var (lvl, msg) in _entries)
                {
                    if (!Passes(msg)) continue;
                    _logBox.SelectionStart = _logBox.TextLength;
                    _logBox.SelectionLength = 0;
                    _logBox.SelectionColor = ColorFor(lvl, msg);
                    _logBox.AppendText(msg + Environment.NewLine);
                }
                if (_autoScroll)
                {
                    _logBox.SelectionStart = _logBox.TextLength;
                    _logBox.ScrollToCaret();
                }
            }
            finally
            {
                PInvoke.SendMessage((HWND)_logBox.Handle, PInvoke.WM_SETREDRAW, 1, 0);
                _logBox.Invalidate();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                _filterBox.Focus();
                _filterBox.SelectAll();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _logger.LogMessageWritten -= OnLogMessageWritten;
            base.OnFormClosing(e);
        }

        /// <summary>
        /// Dark renderer for the toolbar to match the log viewer theme.
        /// </summary>
        private sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
        {
            public DarkToolStripRenderer()
                : base(new DarkColorTable()) { }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin => Color.FromArgb(45, 45, 45);
            public override Color ToolStripGradientMiddle => Color.FromArgb(45, 45, 45);
            public override Color ToolStripGradientEnd => Color.FromArgb(45, 45, 45);
            public override Color SeparatorDark => Color.FromArgb(70, 70, 70);
            public override Color SeparatorLight => Color.FromArgb(70, 70, 70);
            public override Color ButtonSelectedHighlight => Color.FromArgb(60, 60, 60);
            public override Color ButtonSelectedGradientBegin => Color.FromArgb(60, 60, 60);
            public override Color ButtonSelectedGradientEnd => Color.FromArgb(60, 60, 60);
            public override Color ButtonCheckedGradientBegin => Color.FromArgb(70, 70, 70);
            public override Color ButtonCheckedGradientEnd => Color.FromArgb(70, 70, 70);
            public override Color ButtonPressedGradientBegin => Color.FromArgb(80, 80, 80);
            public override Color ButtonPressedGradientEnd => Color.FromArgb(80, 80, 80);
            public override Color MenuItemSelected => Color.FromArgb(60, 60, 60);
            public override Color ImageMarginGradientBegin => Color.FromArgb(45, 45, 45);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(45, 45, 45);
            public override Color ImageMarginGradientEnd => Color.FromArgb(45, 45, 45);
        }
    }
}
