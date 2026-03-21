using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HintOverlay.Configuration;
using HintOverlay.Controls;
using HintOverlay.Models;
using HintOverlay.Services;

namespace HintOverlay
{
    internal sealed class PreferencesDialog : Form
    {
        private readonly HintOverlayOptions _options;

        // General tab controls
        private CheckBox _chkShowRectangles = null!;
        private CheckBox _chkHotkeyEnabled = null!;
        private HotkeyRecorderControl _hotkeyRecorder = null!;
        private CheckBox _chkTaskbarHotkeyEnabled = null!;
        private HotkeyRecorderControl _taskbarHotkeyRecorder = null!;

        // Window Rules tab controls
        private DataGridView _rulesGrid = null!;
        private BindingList<WindowRule> _rulesBindingList = null!;

        // Dialog buttons
        private Button _btnOk = null!;
        private Button _btnCancel = null!;

        /// <summary>Raised when the hotkey recorder begins capturing a shortcut.</summary>
        public event EventHandler? HotkeyRecordingStarted;

        /// <summary>Raised when the hotkey recorder stops capturing.</summary>
        public event EventHandler? HotkeyRecordingStopped;

        public PreferencesDialog(HintOverlayOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            InitializeComponent();
            LoadPreferences();
        }

        private void InitializeComponent()
        {
            Text = "Preferences";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;

            var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            ClientSize = new Size(
                Math.Max(640, (int)(workArea.Width * 0.35)),
                Math.Max(420, (int)(workArea.Height * 0.45)));
            MinimumSize = new Size(520, 360);

            // Main layout: TabControl + button row
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var tabControl = new TabControl { Dock = DockStyle.Fill };

            // ── General Tab ──────────────────────────────────────────
            tabControl.TabPages.Add(CreateGeneralTab());

            // ── Window Rules Tab ─────────────────────────────────────
            tabControl.TabPages.Add(CreateWindowRulesTab());

            mainLayout.Controls.Add(tabControl, 0, 0);

            // ── Buttons ──────────────────────────────────────────────
            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true
            };

            _btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                Padding = new Padding(10, 3, 10, 3)
            };
            buttonPanel.Controls.Add(_btnCancel);

