using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace HintOverlay.Logging
{
    internal sealed class DebugLogger : ILogger, IDisposable
    {
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Windows-Hinting",
            "logs");

        private LogLevel _minLevel = LogLevel.Debug;
        private readonly object _lock = new();
        private StreamWriter? _fileWriter;
        private bool _fileLoggingEnabled;

        /// <summary>
        /// Raised after every log message is written, regardless of output target.
        /// The event argument is the fully formatted log line.
        /// </summary>
        public event EventHandler<LogMessageEventArgs>? LogMessageWritten;

        public LogLevel MinimumLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        public bool FileLoggingEnabled
        {
            get => _fileLoggingEnabled;
            set
            {
                if (_fileLoggingEnabled == value)
                    return;

                _fileLoggingEnabled = value;
                if (value)
                    OpenLogFile();
                else
                    CloseLogFile();
            }
        }

        /// <summary>
        /// Returns the path to the current log file, or null if file logging is not active.
        /// </summary>
        public string? CurrentLogFilePath { get; private set; }

        /// <summary>
        /// Returns the directory where log files are stored.
        /// </summary>
        public static string LogDirectoryPath => LogDirectory;

        public void Debug(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.Debug, message, memberName, filePath);
        }

        public void Info(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.Info, message, memberName, filePath);
        }

        public void Warning(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.Warning, message, memberName, filePath);
        }

        public void Error(string message, Exception? ex = null, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            var fullMessage = ex != null ? $"{message} - Exception: {ex.Message}\n{ex.StackTrace}" : message;
            Log(LogLevel.Error, fullMessage, memberName, filePath);
        }

        private void Log(LogLevel level, string message, string memberName, string filePath)
        {
            if (level < _minLevel)
                return;

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] [{level}] [{fileName}.{memberName}] {message}";

            lock (_lock)
            {
                System.Diagnostics.Debug.WriteLine(logMessage);

                if (_fileLoggingEnabled && _fileWriter != null)
                {
                    try
                    {
                        _fileWriter.WriteLine(logMessage);
                        _fileWriter.Flush();
                    }
                    catch
                    {
                        // Don't let logging failures crash the app
                    }
                }
            }

            LogMessageWritten?.Invoke(this, new LogMessageEventArgs(level, logMessage));
        }

        private void OpenLogFile()
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(LogDirectory);
                    var logFileName = $"Windows-Hinting_{DateTime.Now:yyyy-MM-dd_HHmmss}.log";
                    CurrentLogFilePath = Path.Combine(LogDirectory, logFileName);
                    _fileWriter = new StreamWriter(CurrentLogFilePath, append: true) { AutoFlush = true };
                }
                catch
                {
                    _fileWriter = null;
                    CurrentLogFilePath = null;
                }
            }
        }

        private void CloseLogFile()
        {
            lock (_lock)
            {
                _fileWriter?.Dispose();
                _fileWriter = null;
            }
        }

        public void Dispose()
        {
            CloseLogFile();
        }
    }
}