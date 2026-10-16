using System;
using System.IO;
using Microsoft.Win32;
using WindowsHinting.Logging;
using WindowsHinting.Models;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Owns the "Start with Windows" setting: a per-user value under
    /// <c>HKCU\...\CurrentVersion\Run</c>. The installer no longer writes a Run entry
    /// of its own, so this is the only one and the Preferences checkbox reflects and
    /// controls what actually happens at sign-in.
    /// </summary>
    internal sealed class StartupService
    {
        private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        // Where Task Manager's Startup tab and Settings > Apps > Startup record that the
        // user disabled a Run entry (the entry itself stays in place). The value is a
        // 12-byte blob; an odd first byte (0x03, 0x07) means disabled.
        private const string StartupApprovedKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        private const string AppName = "Windows-Hinting";
        private readonly ILogger _logger;

        public StartupService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// True when the app will start at sign-in: the Run value exists and hasn't been
        /// disabled from Task Manager or Settings.
        /// </summary>
        public bool IsEnabled
        {
            get
            {
                try
                {
                    return ReadRunCommand() != null && !IsDisabledByUser();
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to read startup registry: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Called once at startup. Enables start-at-sign-in the first time an installed
        /// (MSI) build runs for this user, which keeps the behavior of earlier installers
        /// that registered a machine-wide Run entry, and repoints an existing entry whose
        /// exe no longer exists (e.g. a portable exe that was moved). Returns true when
        /// <paramref name="options"/> changed and should be saved.
        /// </summary>
        public bool EnsureInitialized(HintOverlayOptions options)
        {
            bool optionsChanged = false;

            try
            {
                if (!options.StartupDefaultApplied)
                {
                    if (DeploymentMode.Current == DeploymentKind.Installer && ReadRunCommand() == null)
                    {
                        _logger.Info("First run of the installed app: enabling start with Windows (default)");
                        Apply(true);
                    }

                    options.StartupDefaultApplied = true;
                    optionsChanged = true;
                }

                RepairStalePath();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to initialize startup registration: {ex.Message}");
            }

            return optionsChanged;
        }

        public void Apply(bool startWithWindows)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryRunKey, writable: true);
                if (key == null)
                {
                    _logger.Warning("Failed to open Run registry key for writing");
                    return;
                }

                if (startWithWindows)
                {
                    string exePath = CurrentExePath();
                    key.SetValue(AppName, $"\"{exePath}\"");
                    // Re-enabling from our checkbox should win over an earlier "disable"
                    // in Task Manager, so drop that override.
                    ClearUserOverride();
                    _logger.Info($"Startup registry entry set: {exePath}");
                }
                else
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                    ClearUserOverride();
                    _logger.Info("Startup registry entry removed");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to update startup registry: {ex.Message}", ex);
            }
        }

        private void RepairStalePath()
        {
            var command = ReadRunCommand();
            if (command == null)
                return;

            var registeredPath = command.Trim().Trim('"');
            if (File.Exists(registeredPath))
                return;

            // Only repoint when this process is a real deployment; a dev build running
            // from bin\ shouldn't take over the user's startup entry.
            if (DeploymentMode.Current == DeploymentKind.Unknown)
                return;

            _logger.Info($"Startup entry points at a missing exe ({registeredPath}); updating to this one");
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, writable: true);
            key?.SetValue(AppName, $"\"{CurrentExePath()}\"");
        }

        private static string? ReadRunCommand()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, writable: false);
            return key?.GetValue(AppName) as string;
        }

        private static bool IsDisabledByUser()
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: false);
            return key?.GetValue(AppName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
        }

        private static void ClearUserOverride()
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }

        private static string CurrentExePath() =>
            Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppName + ".exe");
    }
}