            _btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                AutoSize = true,
                Padding = new Padding(10, 3, 10, 3)
            };
            _btnOk.Click += BtnOk_Click;
            buttonPanel.Controls.Add(_btnOk);

            mainLayout.Controls.Add(buttonPanel, 0, 1);
            Controls.Add(mainLayout);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private TabPage CreateGeneralTab()
        {
            var tab = new TabPage("General");

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Show rectangles checkbox
            _chkShowRectangles = new CheckBox
            {
                Text = "Show element rectangles",
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(_chkShowRectangles, 0, 0);

            // Hotkey configuration
            var hotkeyGroup = new GroupBox
            {
                Text = "Global Hotkey",
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            _chkHotkeyEnabled = new CheckBox
            {
                Text = "Enable global hotkey",
                AutoSize = true,
                Dock = DockStyle.Top
            };
            _chkHotkeyEnabled.CheckedChanged += (_, _) =>
            {
                _hotkeyRecorder.Enabled = _chkHotkeyEnabled.Checked;
            };

            _hotkeyRecorder = new HotkeyRecorderControl
            {
                Dock = DockStyle.Top,
                Height = 32
            };
            _hotkeyRecorder.RecordingStarted += (s, e) => HotkeyRecordingStarted?.Invoke(this, EventArgs.Empty);
            _hotkeyRecorder.RecordingStopped += (s, e) => HotkeyRecordingStopped?.Invoke(this, EventArgs.Empty);

            hotkeyGroup.Controls.Add(_hotkeyRecorder);
            hotkeyGroup.Controls.Add(_chkHotkeyEnabled);
            layout.Controls.Add(hotkeyGroup, 0, 1);

            // Taskbar hotkey configuration
            var taskbarHotkeyGroup = new GroupBox
            {
                Text = "Taskbar Hotkey",
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            _chkTaskbarHotkeyEnabled = new CheckBox
            {
                Text = "Enable taskbar hotkey",
                AutoSize = true,
                Dock = DockStyle.Top
            };
            _chkTaskbarHotkeyEnabled.CheckedChanged += (_, _) =>
            {
                _taskbarHotkeyRecorder.Enabled = _chkTaskbarHotkeyEnabled.Checked;
            };

            _taskbarHotkeyRecorder = new HotkeyRecorderControl
            {
                Dock = DockStyle.Top,
                Height = 32
            };
            _taskbarHotkeyRecorder.RecordingStarted += (s, e) => HotkeyRecordingStarted?.Invoke(this, EventArgs.Empty);
            _taskbarHotkeyRecorder.RecordingStopped += (s, e) => HotkeyRecordingStopped?.Invoke(this, EventArgs.Empty);

            taskbarHotkeyGroup.Controls.Add(_taskbarHotkeyRecorder);
            taskbarHotkeyGroup.Controls.Add(_chkTaskbarHotkeyEnabled);
            layout.Controls.Add(taskbarHotkeyGroup, 0, 2);

            tab.Controls.Add(layout);
            return tab;
        }

        private TabPage CreateWindowRulesTab()
        {
            var tab = new TabPage("Window Rules");

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var description = new Label
            {
                Text = "Rules control how UI elements are discovered for each application.\n" +
                       "Leave a field empty to match any value. Rules are evaluated top to bottom.",
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 0, 0, 6)
            };
            layout.Controls.Add(description, 0, 0);

            // DataGridView
            _rulesGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                EditMode = DataGridViewEditMode.EditOnEnter,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.Fixed3D,
                RowHeadersWidth = 30,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Executable",
                DataPropertyName = "ExecutableName",
                FillWeight = 25
            });

            _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Class Name",
                DataPropertyName = "ClassName",
                FillWeight = 35
            });

            _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Window Title",
                DataPropertyName = "WindowTitle",
                FillWeight = 20
            });

            var strategyColumn = new DataGridViewComboBoxColumn
            {
                HeaderText = "Strategy",
                DataPropertyName = "Strategy",
                DataSource = Enum.GetValues<RootStrategy>(),
                FillWeight = 20
            };
            _rulesGrid.Columns.Add(strategyColumn);

            _rulesGrid.DataError += (_, e) => e.ThrowException = false;

            layout.Controls.Add(_rulesGrid, 0, 1);

            // Bottom button bar
            var bottomPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Dock = DockStyle.Fill
            };

            var btnResetDefaults = new Button
            {
                Text = "Reset to Defaults",
                AutoSize = true,
                Padding = new Padding(6, 2, 6, 2)
            };
            btnResetDefaults.Click += (_, _) =>
            {
                _rulesBindingList.Clear();
                foreach (var rule in WindowRuleRegistry.GetDefaultRules())
                    _rulesBindingList.Add(rule);
            };
            bottomPanel.Controls.Add(btnResetDefaults);

            layout.Controls.Add(bottomPanel, 0, 2);

            tab.Controls.Add(layout);
            return tab;
        }

        private void LoadPreferences()
        {
            _chkShowRectangles.Checked = _options.ShowRectangles;
            _chkHotkeyEnabled.Checked = _options.Hotkey.Enabled;
            _hotkeyRecorder.Enabled = _options.Hotkey.Enabled;
            _hotkeyRecorder.SetHotkey(_options.Hotkey.Modifiers, _options.Hotkey.VirtualKey);

            _chkTaskbarHotkeyEnabled.Checked = _options.TaskbarHotkey.Enabled;
            _taskbarHotkeyRecorder.Enabled = _options.TaskbarHotkey.Enabled;
            _taskbarHotkeyRecorder.SetHotkey(_options.TaskbarHotkey.Modifiers, _options.TaskbarHotkey.VirtualKey);

            // Window rules
            var rules = _options.WindowRules ?? WindowRuleRegistry.GetDefaultRules();
            _rulesBindingList = new BindingList<WindowRule>(
                rules.Select(r => new WindowRule
                {
                    ExecutableName = r.ExecutableName,
                    ClassName = r.ClassName,
                    WindowTitle = r.WindowTitle,
                    Strategy = r.Strategy
                }).ToList()
            );
            _rulesGrid.DataSource = _rulesBindingList;
        }

        private void BtnOk_Click(object? sender, EventArgs e)
        {
            _options.ShowRectangles = _chkShowRectangles.Checked;
            _options.Hotkey.Enabled = _chkHotkeyEnabled.Checked;
            _options.Hotkey.Modifiers = _hotkeyRecorder.HotkeyModifiers;
            _options.Hotkey.VirtualKey = _hotkeyRecorder.HotkeyVirtualKey;

            _options.TaskbarHotkey.Enabled = _chkTaskbarHotkeyEnabled.Checked;
            _options.TaskbarHotkey.Modifiers = _taskbarHotkeyRecorder.HotkeyModifiers;
            _options.TaskbarHotkey.VirtualKey = _taskbarHotkeyRecorder.HotkeyVirtualKey;

            // Collect window rules from the grid (exclude incomplete new-row entries)
            _options.WindowRules = _rulesBindingList
                .Where(r => !string.IsNullOrWhiteSpace(r.ExecutableName)
                          || !string.IsNullOrWhiteSpace(r.ClassName)
                          || !string.IsNullOrWhiteSpace(r.WindowTitle))
                .ToList();

            var prefsService = new PreferencesService();
            prefsService.Save(_options);
        }
    }
}