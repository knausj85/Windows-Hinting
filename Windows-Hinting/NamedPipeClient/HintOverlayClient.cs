using System;
using System.IO;
using System.Threading;

namespace WindowsHinting.NamedPipeClient
{
    /// <summary>
    /// Client for communicating with Windows-Hinting via command files.
    /// Writes a command to a file in the user's %APPDATA%\Windows-Hinting\commands directory.
    /// The server watches this directory and processes each file as it appears.
    ///
    /// Usage from Talon (Python):
    ///   from pathlib import Path
    ///   cmd_dir = Path.home() / "AppData" / "Roaming" / "Windows-Hinting" / "commands"
    ///   (cmd_dir / "cmd").write_text("TOGGLE")
    ///
    /// Usage from C#:
    ///   new HintOverlayClient().Toggle();
    /// </summary>
    public sealed class HintOverlayClient : IDisposable
    {
        private static readonly string CommandDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Windows-Hinting",
            "commands");

        private bool _disposed;

        /// <summary>
        /// Toggles the hint overlay on or off.
        /// </summary>
        public bool Toggle() => SendCommand("TOGGLE");

        /// <summary>
        /// Selects a hint by its label using the default action.
        /// </summary>
        public bool SelectHint(string hintLabel)
        {
            if (string.IsNullOrWhiteSpace(hintLabel))
                throw new ArgumentException("Hint label cannot be null or empty", nameof(hintLabel));

            return SendCommand($"SELECT {hintLabel}");
        }

        /// <summary>
        /// Selects a hint by its label and performs the specified click action.
        /// </summary>
        /// <param name="hintLabel">The label of the hint to select (e.g., "A", "B", "AB")</param>
        /// <param name="action">The click action: "LEFT", "RIGHT", "DOUBLE", "MOVE", "CTRL", "SHIFT", or null for default.</param>
        public bool SelectHint(string hintLabel, string? action)
        {
            if (string.IsNullOrWhiteSpace(hintLabel))
                throw new ArgumentException("Hint label cannot be null or empty", nameof(hintLabel));

            if (string.IsNullOrWhiteSpace(action))
                return SendCommand($"SELECT {hintLabel}");

            return SendCommand($"SELECT {hintLabel} {action}");
        }

        /// <summary>
        /// Deactivates the hint overlay.
        /// </summary>
        public bool Deactivate() => SendCommand("DEACTIVATE");

        /// <summary>
        /// Toggles the taskbar-only hint mode.
        /// </summary>
        public bool ToggleTaskbar() => SendCommand("TOGGLETASKBAR");

        private bool SendCommand(string command)
        {
            try
            {
                if (!Directory.Exists(CommandDirectory))
                    Directory.CreateDirectory(CommandDirectory);

                // Use a unique filename to avoid collisions
                var filePath = Path.Combine(CommandDirectory,
                    $"cmd_{Environment.TickCount64}_{Thread.CurrentThread.ManagedThreadId}");

                File.WriteAllText(filePath, command);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
        }
    }
}
