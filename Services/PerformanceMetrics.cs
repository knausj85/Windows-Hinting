using System;
using System.Diagnostics;

namespace HintOverlay.Services
{
    /// <summary>
    /// Provides performance monitoring and timing utilities for critical code sections.
    /// </summary>
    internal static class PerformanceMetrics
    {
        /// <summary>
        /// Starts a performance measurement scope that automatically logs the elapsed time when disposed.
        /// </summary>
        /// <param name="operationName">Name of the operation being measured.</param>
        /// <param name="logLevel">Minimum log level required to log the result.</param>
        /// <returns>A disposable scope that measures execution time.</returns>
        public static PerformanceScope Start(string operationName, LogLevel logLevel = LogLevel.Debug)
        {
            return new PerformanceScope(operationName, logLevel);
        }
    }

    /// <summary>
    /// Represents a disposable scope for measuring operation performance.
    /// </summary>
    internal sealed class PerformanceScope : IDisposable
    {
        private readonly string _operationName;
        private readonly LogLevel _logLevel;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        internal PerformanceScope(string operationName, LogLevel logLevel)
        {
            _operationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
            _logLevel = logLevel;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _stopwatch.Stop();

            var elapsed = _stopwatch.ElapsedMilliseconds;
            var message = $"{_operationName} completed in {elapsed}ms";

            switch (_logLevel)
            {
                case LogLevel.Debug:
                    Logger.Debug(message);
                    break;
                case LogLevel.Info:
                    Logger.Info(message);
                    break;
                case LogLevel.Warning:
                    Logger.Warning(message);
                    break;
                case LogLevel.Error:
                    Logger.Error(message);
                    break;
            }
        }
    }

    /// <summary>
    /// Extension methods for performance metrics.
    /// </summary>
    internal static class PerformanceMetricsExtensions
    {
        /// <summary>
        /// Measures the execution time of a function and logs the result.
        /// </summary>
        /// <typeparam name="T">Return type of the function.</typeparam>
        /// <param name="operationName">Name of the operation being measured.</param>
        /// <param name="func">Function to execute and measure.</param>
        /// <param name="logLevel">Minimum log level required to log the result.</param>
        /// <returns>The result of the function execution.</returns>
        public static T MeasureExecution<T>(string operationName, Func<T> func, LogLevel logLevel = LogLevel.Debug)
        {
            using (PerformanceMetrics.Start(operationName, logLevel))
            {
                return func();
            }
        }

        /// <summary>
        /// Measures the execution time of an action and logs the result.
        /// </summary>
        /// <param name="operationName">Name of the operation being measured.</param>
        /// <param name="action">Action to execute and measure.</param>
        /// <param name="logLevel">Minimum log level required to log the result.</param>
        public static void MeasureExecution(string operationName, Action action, LogLevel logLevel = LogLevel.Debug)
        {
            using (PerformanceMetrics.Start(operationName, logLevel))
            {
                action();
            }
        }
    }
}