using System;
using Microsoft.Win32;
using WindowsHinting.Logging;

namespace WindowsHinting.Services
{
    internal sealed class StartupService
    {
        private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Windows-Hinting";
        private readonly ILogger _logger;

        public StartupService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool IsEnabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
                    return key?.GetValue(AppName) != null;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to read startup registry: {ex.Message}");
                    return false;
                }
            }
        }

        public void Apply(bool startWithWindows)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
                if (key == null)
                {
                    _logger.Warning("Failed to open Run registry key for writing");
                    return;
                }

                if (startWithWindows)
                {
                    string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory + AppName + ".exe";
                    key.SetValue(AppName, $"\"{exePath}\"");
                    _logger.Info($"Startup registry entry added: {exePath}");
                }
                else
                {
                    key.DeleteValue(AppName);

                    _logger.Info("Startup registry entry removed");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to update startup registry: {ex.Message}", ex);
            }
        }
    }
}
