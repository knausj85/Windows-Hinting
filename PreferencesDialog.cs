using System;
using System.Drawing;
using System.Windows.Forms;
using HintOverlay.Models;

namespace HintOverlay
{
    internal sealed class PreferencesDialog : Form
    {
        private readonly HintOverlayOptions _options;
        private CheckBox _chkShowRectangles = null!;
        private CheckBox _chkCtrl = null!;
        private CheckBox _chkAlt = null!;
        private CheckBox _chkShift = null!;
        private ComboBox _cmbKey = null!;
        private Button _btnOk = null!;
        private Button _btnCancel = null!;

        public PreferencesDialog(HintOverlayOptions options)
        {
            _options = options;

            // Enable automatic DPI scaling
            AutoScaleMode = AutoScaleMode.Dpi;

            InitializeComponents();
            LoadPreferences();
        }

        private void InitializeComponents()
        {
            Text = "HintOverlay Preferences";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // Use TableLayoutPanel for proper DPI scaling
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(10),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Show rectangles
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Hotkey section
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Spacer
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Buttons

            // Show rectangles checkbox
            _chkShowRectangles = new CheckBox
            {
                Text = "Show highlight rectangles",
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 10)
            };

            // Hotkey groupbox with proper layout
            var hotkeyGroup = new GroupBox
            {
                Text = "Toggle Hotkey",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 10)
            };

            var hotkeyLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(10),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            hotkeyLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Modifiers
            hotkeyLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Key selection

            // Modifiers panel
            var modifiersPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 10),
                WrapContents = false
            };

            _chkCtrl = new CheckBox
            {
                Text = "Ctrl",
                AutoSize = true,
                Margin = new Padding(0, 0, 10, 0)
            };

            _chkAlt = new CheckBox
            {
                Text = "Alt",
                AutoSize = true,
                Margin = new Padding(0, 0, 10, 0)
            };

            _chkShift = new CheckBox
            {
                Text = "Shift",
                AutoSize = true
            };

            modifiersPanel.Controls.Add(_chkCtrl);
            modifiersPanel.Controls.Add(_chkAlt);
            modifiersPanel.Controls.Add(_chkShift);

            // Key selection panel
            var keyPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Top,
                WrapContents = false
            };

            var lblKey = new Label
            {
                Text = "Key:",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 3, 10, 0)
            };

            _cmbKey = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 100
            };

            // Populate key dropdown with common keys
            for (char c = 'A'; c <= 'Z'; c++)
                _cmbKey.Items.Add(c.ToString());

            for (int i = 0; i <= 9; i++)
                _cmbKey.Items.Add(i.ToString());

            keyPanel.Controls.Add(lblKey);
            keyPanel.Controls.Add(_cmbKey);

            hotkeyLayout.Controls.Add(modifiersPanel, 0, 0);
            hotkeyLayout.Controls.Add(keyPanel, 0, 1);
            hotkeyGroup.Controls.Add(hotkeyLayout);

            // Buttons panel
            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            _btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(75, 0),
                Margin = new Padding(0, 0, 0, 0)
            };

            _btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(75, 0),
                Margin = new Padding(0, 0, 10, 0)
            };

            _btnOk.Click += BtnOk_Click;

            buttonPanel.Controls.Add(_btnCancel);
            buttonPanel.Controls.Add(_btnOk);

            // Add spacer panel
            var spacer = new Panel
            {
                Dock = DockStyle.Fill,
                MinimumSize = new Size(0, 10)
            };

            // Add all controls to main layout
            mainLayout.Controls.Add(_chkShowRectangles, 0, 0);
            mainLayout.Controls.Add(hotkeyGroup, 0, 1);
            mainLayout.Controls.Add(spacer, 0, 2);
            mainLayout.Controls.Add(buttonPanel, 0, 3);

            Controls.Add(mainLayout);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            // Set minimum size and auto-size the form
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(350, 0);
        }

        private void LoadPreferences()
        {
            _chkShowRectangles.Checked = _options.ShowRectangles;

            const int MOD_CONTROL = 0x0002;
            const int MOD_ALT = 0x0001;
            const int MOD_SHIFT = 0x0004;

            _chkCtrl.Checked = (_options.Hotkey.Modifiers & MOD_CONTROL) != 0;
            _chkAlt.Checked = (_options.Hotkey.Modifiers & MOD_ALT) != 0;
            _chkShift.Checked = (_options.Hotkey.Modifiers & MOD_SHIFT) != 0;

            // Map virtual key code to character
            if (_options.Hotkey.VirtualKey >= 0x41 && _options.Hotkey.VirtualKey <= 0x5A)
            {
                char keyChar = (char)_options.Hotkey.VirtualKey;
                _cmbKey.SelectedItem = keyChar.ToString();
            }
            else if (_options.Hotkey.VirtualKey >= 0x30 && _options.Hotkey.VirtualKey <= 0x39)
            {
                char keyChar = (char)_options.Hotkey.VirtualKey;
                _cmbKey.SelectedItem = keyChar.ToString();
            }
            else
            {
                _cmbKey.SelectedIndex = 7; // Default to 'H'
            }
        }

        private void BtnOk_Click(object? sender, EventArgs e)
        {
            const int MOD_CONTROL = 0x0002;
            const int MOD_ALT = 0x0001;
            const int MOD_SHIFT = 0x0004;

            _options.ShowRectangles = _chkShowRectangles.Checked;

            int modifiers = 0;
            if (_chkCtrl.Checked) modifiers |= MOD_CONTROL;
            if (_chkAlt.Checked) modifiers |= MOD_ALT;
            if (_chkShift.Checked) modifiers |= MOD_SHIFT;
            _options.Hotkey.Modifiers = modifiers;

            if (_cmbKey.SelectedItem != null)
            {
                string keyStr = _cmbKey.SelectedItem.ToString() ?? "H";
                _options.Hotkey.VirtualKey = keyStr[0];
            }

            var prefsService = new Services.PreferencesService();
            prefsService.Save(_options);
        }
    }
}