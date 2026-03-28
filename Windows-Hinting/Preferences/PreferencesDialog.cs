using System.ComponentModel;
using WindowsHinting.Configuration;
using WindowsHinting.Controls;
using WindowsHinting.Models;
using WindowsHinting.Services;

namespace Preferences
{
    internal sealed class PreferencesDialog : Form
    {
        private readonly HintOverlayOptions _options;
        private readonly StartupService _startupService;

        // General tab controls
        private CheckBox _chkShowRectangles = null!;
        private CheckBox _chkStartWithWindows = null!;
        private readonly Dictionary<HintPosition, RadioButton> _positionButtons = new();
        private CheckBox _chkHotkeyEnabled = null!;
        private HotkeyRecorderControl _hotkeyRecorder = null!;
        private CheckBox _chkTaskbarHotkeyEnabled = null!;
        private HotkeyRecorderControl _taskbarHotkeyRecorder = null!;
        private CheckBox _chkClickActionShortcutsEnabled = null!;
        private HotkeyRecorderControl _leftClickKeyRecorder = null!;
        private HotkeyRecorderControl _rightClickKeyRecorder = null!;
        private HotkeyRecorderControl _doubleClickKeyRecorder = null!;
        private HotkeyRecorderControl _mouseMoveKeyRecorder = null!;
        private HotkeyRecorderControl _ctrlClickKeyRecorder = null!;
        private HotkeyRecorderControl _shiftClickKeyRecorder = null!;
        private TrackBar _overlapThresholdTrackBar = null!;
        private Label _overlapThresholdValueLabel = null!;

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

        /// <summary>Raised when the user selects a different hint position.</summary>
        public event EventHandler<HintPosition>? HintPositionChanged;

