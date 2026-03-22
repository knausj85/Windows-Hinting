# Named Pipe Interface - Quick Reference

## Quick Start

### For C# Applications

```csharp
using HintOverlay.NamedPipeClient;

using var client = new HintOverlayClient();

// Toggle hints
client.Toggle();

// Select a hint
client.SelectHint("A");

// Deactivate
client.Deactivate();
```

### For Other Languages

Connect to: `\\.\pipe\WindowsHinting_Pipe` (UTF-8 text, line-delimited)

Commands:
- `TOGGLE\n`
- `SELECT A\n`
- `DEACTIVATE\n`

## Key Features

✅ **Order-Independent** - Client and server startup order doesn't matter
✅ **Automatic Retries** - Built-in retry logic (up to 5 seconds)
✅ **Async** - Efficient asynchronous server handling
✅ **Thread-Safe** - Can be called from multiple threads
✅ **Simple** - Easy to integrate and use

## Available Commands

| Command | Purpose | Example |
|---------|---------|---------|
| `TOGGLE` | Toggle hints on/off | `TOGGLE` |
| `SELECT <label>` | Select and activate a hint | `SELECT A` or `SELECT AB` |
| `DEACTIVATE` | Turn off hints | `DEACTIVATE` |

## Return Values

The C# client methods return `bool`:
- `true` = Command sent successfully
- `false` = Failed to connect (after retries exhausted)

## Common Use Cases

### Toggle Hints with Global Hotkey
```csharp
using var client = new HintOverlayClient();
client.Toggle();
```

### Select Hint from UI Button
```csharp
using var client = new HintOverlayClient();
client.SelectHint(buttonLabel); // e.g., "A", "B", "AB"
```

### Automated Testing
```csharp
using var client = new HintOverlayClient();
client.Toggle();           // Activate hints
Thread.Sleep(500);         // Wait for hints to load
client.SelectHint("A");    // Select first hint
client.Deactivate();       // Deactivate
```

## Integration Points

### From Your Application
1. Add reference to `NamedPipeClient` namespace
2. Create `HintOverlayClient` instance
3. Call desired method
4. Dispose when done (using statement recommended)

### From Command Line (PowerShell)
```powershell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "WindowsHinting_Pipe", [System.IO.Pipes.PipeDirection]::Out)
$pipe.Connect(5000)
$writer = New-Object System.IO.StreamWriter($pipe)
$writer.WriteLine("TOGGLE")
$writer.Flush()
$writer.Close()
$pipe.Close()
```

### From Node.js
```javascript
const net = require('net');
const client = net.createConnection({ path: '\\\\.\\pipe\\WindowsHinting_Pipe' }, () => {
  client.write('TOGGLE\n');
  client.end();
});
```

## Architecture

```
External App
    |
    v
HintOverlayClient (with auto-retry)
    |
    v
Named Pipe: "WindowsHinting_Pipe"
    |
    v
NamedPipeService (server)
    |
    v
HintController (handles commands)
    |
    v
HintStateManager (manages hint state)
```

## Troubleshooting

### "Failed to connect" after 5 seconds
- Ensure Windows-Hinting application is running
- Check Windows Firewall isn't blocking pipes
- Check application logs for errors

### Command sent but nothing happened
- Verify hint label is correct (case-insensitive)
- Check Windows-Hinting logs for warnings
- Ensure hints are active before selecting

### Multiple applications can't connect simultaneously
- This is by design - connections are sequential
- Each connection is processed, then closed
- Client automatically handles retry/reconnect

## Limits & Configuration

| Setting | Value |
|---------|-------|
| Max concurrent connections | 10 |
| Command timeout | 5 seconds |
| Retry attempts | 50 |
| Retry delay | 100ms |
| Max wait time | ~5 seconds |

## See Also

- `NAMED_PIPE_INTERFACE.md` - Detailed documentation
- `Examples/HintOverlayClientExamples.cs` - Code examples
- `Services/NamedPipeService.cs` - Server implementation
- `NamedPipeClient/HintOverlayClient.cs` - Client implementation
