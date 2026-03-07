using HintOverlay.Logging;
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
        /// <param name="logger">Logger instance to use for output.</param>
        /// <param name="logLevel">Minimum log level required to log the result.</param>
        /// <returns>A disposable scope that measures execution time.</returns>
        public static PerformanceScope Start(string operationName, ILogger logger, LogLevel logLevel = LogLevel.Debug)
        {
            return new PerformanceScope(operationName, logger, logLevel);
        }
    }

    /// <summary>
    /// Represents a disposable scope for measuring operation performance.
    /// </summary>
    internal sealed class PerformanceScope : IDisposable
    {
        private readonly string _operationName;
        private readonly ILogger _logger;
        private readonly LogLevel _logLevel;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        internal PerformanceScope(string operationName, ILogger logger, LogLevel logLevel)
        {
            _operationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                    _logger.Debug(message);
                    break;
                case LogLevel.Info:
                    _logger.Info(message);
                    break;
                case LogLevel.Warning:
                    _logger.Warning(message);
                    break;
                case LogLevel.Error:
                    _logger.Error(message);
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
        /// <param name="logger">Logger instance to use for output.</param>
        /// <param name="logLevel">Minimum log level required to log the result.</param>
        /// <returns>The result of the function execution.</returns>
        public static T MeasureExecution<T>(string operationName, Func<T> func, ILogger logger, LogLevel logLevel = LogLevel.Debug)
        {
            using (PerformanceMetrics.Start(operationName, logger, logLevel))
            {
                return func();
            }
        }

        /// <summary>
        /// Measures the execution time of an action and logs the result.
        /// </summary>
        /// <param name="operationName">Name of the operation being measured.</param>
        /// <param name="action">Action to execute and measure.</param>
        /// <param name="logger">Logger instance to use for output.</param>
        /// <param name="logLevel">Minimum log level required to log the result.</param>
        public static void MeasureExecution(string operationName, Action action, ILogger logger, LogLevel logLevel = LogLevel.Debug)
        {
            using (PerformanceMetrics.Start(operationName, logger, logLevel))
            {
                action();
            }
        }
    }
}