using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace HintOverlay.Services
{
    internal enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    internal static class Logger
    {
        private static LogLevel _minLevel = LogLevel.Debug;
        private static readonly object _lock = new object();

        public static LogLevel MinimumLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        public static void Debug(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.Debug, message, memberName, filePath);
        }

        public static void Info(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.Info, message, memberName, filePath);
        }

        public static void Warning(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.Warning, message, memberName, filePath);
        }

        public static void Error(string message, Exception? ex = null, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            var fullMessage = ex != null ? $"{message} - Exception: {ex.Message}\n{ex.StackTrace}" : message;
            Log(LogLevel.Error, fullMessage, memberName, filePath);
        }

        private static void Log(LogLevel level, string message, string memberName, string filePath)
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