        public PreferencesDialog(HintOverlayOptions options, StartupService startupService)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
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
                RowCount = 8,
                Padding = new Padding(10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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

            // Start with Windows checkbox
            _chkStartWithWindows = new CheckBox
            {
                Text = "Start with Windows",
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(_chkStartWithWindows, 0, 1);

            // Hint position grid
            var hintPosGroup = new GroupBox
            {
                Text = "Hint label position",
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };

            var posGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                AutoSize = true
            };
            posGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            posGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            posGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            posGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            posGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            posGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var positions = new (HintPosition pos, string label, int col, int row)[]
            {
                (HintPosition.UpperLeft,   "Upper Left",   0, 0),
                (HintPosition.UpperCenter, "Upper Center", 1, 0),
                (HintPosition.UpperRight,  "Upper Right",  2, 0),
                (HintPosition.Left,        "Left",         0, 1),
                (HintPosition.Center,      "Center",       1, 1),
                (HintPosition.Right,       "Right",        2, 1),
                (HintPosition.LowerLeft,   "Lower Left",   0, 2),
                (HintPosition.LowerCenter, "Lower Center", 1, 2),
                (HintPosition.LowerRight,  "Lower Right",  2, 2),
            };

            foreach (var (pos, label, col, row) in positions)
            {
                var rb = new RadioButton
                {
                    Text = label,
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Tag = pos
                };
                rb.CheckedChanged += (s, _) =>
                {
                    if (s is RadioButton { Checked: true, Tag: HintPosition selectedPos })
                        HintPositionChanged?.Invoke(this, selectedPos);
                };
                _positionButtons[pos] = rb;
                posGrid.Controls.Add(rb, col, row);
            }

            hintPosGroup.Controls.Add(posGrid);
            layout.Controls.Add(hintPosGroup, 0, 2);

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
            layout.Controls.Add(hotkeyGroup, 0, 3);

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
            layout.Controls.Add(taskbarHotkeyGroup, 0, 4);

            // Click action shortcuts configuration
            var clickActionGroup = new GroupBox
            {
                Text = "Click Action Shortcuts",
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            var clickActionLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 7,
                AutoSize = true
            };
            clickActionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            clickActionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            clickActionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 7; i++)
                clickActionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _chkClickActionShortcutsEnabled = new CheckBox
            {
                Text = "Enable click action shortcuts (Shift + key while hints are active)",
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            _chkClickActionShortcutsEnabled.CheckedChanged += (_, _) =>
            {
                bool enabled = _chkClickActionShortcutsEnabled.Checked;
                _leftClickKeyRecorder.Enabled = enabled;
                _rightClickKeyRecorder.Enabled = enabled;
                _doubleClickKeyRecorder.Enabled = enabled;
                _mouseMoveKeyRecorder.Enabled = enabled;
                _ctrlClickKeyRecorder.Enabled = enabled;
                _shiftClickKeyRecorder.Enabled = enabled;
            };
            clickActionLayout.SetColumnSpan(_chkClickActionShortcutsEnabled, 3);
            clickActionLayout.Controls.Add(_chkClickActionShortcutsEnabled, 0, 0);

            var defaults = new ClickActionShortcutOptions();

            _leftClickKeyRecorder = new HotkeyRecorderControl { Mode = RecorderMode.SingleKey, Dock = DockStyle.Fill, Height = 28 };
            AddShortcutRow(clickActionLayout, 1, "Left click:", _leftClickKeyRecorder, defaults.LeftClickKey);

            _rightClickKeyRecorder = new HotkeyRecorderControl { Mode = RecorderMode.SingleKey, Dock = DockStyle.Fill, Height = 28 };
            AddShortcutRow(clickActionLayout, 2, "Right click:", _rightClickKeyRecorder, defaults.RightClickKey);

            _doubleClickKeyRecorder = new HotkeyRecorderControl { Mode = RecorderMode.SingleKey, Dock = DockStyle.Fill, Height = 28 };
            AddShortcutRow(clickActionLayout, 3, "Double click:", _doubleClickKeyRecorder, defaults.DoubleClickKey);

            _mouseMoveKeyRecorder = new HotkeyRecorderControl { Mode = RecorderMode.SingleKey, Dock = DockStyle.Fill, Height = 28 };
            AddShortcutRow(clickActionLayout, 4, "Move mouse:", _mouseMoveKeyRecorder, defaults.MouseMoveKey);

            _ctrlClickKeyRecorder = new HotkeyRecorderControl { Mode = RecorderMode.SingleKey, Dock = DockStyle.Fill, Height = 28 };
            AddShortcutRow(clickActionLayout, 5, "Ctrl+Click:", _ctrlClickKeyRecorder, defaults.CtrlClickKey);

            _shiftClickKeyRecorder = new HotkeyRecorderControl { Mode = RecorderMode.SingleKey, Dock = DockStyle.Fill, Height = 28 };
            AddShortcutRow(clickActionLayout, 6, "Shift+Click:", _shiftClickKeyRecorder, defaults.ShiftClickKey);

            clickActionGroup.Controls.Add(clickActionLayout);
            layout.Controls.Add(clickActionGroup, 0, 5);

            // Overlap threshold slider
            var overlapGroup = new GroupBox
            {
                Text = "Overlap Deduplication",
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            var overlapLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                AutoSize = true
            };
            overlapLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            overlapLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            overlapLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            overlapLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var overlapDescription = new Label
            {
                Text = "Area overlap threshold for removing container elements. " +
                       "Lower values remove more aggressively.",
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 0, 0, 4)
            };
            overlapLayout.SetColumnSpan(overlapDescription, 2);
            overlapLayout.Controls.Add(overlapDescription, 0, 0);

            _overlapThresholdTrackBar = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                SmallChange = 5,
                LargeChange = 10,
                Dock = DockStyle.Fill
            };
            _overlapThresholdValueLabel = new Label
            {
                Text = "25%",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(4, 6, 0, 0)
            };
            _overlapThresholdTrackBar.ValueChanged += (_, _) =>
            {
                _overlapThresholdValueLabel.Text = $"{_overlapThresholdTrackBar.Value}%";
            };
            overlapLayout.Controls.Add(_overlapThresholdTrackBar, 0, 1);
            overlapLayout.Controls.Add(_overlapThresholdValueLabel, 1, 1);

