using System;
using System.Drawing;
using System.Windows.Forms;
using HintOverlay.Logging;

namespace HintOverlay
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
        private bool _autoScroll = true;
        private string _filterText = "";

        private static readonly Color BackColor_ = Color.FromArgb(30, 30, 30);
        private static readonly Color ForeColorDefault = Color.FromArgb(204, 204, 204);
        private static readonly Color DebugColor = Color.FromArgb(140, 140, 140);
        private static readonly Color InfoColor = Color.FromArgb(86, 186, 255);
        private static readonly Color WarningColor = Color.FromArgb(255, 200, 60);
        private static readonly Color ErrorColor = Color.FromArgb(255, 85, 85);

        public LogViewerForm(DebugLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            Text = "Windows-Hinting — Log Viewer";
            Size = new Size(900, 520);
            MinimumSize = new Size(500, 300);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = null;
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

            _btnClear = new ToolStripButton("✕ Clear")
            {
                ForeColor = ForeColorDefault,
                ToolTipText = "Clear the log view"
            };
            _btnClear.Click += (_, _) => _logBox.Clear();

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
            _filterBox.TextChanged += (_, _) => _filterText = _filterBox.Text;

            _toolbar.Items.Add(_btnAutoScroll);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(_btnWordWrap);
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

        private void AppendLogLine(LogLevel level, string message)
        {
            // Apply text filter
            if (!string.IsNullOrEmpty(_filterText) &&
                !message.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                return;

            var color = level switch
            {
                LogLevel.Debug => DebugColor,
                LogLevel.Info => InfoColor,
                LogLevel.Warning => WarningColor,
                LogLevel.Error => ErrorColor,
                _ => ForeColorDefault
            };

            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.SelectionLength = 0;
            _logBox.SelectionColor = color;
            _logBox.AppendText(message + Environment.NewLine);

            // Cap at 10,000 lines to prevent memory issues
            if (_logBox.Lines.Length > 10_000)
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
