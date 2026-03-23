using System;
using System.IO;
using System.Threading;
using WindowsHinting.Logging;
using WindowsHinting.Models;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Watches a user-scoped directory for command files written by an external
    /// client (e.g., Talon Voice).  The directory lives under %APPDATA% and
    /// inherits the user-profile NTFS ACL, so only the current user can write
    /// to it — no pipe name to discover, no ACL to configure, no signature
    /// verification required.
    ///
    /// Protocol:
    ///   1. Client writes a file whose content is the command text
    ///      (e.g., "TOGGLE", "SELECT AB LEFT").
    ///   2. This service detects the new file, reads it, deletes it,
    ///      and raises <see cref="CommandReceived"/>.
    ///
    /// The simplest Talon integration is a single Python line:
    ///   Path(command_dir / "cmd").write_text("TOGGLE")
    /// </summary>
    internal sealed class CommandFileService : IDisposable
    {
        private const int MaxCommandLength = 256;
        private static readonly string CommandDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Windows-Hinting",
            "commands");

        private readonly ILogger _logger;
        private FileSystemWatcher? _watcher;
        private bool _disposed;

        public event EventHandler<CommandFileCommand>? CommandReceived;

        /// <summary>
        /// Returns the directory path that external clients should write command
        /// files to.  Exposed so documentation / client helpers can reference it.
        /// </summary>
        public static string GetCommandDirectory() => CommandDirectory;

        public CommandFileService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Start()
        {
            if (_watcher != null)
            {
                _logger.Warning("Command file service already started");
                return;
            }

            if (!Directory.Exists(CommandDirectory))
            {
                Directory.CreateDirectory(CommandDirectory);
                _logger.Info($"Created command directory: {CommandDirectory}");
            }

            // Drain any stale files that were written while we were not running
            DrainExistingFiles();

            _watcher = new FileSystemWatcher(CommandDirectory)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileCreated;
            _watcher.Error += OnWatcherError;

            _logger.Info($"Watching for command files in: {CommandDirectory}");
        }

        public void Stop()
        {
            _logger.Info("Stopping command file service");
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileCreated;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
                _watcher = null;
            }
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            ProcessFile(e.FullPath);
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            _logger.Error($"FileSystemWatcher error: {e.GetException().Message}", e.GetException());

            // Attempt to restart the watcher
            try
            {
                Stop();
                Start();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to restart command file watcher: {ex.Message}", ex);
            }
        }

        private void DrainExistingFiles()
        {
            try
            {
                foreach (var file in Directory.GetFiles(CommandDirectory))
                {
                    ProcessFile(file);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error draining existing command files: {ex.Message}");
            }
        }

        private void ProcessFile(string filePath)
        {
            try
            {
                // Brief retry — the writer may not have finished flushing yet
                string? content = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        content = File.ReadAllText(filePath).Trim();
                        break;
                    }
                    catch (IOException) when (attempt < 2)
                    {
                        Thread.Sleep(10);
                    }
                }

                // Always delete the file, even if we couldn't read it
                TryDelete(filePath);

                if (string.IsNullOrWhiteSpace(content))
                    return;

                if (content.Length > MaxCommandLength)
                {
                    _logger.Warning($"Command file too long ({content.Length} chars), ignored");
                    return;
                }

                _logger.Debug($"Command file received: {content}");

                var command = ParseCommand(content);
                if (command != null)
                {
                    CommandReceived?.Invoke(this, command);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error processing command file '{filePath}': {ex.Message}");
                TryDelete(filePath);
            }
        }

        private static void TryDelete(string filePath)
        {
            try { File.Delete(filePath); } catch { }
        }

        private CommandFileCommand? ParseCommand(string line)
        {
            try
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return null;

                string command = parts[0].ToUpperInvariant();

                return command switch
                {
                    "TOGGLE" => new CommandFileCommand { CommandType = CommandType.Toggle },
                    "TOGGLETASKBAR" => new CommandFileCommand { CommandType = CommandType.ToggleTaskbar },
                    "SELECT" => parts.Length > 1 ? new CommandFileCommand
                    {
                        CommandType = CommandType.Select,
                        HintLabel = parts[1],
                        Action = ParseClickAction(parts.Length > 2 ? parts[2] : null)
                    } : null,
                    "DEACTIVATE" => new CommandFileCommand { CommandType = CommandType.Deactivate },
                    _ => null
                };
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error parsing command '{line}': {ex.Message}");
                return null;
            }
        }

        private static ClickAction ParseClickAction(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return ClickAction.Default;

            return value.ToUpperInvariant() switch
            {
                "LEFT" => ClickAction.LeftClick,
                "RIGHT" => ClickAction.RightClick,
                "DOUBLE" => ClickAction.DoubleClick,
                "MOVE" => ClickAction.MouseMove,
                "CTRL" => ClickAction.CtrlClick,
                "SHIFT" => ClickAction.ShiftClick,
                _ => ClickAction.Default
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _logger.Debug("Disposing CommandFileService");
            Stop();
            _disposed = true;
        }
    }

    internal enum CommandType
    {
        Toggle,
        ToggleTaskbar,
        Select,
        Deactivate
    }

    internal sealed class CommandFileCommand
    {
        public CommandType CommandType { get; set; }
        public string? HintLabel { get; set; }
        public ClickAction Action { get; set; } = ClickAction.Default;
    }
}
