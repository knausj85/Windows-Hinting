using System;
using System.Runtime.CompilerServices;

namespace WindowsHinting.Logging
{
    public interface ILogger
    {
        LogLevel MinimumLevel { get; set; }
        void Debug(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "");
        void Info(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "");
        void Warning(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "");
        void Error(string message, Exception? ex = null, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "");
    }
}