using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HintOverlay.NamedPipeClient
{
    /// <summary>
    /// Client for communicating with HintOverlay via named pipes.
    /// This client handles connection retries to support order-independent execution.
    /// </summary>
    public sealed class HintOverlayClient : IDisposable
    {
        private const string PipeName = "HintOverlay_Pipe";
        private const int ConnectionTimeoutMs = 5000;
        private const int RetryDelayMs = 100;
        private const int MaxRetries = 50;
        private bool _disposed;

        /// <summary>
        /// Toggles the hint overlay on or off.
        /// </summary>
        /// <returns>True if the command was sent successfully, false otherwise.</returns>
        public bool Toggle()
        {
            return SendCommand("TOGGLE");
        }

        /// <summary>
        /// Selects a hint by its label and activates the associated element using the default action.
        /// </summary>
        /// <param name="hintLabel">The label of the hint to select (e.g., "A", "B", "AB", etc.)</param>
        /// <returns>True if the command was sent successfully, false otherwise.</returns>
        public bool SelectHint(string hintLabel)
        {
            if (string.IsNullOrWhiteSpace(hintLabel))
                throw new ArgumentException("Hint label cannot be null or empty", nameof(hintLabel));

            return SendCommand($"SELECT {hintLabel}");
        }

        /// <summary>
        /// Selects a hint by its label and performs the specified click action.
        /// </summary>
        /// <param name="hintLabel">The label of the hint to select (e.g., "A", "B", "AB", etc.)</param>
        /// <param name="action">The click action to perform: "LEFT", "RIGHT", "DOUBLE", or null/empty for default activation.</param>
        /// <returns>True if the command was sent successfully, false otherwise.</returns>
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
        /// <returns>True if the command was sent successfully, false otherwise.</returns>
        public bool Deactivate()
        {
            return SendCommand("DEACTIVATE");
        }

        /// <summary>
        /// Toggles the taskbar-only hint mode on or off.
        /// </summary>
        /// <returns>True if the command was sent successfully, false otherwise.</returns>
        public bool ToggleTaskbar()
        {
            return SendCommand("TOGGLETASKBAR");
        }

        private bool SendCommand(string command)
        {
            return SendCommandAsync(command).GetAwaiter().GetResult();
        }

        private async Task<bool> SendCommandAsync(string command)
        {
            int retryCount = 0;

            while (retryCount < MaxRetries)
            {
                try
                {
                    using (var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                    {
                        pipeClient.Connect(ConnectionTimeoutMs);

                        using (var writer = new StreamWriter(pipeClient, Encoding.UTF8))
                        {
                            await writer.WriteLineAsync(command);
                            await writer.FlushAsync();
                        }

                        return true;
                    }
                }
                catch (TimeoutException)
                {
                    // Server not ready yet, retry
                    retryCount++;
                    if (retryCount < MaxRetries)
                    {
                        await Task.Delay(RetryDelayMs);
                    }
                }
                catch (Exception)
                {
                    // Other errors, stop retrying
                    return false;
                }
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
        }
    }
}
