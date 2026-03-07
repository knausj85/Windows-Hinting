using System;
using System.Drawing;
using System.Windows.Forms;
using HintOverlay.Models;
using HintOverlay.Services;

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
            _options = options ?? throw new ArgumentNullException(nameof(options));
            InitializeComponent();
            LoadPreferences();
        }

        private void InitializeComponent()
        {
            Text = "Preferences";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(10)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Show rectangles checkbox
            _chkShowRectangles = new CheckBox
            {
                Text = "Show element rectangles",
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            mainLayout.Controls.Add(_chkShowRectangles, 0, 0);

            // Hotkey configuration
            var hotkeyGroup = new GroupBox
            {
                Text = "Global Hotkey",
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };

            var hotkeyLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                AutoSize = true
            };
            hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));

            var modifiersLabel = new Label
            {
                Text = "Modifiers:",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            hotkeyLayout.Controls.Add(modifiersLabel, 0, 0);

            var modifiersPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight
            };
            _chkCtrl = new CheckBox { Text = "Ctrl", AutoSize = true };
            _chkAlt = new CheckBox { Text = "Alt", AutoSize = true };
            _chkShift = new CheckBox { Text = "Shift", AutoSize = true };
            modifiersPanel.Controls.Add(_chkCtrl);
            modifiersPanel.Controls.Add(_chkAlt);
            modifiersPanel.Controls.Add(_chkShift);
            hotkeyLayout.Controls.Add(modifiersPanel, 1, 0);

            var keyLabel = new Label
            {
                Text = "Key:",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            hotkeyLayout.Controls.Add(keyLabel, 0, 1);

            _cmbKey = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill
            };
            for (char c = 'A'; c <= 'Z'; c++)
                _cmbKey.Items.Add(c.ToString());
            for (char c = '0'; c <= '9'; c++)
                _cmbKey.Items.Add(c.ToString());
            hotkeyLayout.Controls.Add(_cmbKey, 1, 1);

            hotkeyGroup.Controls.Add(hotkeyLayout);
            mainLayout.Controls.Add(hotkeyGroup, 0, 1);

            // Spacer
            mainLayout.Controls.Add(new Panel { Height = 10 }, 0, 2);

            // Buttons
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

            var prefsService = new PreferencesService();
            prefsService.Save(_options);
        }
    }
}