            overlapGroup.Controls.Add(overlapLayout);
            layout.Controls.Add(overlapGroup, 0, 6);

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
                       "Leave a field empty to match any value. Rules are evaluated top to bottom.\n" +
                       "Built-in rules cannot be removed but their properties can be modified.",
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
                HeaderText = "Name",
                DataPropertyName = "Name",
                FillWeight = 18,
                ReadOnly = true
            });

            _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Executable",
                DataPropertyName = "ExecutableName",
                FillWeight = 18
            });

            _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Class Name",
                DataPropertyName = "ClassName",
                FillWeight = 22
            });

            _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Window Title",
                DataPropertyName = "WindowTitle",
                FillWeight = 15
            });

            var titleMatchColumn = new DataGridViewComboBoxColumn
            {
                HeaderText = "Title Match",
                DataPropertyName = "TitleMatchMode",
                DataSource = Enum.GetValues<TitleMatchMode>(),
                FillWeight = 12
            };
            _rulesGrid.Columns.Add(titleMatchColumn);

            var strategyColumn = new DataGridViewComboBoxColumn
            {
                HeaderText = "Strategy",
                DataPropertyName = "Strategy",
                DataSource = Enum.GetValues<RootStrategy>(),
                FillWeight = 15
            };
            _rulesGrid.Columns.Add(strategyColumn);

            _rulesGrid.DataError += (_, e) => e.ThrowException = false;

            // Prevent deleting built-in default rules
            _rulesGrid.UserDeletingRow += (_, e) =>
            {
                if (e.Row?.DataBoundItem is WindowRule rule && rule.IsDefault)
                    e.Cancel = true;
            };

            // Style default rows with a subtle background
            _rulesGrid.RowPrePaint += (_, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= _rulesBindingList.Count)
                    return;
                var rule = _rulesBindingList[e.RowIndex];
                var row = _rulesGrid.Rows[e.RowIndex];
                if (rule.IsDefault)
                {
                    row.DefaultCellStyle.BackColor = SystemColors.Control;
                    row.DefaultCellStyle.ForeColor = SystemColors.ControlText;
                }
                else
                {
                    row.DefaultCellStyle.BackColor = SystemColors.Window;
                    row.DefaultCellStyle.ForeColor = SystemColors.WindowText;
                }
            };

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

        private static void AddShortcutRow(TableLayoutPanel layout, int row, string labelText,
            HotkeyRecorderControl recorder, int defaultVirtualKey)
        {
            var label = new Label
            {
                Text = labelText,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 6, 0, 0)
            };
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(recorder, 1, row);

            var resetButton = new Button
            {
                Text = "Reset",
                AutoSize = true,
                Padding = new Padding(4, 0, 4, 0)
            };
            resetButton.Click += (_, _) => recorder.SetKey(defaultVirtualKey);
            layout.Controls.Add(resetButton, 2, row);
        }

        private void LoadPreferences()
        {
            _chkShowRectangles.Checked = _options.ShowRectangles;
            _chkStartWithWindows.Checked = _startupService.IsEnabled;
            if (_positionButtons.TryGetValue(_options.HintPosition, out var rb))
                rb.Checked = true;
            else
                _positionButtons[HintPosition.UpperLeft].Checked = true;
            _chkHotkeyEnabled.Checked = _options.Hotkey.Enabled;
            _hotkeyRecorder.Enabled = _options.Hotkey.Enabled;
            _hotkeyRecorder.SetHotkey(_options.Hotkey.Modifiers, _options.Hotkey.VirtualKey);

            _chkTaskbarHotkeyEnabled.Checked = _options.TaskbarHotkey.Enabled;
            _taskbarHotkeyRecorder.Enabled = _options.TaskbarHotkey.Enabled;
            _taskbarHotkeyRecorder.SetHotkey(_options.TaskbarHotkey.Modifiers, _options.TaskbarHotkey.VirtualKey);

            _chkClickActionShortcutsEnabled.Checked = _options.ClickActionShortcuts.Enabled;
            var shortcutsEnabled = _options.ClickActionShortcuts.Enabled;
            _leftClickKeyRecorder.Enabled = shortcutsEnabled;
            _leftClickKeyRecorder.SetKey(_options.ClickActionShortcuts.LeftClickKey);
            _rightClickKeyRecorder.Enabled = shortcutsEnabled;
            _rightClickKeyRecorder.SetKey(_options.ClickActionShortcuts.RightClickKey);
            _doubleClickKeyRecorder.Enabled = shortcutsEnabled;
            _doubleClickKeyRecorder.SetKey(_options.ClickActionShortcuts.DoubleClickKey);
            _mouseMoveKeyRecorder.Enabled = shortcutsEnabled;
            _mouseMoveKeyRecorder.SetKey(_options.ClickActionShortcuts.MouseMoveKey);
            _ctrlClickKeyRecorder.Enabled = shortcutsEnabled;
            _ctrlClickKeyRecorder.SetKey(_options.ClickActionShortcuts.CtrlClickKey);
            _shiftClickKeyRecorder.Enabled = shortcutsEnabled;
            _shiftClickKeyRecorder.SetKey(_options.ClickActionShortcuts.ShiftClickKey);

            _overlapThresholdTrackBar.Value = Math.Clamp(_options.OverlapThreshold, 0, 100);
            _overlapThresholdValueLabel.Text = $"{_overlapThresholdTrackBar.Value}%";

            // Window rules — always merge with defaults so built-in rules are present
            var rules = WindowRuleRegistry.MergeWithDefaults(_options.WindowRules);
            _rulesBindingList = new BindingList<WindowRule>(rules);
            _rulesGrid.DataSource = _rulesBindingList;
        }

        private void BtnOk_Click(object? sender, EventArgs e)
        {
            _options.ShowRectangles = _chkShowRectangles.Checked;
            _startupService.Apply(_chkStartWithWindows.Checked);
            _options.HintPosition = _positionButtons
                .FirstOrDefault(kvp => kvp.Value.Checked).Key;
            _options.Hotkey.Enabled = _chkHotkeyEnabled.Checked;
            _options.Hotkey.Modifiers = _hotkeyRecorder.HotkeyModifiers;
            _options.Hotkey.VirtualKey = _hotkeyRecorder.HotkeyVirtualKey;

            _options.TaskbarHotkey.Enabled = _chkTaskbarHotkeyEnabled.Checked;
            _options.TaskbarHotkey.Modifiers = _taskbarHotkeyRecorder.HotkeyModifiers;
            _options.TaskbarHotkey.VirtualKey = _taskbarHotkeyRecorder.HotkeyVirtualKey;

            _options.ClickActionShortcuts.Enabled = _chkClickActionShortcutsEnabled.Checked;
            _options.ClickActionShortcuts.LeftClickKey = _leftClickKeyRecorder.VirtualKey;
            _options.ClickActionShortcuts.RightClickKey = _rightClickKeyRecorder.VirtualKey;
            _options.ClickActionShortcuts.DoubleClickKey = _doubleClickKeyRecorder.VirtualKey;
            _options.ClickActionShortcuts.MouseMoveKey = _mouseMoveKeyRecorder.VirtualKey;
            _options.ClickActionShortcuts.CtrlClickKey = _ctrlClickKeyRecorder.VirtualKey;
            _options.ClickActionShortcuts.ShiftClickKey = _shiftClickKeyRecorder.VirtualKey;

            _options.OverlapThreshold = _overlapThresholdTrackBar.Value;

            // Collect window rules from the grid (exclude incomplete new-row entries)
            _options.WindowRules = _rulesBindingList
                .Where(r => r.IsDefault
                          || !string.IsNullOrWhiteSpace(r.ExecutableName)
                          || !string.IsNullOrWhiteSpace(r.ClassName)
                          || !string.IsNullOrWhiteSpace(r.WindowTitle))
                .ToList();

            var prefsService = new PreferencesService();
            prefsService.Save(_options);
        }
    }
}
