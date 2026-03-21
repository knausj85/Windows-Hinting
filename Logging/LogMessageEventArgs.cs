using System;

namespace HintOverlay.Logging
{
    internal sealed class LogMessageEventArgs : EventArgs
    {
        public LogLevel Level { get; }
        public string Message { get; }

        public LogMessageEventArgs(LogLevel level, string message)
        {
            Level = level;
            Message = message;
        }
    }
}
