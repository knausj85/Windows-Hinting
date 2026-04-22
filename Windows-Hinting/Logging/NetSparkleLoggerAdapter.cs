using System;
using NetSparkleUpdater.Interfaces;

namespace WindowsHinting.Logging
{
    /// <summary>
    /// Adapter that routes NetSparkle's diagnostic messages through our application's
    /// <see cref="ILogger"/> with a configurable minimum level and "[NetSparkle] " prefix.
    /// </summary>
    internal sealed class NetSparkleLoggerAdapter : NetSparkleUpdater.Interfaces.ILogger
    {
        private readonly ILogger _logger;
        private readonly bool _enabled;

        public NetSparkleLoggerAdapter(ILogger logger, bool enabled)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _enabled = enabled;
        }

        /// <summary>
        /// Called by NetSparkle to emit diagnostic messages. Routes to ILogger at the
        /// configured <see cref="_minimumLevel"/> with a "[NetSparkle] " prefix.
        /// </summary>
        public void PrintMessage(string message, params object[]? arguments)
        {
            if (!_enabled)
                return;

            try
            {
                // NetSparkle sometimes passes messages with { } that aren't format strings;
                // only call string.Format if we have arguments.
                var formatted = (arguments != null && arguments.Length > 0)
                    ? string.Format(message, arguments)
                    : message;

                var prefixed = $"[NetSparkle] {formatted}";

                // Route to ILogger at the configured level
                _logger.Info(prefixed);
            }
            catch
            {
                // Swallow any formatting exceptions to avoid crashing NetSparkle's internal flows
            }
        }
    }
}
