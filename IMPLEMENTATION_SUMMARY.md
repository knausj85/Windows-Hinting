# Named Pipe Interface Implementation - Summary

## What Was Implemented

A complete named pipe interface for Windows-Hinting that allows external applications to control hints remotely. The implementation ensures that **execution order doesn't matter** - the connecting app can start before or after the main app.

## Files Added

### Core Implementation

1. **`Services/NamedPipeService.cs`** (Server)
   - Asynchronous named pipe server that listens for commands
   - Handles up to 10 concurrent connections
   - Parses text-based commands
   - Thread-safe event handling
   - Automatic retry logic built-in

2. **`NamedPipeClient/HintOverlayClient.cs`** (Client)
   - Simple C# API for external applications
   - Automatic retry logic (up to 50 retries over ~5 seconds)
   - Supports three main operations: toggle, select, deactivate
   - Methods return `bool` for success/failure

3. **`HintController.cs`** (Modified)
   - Integrated NamedPipeService
   - Added command handler: `OnNamedPipeCommandReceived`
   - Added hint selection method: `SelectHintByLabel`
   - Proper cleanup in Dispose

### Documentation

4. **`NAMED_PIPE_INTERFACE.md`**
   - Complete detailed documentation
   - Architecture overview
   - Usage examples in multiple languages (C#, PowerShell, Python, C++)
   - Connection retry logic explanation
   - Threading and error handling details
   - Extensibility guide

5. **`NAMED_PIPE_QUICK_REFERENCE.md`**
   - Quick start guide
   - Command reference table
   - Common use cases
   - Troubleshooting tips
   - Integration points

### Examples & Tests

6. **`Examples/HintOverlayClientExamples.cs`**
   - 8 practical code examples
   - Basic usage, automation, UI integration
   - Error handling patterns

7. **`NamedPipeClient.Tests/NamedPipeClientTests.cs`**
   - 10 comprehensive unit/integration tests
   - Covers happy paths and edge cases
   - Server running and not running scenarios

## Key Features

### ✅ Order-Independent Execution
- Client doesn't need to know when server starts
- Automatic retry with exponential backoff
- Maximum wait time: ~5 seconds
- Once either side starts, they can communicate

### ✅ Simple Text Protocol
- Easy to implement in any language
- Commands: `TOGGLE`, `SELECT <label>`, `DEACTIVATE`
- UTF-8 text, newline-delimited
- No binary serialization needed

### ✅ Asynchronous & Efficient
- Server uses async I/O
- Multiple concurrent connections (up to 10)
- Proper resource cleanup
- Thread-safe operations

### ✅ Well-Documented
- Detailed interface documentation
- Quick reference guide
- Working code examples
- Test suite included

## Architecture

```
┌─────────────────────────────────┐
│  External Application           │
│  (C#, PowerShell, Python, etc.) │
└────────────┬────────────────────┘
             │
             ▼
┌─────────────────────────────────┐
│  HintOverlayClient              │
│  • Toggle()                     │
│  • SelectHint(label)            │
│  • Deactivate()                 │
│  (Auto-retry: 50x, 100ms apart) │
└────────────┬────────────────────┘
             │
             ▼
┌─────────────────────────────────┐
│  Windows Named Pipe             │
│  "WindowsHinting_Pipe"             │
│  (UTF-8 text, line-delimited)   │
└────────────┬────────────────────┘
             │
             ▼
┌─────────────────────────────────┐
│  NamedPipeService (Server)      │
│  • Listens for connections      │
│  • Parses commands              │
│  • Raises events                │
└────────────┬────────────────────┘
             │
             ▼
┌─────────────────────────────────┐
│  HintController                 │
│  • OnNamedPipeCommandReceived()  │
│  • SelectHintByLabel()          │
│  • Calls HintStateManager       │
└────────────┬────────────────────┘
             │
             ▼
┌─────────────────────────────────┐
│  HintStateManager & Activators  │
│  (Executes the actual action)   │
└─────────────────────────────────┘
```

## Available Commands

| Command | Purpose | Example |
|---------|---------|---------|
| `TOGGLE` | Toggle hints on/off | `TOGGLE` |
| `SELECT <label>` | Select & activate a hint | `SELECT A` or `SELECT AB` |
| `SELECT <label> <action>` | Select with specific action | `SELECT A LEFT`, `SELECT A MOVE` |
| `DEACTIVATE` | Turn off hints | `DEACTIVATE` |

## Usage Examples

### C# - Basic Toggle
```csharp
using HintOverlay.NamedPipeClient;

using var client = new HintOverlayClient();
client.Toggle();
```

### C# - Select Hint
```csharp
using var client = new HintOverlayClient();
client.SelectHint("A");
```

### PowerShell - Direct Pipe
```powershell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "WindowsHinting_Pipe", [System.IO.Pipes.PipeDirection]::Out)
$pipe.Connect(5000)
$writer = New-Object System.IO.StreamWriter($pipe)
$writer.WriteLine("TOGGLE")
$writer.Flush()
$writer.Close()
$pipe.Close()
```

### Python - Direct Pipe
```python
import pywintypes
import win32pipe
import win32file

handle = win32file.CreateFile(
    r"\\.\pipe\WindowsHinting_Pipe",
    win32file.GENERIC_WRITE, 0, None,
    win32file.OPEN_EXISTING, 0, None
)
win32file.WriteFile(handle, b"TOGGLE\n")
handle.Close()
```

## Testing

Run the provided test suite to verify functionality:

```csharp
NamedPipeClientTests.RunAllTests();
```

Tests include:
- ✓ Toggle command
- ✓ Select command
- ✓ Deactivate command
- ✓ Invalid hint label handling
- ✓ Empty/null hint label validation
- ✓ Server not running scenario
- ✓ Multiple commands with same client
- ✓ Multiple independent clients
- ✓ Case-insensitive hint labels

## Integration Points

### For Windows-Hinting application
1. NamedPipeService automatically starts with HintController
2. Handles incoming commands via event
3. Integrates with existing HintStateManager
4. Proper cleanup on shutdown

### For External Applications
1. Create `HintOverlayClient` instance
2. Call desired method (Toggle, SelectHint, Deactivate)
3. Dispose when done
4. No need to handle timing or retries

## Configuration

| Setting | Value | Location |
|---------|-------|----------|
| Pipe Name | `WindowsHinting_Pipe` | NamedPipeService.cs |
| Max Connections | 10 | NamedPipeService.cs |
| Client Retries | 50 | HintOverlayClient.cs |
| Retry Delay | 100ms | HintOverlayClient.cs |
| Connection Timeout | 5s | HintOverlayClient.cs |

All values can be easily modified if needed.

## Future Enhancements

The design allows for easy extension:

1. **New Commands**
   - Add to `CommandType` enum
   - Implement in `ParseCommand`
   - Handle in `OnNamedPipeCommandReceived`

2. **Response Messages**
   - Currently one-way (client sends, server acts)
   - Could be extended to bidirectional (server sends responses)

3. **Authentication**
   - Could add security if needed
   - Optional per-command validation

4. **Performance Monitoring**
   - Could add metrics/telemetry
   - Command latency tracking

## Notes

- ✅ All code follows existing codebase conventions
- ✅ Proper logging at appropriate levels
- ✅ Exception handling and cleanup
- ✅ Thread-safe operations
- ✅ Zero-breaking changes to existing API
- ✅ Build successful with no errors/warnings

## Getting Started

1. Review `NAMED_PIPE_QUICK_REFERENCE.md` for quick overview
2. Check `Examples/HintOverlayClientExamples.cs` for code patterns
3. Read `NAMED_PIPE_INTERFACE.md` for detailed documentation
4. Run `NamedPipeClientTests.RunAllTests()` to verify
5. Integrate into your application

## Support

For questions or issues:
- Check the detailed documentation in `NAMED_PIPE_INTERFACE.md`
- Review examples in `Examples/HintOverlayClientExamples.cs`
- Run the test suite to diagnose issues
- Check Windows-Hinting logs for server-side details
