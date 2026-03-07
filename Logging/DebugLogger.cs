using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace HintOverlay.Logging
{
    internal sealed class DebugLogger : ILogger
    {
        private LogLevel _minLevel = LogLevel.Debug;
        private readonly object _lock = new();

        public LogLevel MinimumLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

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
            }
        }
    }
}