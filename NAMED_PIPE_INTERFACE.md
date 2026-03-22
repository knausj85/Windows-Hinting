# Windows-Hinting Named Pipe Interface

## Overview

The Windows-Hinting application now supports a named pipe interface that allows external applications to control hints remotely. This interface supports toggling hints, selecting specific hints, and deactivating the overlay.

## Key Features

- **Order-Independent Execution**: The connecting client doesn't need to worry about timing. If the server isn't ready yet, the client will automatically retry with exponential backoff.
- **Simple Text Protocol**: Commands are simple text strings sent over a named pipe.
- **Asynchronous Server**: The named pipe server handles multiple concurrent connections efficiently.

## Architecture

### Server Side (Windows-Hinting)

The server is implemented in `Services/NamedPipeService.cs`:

- Creates a named pipe server that listens for incoming client connections
- Parses text-based commands
- Raises events that are handled by `HintController`
- Supports up to 10 concurrent connections

### Client Side

The client is implemented in `NamedPipeClient/HintOverlayClient.cs`:

- Provides a simple C# API for external applications
- Automatically handles connection retries (up to 50 retries with 100ms delays)
- Supports three main operations: toggle, select, and deactivate

## Usage

### Using the Provided Client

If you're building a C# application, you can use the `HintOverlayClient` class:

```csharp
using HintOverlay.NamedPipeClient;

// Create a client instance
using var client = new HintOverlayClient();

// Toggle hints on/off
bool success = client.Toggle();

// Select a specific hint by label (e.g., "A", "B", "AB")
bool success = client.SelectHint("A");

// Deactivate the overlay
bool success = client.Deactivate();
```

### Using Named Pipes Directly

You can also communicate with the named pipe directly from any language/platform:

1. Connect to the named pipe: `\\.\pipe\WindowsHinting_Pipe`
2. Send commands as UTF-8 text strings followed by a newline
3. Close the connection

#### Available Commands

- **TOGGLE** - Toggle the hint overlay on or off
  ```
  TOGGLE
  ```

- **SELECT <label> [action]** - Select and activate a hint by its label. Optional action: `LEFT`, `RIGHT`, `DOUBLE`, `MOVE`
  ```
  SELECT A
  SELECT AB
  SELECT XYZ
  SELECT A MOVE
  SELECT B RIGHT
  ```

- **DEACTIVATE** - Deactivate the hint overlay
  ```
  DEACTIVATE
  ```

### Example: PowerShell

```powershell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "WindowsHinting_Pipe", [System.IO.Pipes.PipeDirection]::Out)
$pipe.Connect(5000)
$writer = New-Object System.IO.StreamWriter($pipe)
$writer.WriteLine("TOGGLE")
$writer.Flush()
$writer.Close()
$pipe.Close()
```

### Example: Python

```python
import pywintypes
import win32pipe
import win32file

try:
    handle = win32file.CreateFile(
        r"\\.\pipe\WindowsHinting_Pipe",
        win32file.GENERIC_WRITE,
        0,
        None,
        win32file.OPEN_EXISTING,
        0,
        None
    )
    win32file.WriteFile(handle, b"TOGGLE\n")
    handle.Close()
except:
    print("Failed to connect to Windows-Hinting pipe")
```

### Example: C++ / Win32

```cpp
#include <windows.h>
#include <string>

bool SendCommand(const std::string& command) {
    HANDLE hPipe = CreateFileA(
        "\\\\.\\pipe\\WindowsHinting_Pipe",
        GENERIC_WRITE,
        0,
        NULL,
        OPEN_EXISTING,
        0,
        NULL
    );

    if (hPipe == INVALID_HANDLE_VALUE) {
        return false;
    }

    std::string message = command + "\n";
    DWORD cbWritten;

    if (!WriteFile(hPipe, message.c_str(), message.length(), &cbWritten, NULL)) {
        CloseHandle(hPipe);
        return false;
    }

    CloseHandle(hPipe);
    return true;
}

// Usage
SendCommand("TOGGLE");
SendCommand("SELECT A");
```

## Connection Retry Logic

The client implements automatic connection retry logic to handle order-independent execution:

- **Max Retries**: 50 attempts
- **Retry Delay**: 100ms between attempts
- **Connection Timeout**: 5 seconds per attempt
- **Total Max Wait Time**: ~5 seconds (50 retries × 100ms)

This means:
- If you start the client before the Windows-Hinting server, it will wait up to 5 seconds for the server to start
- If the server is already running, connections are established immediately
- The caller doesn't need to worry about timing or wait times

## Error Handling

The client's methods return `bool` to indicate success/failure:

- **true**: Command was sent successfully to the server
- **false**: Failed to connect after all retries, or the command format was invalid

Note: Returning `true` means the command was sent, but doesn't guarantee the action was executed. For example, if you send `SELECT unknown_label`, the server will log a warning but still return `true` (command received), and nothing will happen.

## Threading and Async Behavior

- The named pipe server runs asynchronously on a background thread
- The client's methods are synchronous but use async operations internally
- All operations are thread-safe and can be called from multiple threads

## Logging

All named pipe operations are logged using the application's logging system at appropriate levels:
- **Info**: Server start/stop, new connections, command types
- **Debug**: Connection details, parsed commands
- **Warning**: Invalid commands, failed activations
- **Error**: Critical errors in the pipe listener

## Implementation Details

### Server Flow

1. Application starts and creates `NamedPipeService`
2. `HintController` registers an event handler for `CommandReceived`
3. Server creates a named pipe and waits for connections
4. When a client connects:
   - Client sends a command string followed by newline
   - Server reads and parses the command
   - `CommandReceived` event is raised with the parsed command
   - Handler processes the command (toggle, select, or deactivate)
   - Connection is closed
5. Server returns to waiting for the next connection

### Client Flow

1. Client attempts to connect to the named pipe
2. If connection fails (timeout), retry with 100ms delay
3. Once connected, send the command text followed by newline
4. Close the connection
5. Return success/failure status

## Future Extensions

The interface is designed to be easily extensible. Additional commands can be added by:

1. Adding a new case to the `CommandType` enum in `NamedPipeService.cs`
2. Parsing the new command in the `ParseCommand` method
3. Handling the new command in `HintController.OnNamedPipeCommandReceived`
4. Adding a corresponding method to `HintOverlayClient` (optional, for convenience)
