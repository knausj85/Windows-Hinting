using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindowsHinting.Logging;
using WindowsHinting.Models;

namespace WindowsHinting.Services
{
    internal sealed class NamedPipeService : IDisposable
    {
        private const string PipeName = "WindowsHinting_Pipe";
        private const int MaxConnections = 10;
        private readonly ILogger _logger;
        private NamedPipeServerStream? _pipeServer;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _listenerTask;
        private bool _disposed;

        public event EventHandler<NamedPipeCommand>? CommandReceived;

        public NamedPipeService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Start()
        {
            if (_cancellationTokenSource != null)
            {
                _logger.Warning("Named pipe service already started");
                return;
            }

            _logger.Info($"Starting named pipe server: {PipeName}");
            _cancellationTokenSource = new CancellationTokenSource();
            _listenerTask = ListenForConnectionsAsync(_cancellationTokenSource.Token);
        }

        public void Stop()
        {
            _logger.Info("Stopping named pipe service");
            _cancellationTokenSource?.Cancel();
            try
            {
                _listenerTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            _pipeServer?.Dispose();
            _pipeServer = null;
        }

        private async Task ListenForConnectionsAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    _pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        MaxConnections,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    try
                    {
                        _logger.Debug("Waiting for named pipe connection...");
                        await _pipeServer.WaitForConnectionAsync(cancellationToken);
                        _logger.Debug("Named pipe client connected");

                        // Handle the client in a separate task
                        _ = HandleClientAsync(_pipeServer, cancellationToken);

                        // Create a new server for the next connection
                        _pipeServer = null;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error in named pipe listener: {ex.Message}", ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Named pipe listener cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fatal error in named pipe listener: {ex.Message}", ex);
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
        {
            try
            {
                using (var reader = new StreamReader(pipeServer, Encoding.UTF8))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        _logger.Debug($"Received named pipe command: {line}");

                        var command = ParseCommand(line);
                        if (command != null)
                        {
                            CommandReceived?.Invoke(this, command);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Client handler cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error handling named pipe client: {ex.Message}", ex);
            }
            finally
            {
                try
                {
                    pipeServer.Close();
                }
                catch { }
            }
        }

        private NamedPipeCommand? ParseCommand(string line)
        {
            try
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return null;

                string command = parts[0].ToUpperInvariant();

                return command switch
                {
                    "TOGGLE" => new NamedPipeCommand { CommandType = CommandType.Toggle },
                    "TOGGLETASKBAR" => new NamedPipeCommand { CommandType = CommandType.ToggleTaskbar },
                    "SELECT" => parts.Length > 1 ? new NamedPipeCommand
                    {
                        CommandType = CommandType.Select,
                        HintLabel = parts[1],
                        Action = ParseClickAction(parts.Length > 2 ? parts[2] : null)
                    } : null,
                    "DEACTIVATE" => new NamedPipeCommand { CommandType = CommandType.Deactivate },
                    _ => null
                };
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error parsing named pipe command '{line}': {ex.Message}");
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

            _logger.Debug("Disposing NamedPipeService");
            Stop();
            _cancellationTokenSource?.Dispose();
            _pipeServer?.Dispose();
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

    internal sealed class NamedPipeCommand
    {
        public CommandType CommandType { get; set; }
        public string? HintLabel { get; set; }
        public ClickAction Action { get; set; } = ClickAction.Default;
    }
}
