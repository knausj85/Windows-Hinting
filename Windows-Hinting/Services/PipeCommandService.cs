using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using WindowsHinting.Logging;
using WindowsHinting.Models;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Talon-facing RPC endpoint: one JSON request line in, one JSON response line out,
    /// over a <c>CurrentUserOnly</c> named pipe. See docs/design/named-pipe-rpc.md.
    /// Parsing, validation and caller identification happen here on the pipe thread;
    /// the handler passed to <see cref="Start"/> executes the command (and applies the
    /// elevated-target policy) on the UI thread.
    /// </summary>
    internal sealed class PipeCommandService : IDisposable
    {
        public const int ProtocolVersion = 1;
        private const int MaxRequestBytes = 64 * 1024;
        private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ClientCloseTimeout = TimeSpan.FromSeconds(2);
        private const long StateChangeIntervalMs = 100;

        private static readonly JsonSerializerOptions RequestJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        private static readonly JsonSerializerOptions ResponseJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        private readonly ILogger _logger;
        private NamedPipeServerStream? _server;
        private CancellationTokenSource? _cts;
        private Task? _acceptLoop;
        private Func<PipeCommand, PipeCaller, Task<PipeResponse>>? _handler;
        private long _lastStateChangeTicks;
        private bool _disposed;

        public PipeCommandService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary><c>\\.\pipe\WindowsHinting-&lt;user SID&gt;</c>, without the <c>\\.\pipe\</c> prefix.</summary>
        public static string PipeName { get; } =
            "WindowsHinting-" + (WindowsIdentity.GetCurrent().User?.Value ?? "unknown");

        public void Start(Func<PipeCommand, PipeCaller, Task<PipeResponse>> handler)
        {
            if (_cts != null)
                return;

            try
            {
                // A single server instance that is reused (Disconnect + WaitForConnection)
                // for every client, so the name is never released for a squatter to take.
                _server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Error($"RPC pipe '{PipeName}' is already in use; running without RPC", ex);
                return;
            }

            _handler = handler;
            _cts = new CancellationTokenSource();
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_server, _cts.Token));
            _logger.Info($"RPC pipe listening: \\\\.\\pipe\\{PipeName}");
        }

        private async Task AcceptLoopAsync(NamedPipeServerStream server, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error("RPC pipe accept failed; stopping RPC", ex);
                    break;
                }

                try
                {
                    await HandleConnectionAsync(server, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error("RPC connection failed", ex);
                }
                finally
                {
                    try
                    {
                        server.Disconnect();
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
                    {
                        // Client already gone, or shutting down.
                    }
                }
            }
        }

        private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
        {
            using var caller = PipeCaller.Identify(server.SafePipeHandle);
            if (caller.Pid == 0)
            {
                _logger.Warning("RPC connection rejected: could not identify the client process");
                await WriteResponseAsync(server, null,
                    PipeResponse.Fail(PipeResultCode.UnauthorizedCaller, "Could not identify the calling process"), ct)
                    .ConfigureAwait(false);
                return;
            }

            string? id = null;
            PipeResponse response;
            string commandName = "(none)";

            var (line, readError) = await ReadRequestLineAsync(server, ct).ConfigureAwait(false);
            if (line == null)
            {
                response = readError!;
            }
            else
            {
                var (command, parsedId, parseError) = ParseRequest(line);
                id = parsedId;
                if (command == null)
                {
                    response = parseError!;
                }
                else
                {
                    commandName = command.Name;
                    response = await DispatchAsync(command, caller, ct).ConfigureAwait(false);
                }
            }

            var summary = $"RPC {commandName} from pid={caller.Pid} image={caller.ImagePath ?? "?"} " +
                          $"integrity={ProcessSecurity.DescribeIntegrity(caller.IntegrityRid)} uiAccess={caller.UIAccess} " +
                          $"-> {response.Code}{(string.IsNullOrEmpty(response.Message) ? "" : ": " + response.Message)}";
            if (response.Ok)
                _logger.Info(summary);
            else
                _logger.Warning(summary);

            await WriteResponseAsync(server, id, response, ct).ConfigureAwait(false);
            await WaitForClientCloseAsync(server, ct).ConfigureAwait(false);
        }

        private async Task<PipeResponse> DispatchAsync(PipeCommand command, PipeCaller caller, CancellationToken ct)
        {
            if (command.Kind == PipeCommandKind.Ping)
                return PipeResponse.Success();

            if (command.IsStateChanging)
            {
                // At most one state-changing command per StateChangeIntervalMs. The accept
                // loop is serial, so delaying here throttles every caller.
                long elapsedMs = (Stopwatch.GetTimestamp() - _lastStateChangeTicks) * 1000 / Stopwatch.Frequency;
                if (elapsedMs < StateChangeIntervalMs)
                    await Task.Delay(TimeSpan.FromMilliseconds(StateChangeIntervalMs - elapsedMs), ct).ConfigureAwait(false);
                _lastStateChangeTicks = Stopwatch.GetTimestamp();
            }

            var handler = _handler;
            if (handler == null)
                return PipeResponse.Fail(PipeResultCode.Internal, "No command handler");

            try
            {
                return await handler(command, caller).WaitAsync(DispatchTimeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return PipeResponse.Fail(PipeResultCode.Internal, $"Command did not complete within {DispatchTimeout.TotalSeconds:0}s");
            }
        }

        /// <summary>
        /// Reads bytes up to the first '\n' (or EOF), within <see cref="ReadDeadline"/> and
        /// <see cref="MaxRequestBytes"/>.
        /// </summary>
        private static async Task<(byte[]? Line, PipeResponse? Error)> ReadRequestLineAsync(
            NamedPipeServerStream server, CancellationToken ct)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(ReadDeadline);

            var buffer = new byte[4096];
            using var line = new MemoryStream();
            bool tooLong = false;
            try
            {
                while (true)
                {
                    int read = await server.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
                    if (read == 0)
                        break;

                    int newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
                    // Past the limit, keep reading but discard: a client blocked writing the
                    // rest of its request can't read the error, and the response would be
                    // cancelled by the write timeout.
                    if (!tooLong)
                    {
                        line.Write(buffer, 0, newline >= 0 ? newline : read);
                        tooLong = line.Length > MaxRequestBytes;
                    }
                    if (newline >= 0)
                        break;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (!tooLong)
                    return (null, PipeResponse.Fail(PipeResultCode.InvalidInput, $"No request line within {ReadDeadline.TotalSeconds:0}s"));
            }
            catch (IOException)
            {
                return (null, PipeResponse.Fail(PipeResultCode.InvalidInput, "Client disconnected before sending a request"));
            }

            if (tooLong)
                return (null, PipeResponse.Fail(PipeResultCode.InvalidInput, $"Request exceeds {MaxRequestBytes} bytes"));
            if (line.Length == 0)
                return (null, PipeResponse.Fail(PipeResultCode.InvalidInput, "Empty request"));
            return (line.ToArray(), null);
        }

        internal static (PipeCommand? Command, string? Id, PipeResponse? Error) ParseRequest(byte[] line)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                return (null, null, PipeResponse.Fail(PipeResultCode.InvalidInput, $"Malformed JSON: {ex.Message}"));
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return (null, null, PipeResponse.Fail(PipeResultCode.InvalidInput, "Request must be a JSON object"));

                // Pull id and protocol out first so errors can echo the id, and a newer
                // client gets ERR_VERSION_MISMATCH rather than an unknown-field error.
                string? id = doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString()
                    : null;

                if (!doc.RootElement.TryGetProperty("protocol", out var protocolProp) ||
                    !protocolProp.TryGetInt32(out int protocol) || protocol < 1)
                    return (null, id, PipeResponse.Fail(PipeResultCode.InvalidInput, "Missing or invalid 'protocol'"));
                if (protocol > ProtocolVersion)
                    return (null, id, PipeResponse.Fail(PipeResultCode.VersionMismatch,
                        $"Protocol {protocol} is not supported; server speaks {ProtocolVersion}"));

                PipeRequest? request;
                try
                {
                    request = doc.RootElement.Deserialize<PipeRequest>(RequestJsonOptions);
                }
                catch (JsonException ex)
                {
                    return (null, id, PipeResponse.Fail(PipeResultCode.InvalidInput, ex.Message));
                }

                if (request == null)
                    return (null, id, PipeResponse.Fail(PipeResultCode.InvalidInput, "Empty request"));

                var (command, error) = PipeCommand.FromRequest(request);
                return (command, id, error);
            }
        }

        private static async Task WriteResponseAsync(NamedPipeServerStream server, string? id, PipeResponse response, CancellationToken ct)
        {
            var envelope = new PipeResponseEnvelope
            {
                Protocol = ProtocolVersion,
                Id = id,
                Ok = response.Ok,
                Code = response.Code,
                Message = response.Message,
                Data = response.Data,
            };
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(envelope, ResponseJsonOptions);

            // Bounded so a client that never reads (e.g. still blocked writing an
            // oversized request) can't wedge the serial accept loop.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(WriteTimeout);
            try
            {
                await server.WriteAsync(json, timeout.Token).ConfigureAwait(false);
                await server.WriteAsync("\n"u8.ToArray(), timeout.Token).ConfigureAwait(false);
                await server.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Client isn't reading; drop the response.
            }
            catch (IOException)
            {
                // Client went away before reading the response.
            }
        }

        /// <summary>
        /// DisconnectNamedPipe discards data the client has not read yet, so wait for the
        /// client to close its end (read returns 0) before the accept loop disconnects.
        /// </summary>
        private static async Task WaitForClientCloseAsync(NamedPipeServerStream server, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ClientCloseTimeout);
            var buffer = new byte[256];
            try
            {
                while (await server.ReadAsync(buffer, timeout.Token).ConfigureAwait(false) > 0)
                {
                    // One request per connection; ignore anything else the client sends.
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _cts?.Cancel();
            try
            {
                _acceptLoop?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }
            _server?.Dispose();
            _cts?.Dispose();
        }
    }

    internal enum PipeCommandKind
    {
        Ping,
        Status,
        Toggle,
        ToggleTaskbar,
        Select,
        Deactivate,
    }

    internal sealed class PipeRequest
    {
        [JsonRequired]
        public int Protocol { get; init; }

        [JsonRequired]
        public string Command { get; init; } = "";

        public string[]? Args { get; init; }

        public string? Id { get; init; }
    }

    internal sealed record PipeCommand(PipeCommandKind Kind, string Name, string? Label = null, ClickAction Action = ClickAction.Default)
    {
        private const int MaxLabelLength = 8;

        public bool IsStateChanging => Kind is PipeCommandKind.Toggle or PipeCommandKind.ToggleTaskbar
            or PipeCommandKind.Select or PipeCommandKind.Deactivate;

        public static (PipeCommand? Command, PipeResponse? Error) FromRequest(PipeRequest request)
        {
            var args = request.Args ?? Array.Empty<string>();
            var name = request.Command;

            PipeCommandKind? kind = name switch
            {
                "ping" => PipeCommandKind.Ping,
                "status" => PipeCommandKind.Status,
                "toggle" => PipeCommandKind.Toggle,
                "toggle_taskbar" => PipeCommandKind.ToggleTaskbar,
                "select" => PipeCommandKind.Select,
                "deactivate" => PipeCommandKind.Deactivate,
                _ => null,
            };

            if (kind == null)
                return (null, PipeResponse.Fail(PipeResultCode.UnknownCommand, $"Unknown command '{Truncate(name)}'"));

            if (kind != PipeCommandKind.Select)
            {
                return args.Length == 0
                    ? (new PipeCommand(kind.Value, name), null)
                    : (null, PipeResponse.Fail(PipeResultCode.InvalidInput, $"'{name}' takes no arguments"));
            }

            if (args.Length is < 1 or > 2)
                return (null, PipeResponse.Fail(PipeResultCode.InvalidInput, "'select' takes a label and an optional action"));

            var label = args[0] ?? "";
            if (label.Length is 0 or > MaxLabelLength || !label.All(char.IsAsciiLetter))
                return (null, PipeResponse.Fail(PipeResultCode.InvalidInput, $"Invalid label '{Truncate(label)}'"));

            ClickAction? action = (args.Length > 1 ? args[1] : "") switch
            {
                null or "" => ClickAction.Default,
                "left" => ClickAction.LeftClick,
                "right" => ClickAction.RightClick,
                "double" => ClickAction.DoubleClick,
                "move" => ClickAction.MouseMove,
                "ctrl" => ClickAction.CtrlClick,
                "shift" => ClickAction.ShiftClick,
                _ => null,
            };
            if (action == null)
                return (null, PipeResponse.Fail(PipeResultCode.InvalidInput, $"Invalid action '{Truncate(args[1])}'"));

            return (new PipeCommand(PipeCommandKind.Select, name, label.ToUpperInvariant(), action.Value), null);
        }

        private static string Truncate(string? s) => s == null ? "" : s.Length <= 32 ? s : s[..32] + "...";
    }

    internal static class PipeResultCode
    {
        public const string Ok = "OK";
        public const string UnknownCommand = "ERR_UNKNOWN_COMMAND";
        public const string InvalidInput = "ERR_INVALID_INPUT";
        public const string NotActive = "ERR_NOT_ACTIVE";
        public const string LabelNotFound = "ERR_LABEL_NOT_FOUND";
        public const string ElevatedTarget = "ERR_ELEVATED_TARGET";
        public const string UnauthorizedCaller = "ERR_UNAUTHORIZED_CALLER";
        public const string VersionMismatch = "ERR_VERSION_MISMATCH";
        public const string Internal = "ERR_INTERNAL";
    }

    internal sealed record PipeResponse(bool Ok, string Code, string Message, object? Data)
    {
        public static PipeResponse Success(string message = "", object? data = null) => new(true, PipeResultCode.Ok, message, data);

        public static PipeResponse Fail(string code, string message) => new(false, code, message, null);
    }

    internal sealed class PipeResponseEnvelope
    {
        public int Protocol { get; init; }
        public string? Id { get; init; }
        public bool Ok { get; init; }
        public string Code { get; init; } = "";
        public string Message { get; init; } = "";

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Data { get; init; }
    }

    /// <summary>
    /// The connected client's identity, captured before the request is read. Holds the
    /// client's process handle for the connection's lifetime so the PID can't be recycled
    /// underneath the checks.
    /// </summary>
    internal sealed class PipeCaller : IDisposable
    {
        private readonly SafeFileHandle? _process;

        private PipeCaller(uint pid, SafeFileHandle? process, string? imagePath, int? integrityRid, bool uiAccess)
        {
            Pid = pid;
            _process = process;
            ImagePath = imagePath;
            IntegrityRid = integrityRid;
            UIAccess = uiAccess;
        }

        /// <summary>0 when the client process could not be identified.</summary>
        public uint Pid { get; }
        public string? ImagePath { get; }
        public int? IntegrityRid { get; }
        public bool UIAccess { get; }

        public static PipeCaller Identify(SafePipeHandle pipe)
        {
            if (!PInvoke.GetNamedPipeClientProcessId(pipe, out uint pid) || pid == 0)
                return new PipeCaller(0, null, null, null, false);

            var process = ProcessSecurity.OpenForQuery(pid);
            if (process == null)
                return new PipeCaller(pid, null, null, null, false);

            var (integrity, uiAccess) = ProcessSecurity.QueryToken(process);
            return new PipeCaller(pid, process, ProcessSecurity.GetImagePath(process), integrity, uiAccess);
        }

        public void Dispose() => _process?.Dispose();
    }
}
