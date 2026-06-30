using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WindowsHinting.Services;

namespace WindowsHinting.Forms
{
    internal sealed class AboutForm : Form
    {
        private const string GitHubUrl = "https://github.com/knausj85/Windows-Hinting";

        public event EventHandler? CheckForUpdatesRequested;

        private static readonly Color BackColor_ = Color.FromArgb(30, 30, 30);
        private static readonly Color ForeColorDefault = Color.FromArgb(204, 204, 204);
        private static readonly Color AccentColor = Color.FromArgb(255, 220, 50);
        private static readonly Color LinkColor_ = Color.FromArgb(86, 186, 255);

        public AboutForm()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Windows-Hinting";
            var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = informationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";

            // Strip the commit hash suffix for display (e.g. "1.0.0+abc123" -> "1.0.0")
            var displayVersion = version.Contains('+') ? version[..version.IndexOf('+')] : version;
            var commitHash = version.Contains('+') ? version[(version.IndexOf('+') + 1)..] : null;

            Text = $"About {name}";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = BackColor_;
            ForeColor = ForeColorDefault;

            // Use em-based padding so margins scale with text size
            var emSize = (int)Math.Ceiling(Font.GetHeight());

            var contentPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(emSize * 2, emSize, emSize * 2, emSize),
                BackColor = BackColor_
            };

            var titleLabel = new Label
            {
                Text = name,
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = AccentColor,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, emSize / 4)
            };

            var isBeta = string.Equals(UpdateService.ChannelName, "Beta", StringComparison.OrdinalIgnoreCase);
            var versionText = isBeta
                ? $"Version {displayVersion} (Beta)"
                : $"Version {displayVersion}";

            var versionLabel = new Label
            {
                Text = versionText,
                Font = new Font("Segoe UI", 10f),
                ForeColor = isBeta ? AccentColor : ForeColorDefault,
                AutoSize = true,
                Margin = new Padding(2, 0, 0, emSize / 4)
            };

            var authorLabel = new Label
            {
                Text = $"Copyright © 2026 Jeff Knaus",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(140, 140, 140),
                AutoSize = true,
                Margin = new Padding(2, 0, 0, emSize)
            };

            var linkLabel = new LinkLabel
            {
                Text = GitHubUrl,
                Font = new Font("Segoe UI", 9f),
                LinkColor = LinkColor_,
                ActiveLinkColor = LinkColor_,
                VisitedLinkColor = LinkColor_,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, emSize)
            };
            linkLabel.LinkClicked += (_, _) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = GitHubUrl,
                    UseShellExecute = true
                });
            };

            var updateLink = new LinkLabel
            {
                Text = "Check for updates",
                Font = new Font("Segoe UI", 9f),
                LinkColor = LinkColor_,
                ActiveLinkColor = LinkColor_,
                VisitedLinkColor = LinkColor_,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, emSize)
            };
            updateLink.LinkClicked += (_, _) => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

            // Right-align the OK button inside a panel that stretches to match the content width
            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                BackColor = BackColor_
            };

            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 55, 55),
                ForeColor = ForeColorDefault,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(emSize, emSize / 4, emSize, emSize / 4)
            };
            okButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            buttonPanel.Controls.Add(okButton);

            AcceptButton = okButton;
            CancelButton = okButton;

            contentPanel.Controls.Add(titleLabel);
            contentPanel.Controls.Add(versionLabel);
            contentPanel.Controls.Add(authorLabel);
            contentPanel.Controls.Add(linkLabel);
            contentPanel.Controls.Add(updateLink);
            contentPanel.Controls.Add(buttonPanel);

            Controls.Add(contentPanel);

            // After layout, make the button panel match the widest content row
            // so the OK button right-aligns against the dialog edge
            Load += (_, _) =>
            {
                buttonPanel.Width = contentPanel.DisplayRectangle.Width
                    - contentPanel.Padding.Horizontal;
            };
        }
    }
}
