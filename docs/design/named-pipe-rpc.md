# Design: Talon-driven RPC for Windows-Hinting over a named pipe

Status: implemented on `feature/named-pipe-rpc` (cut from `main`); see "Implementation notes" at the end.

## Goal

Let Talon (and other approved local automation) drive Windows-Hinting the way the
Cursorless `command-server` lets it drive VS Code: a secure, file/IPC-based request
and response channel with no hotkey, replacing the current keystroke choreography
(`shift-<key>`, typed label letters, `enter`) in `community/apps/windows_hinting`.

## Why a pipe rather than the talon-rpc file protocol

The VS Code design uses a keystroke for two reasons that do not apply here:

1. Focus discrimination between many VS Code windows sharing one directory.
   Windows-Hinting is single-instance (mutex in `Program.cs`).
2. Waking the extension host. A WinForms app can watch for input itself.

Both file RPC and a `CurrentUserOnly` named pipe land in the same security tier
("same user only, local only, no replay"). The pipe is event-driven, has no stale
file state, allows caller identification (`GetNamedPipeClientProcessId`), and gets
first-instance squatting protection from the kernel. The file protocol's advantage
is reusing community's client untouched; the pipe costs ~60 lines of Talon client.

Reference implementation studied: Neru (`internal/ipcendpoint/endpoint_windows.go`,
`internal/adapter/ipc/peercred_*.go`). Borrow its endpoint and protocol hygiene; do
not borrow its wide command surface.

## Threat model (what the channel can and cannot protect)

- Same-user processes can always reach the pipe, directly or by dropping a script
  into Talon's hot-reloaded `%APPDATA%\talon\user` or editing `preferences.json`.
  No local IPC channel is a boundary against same-user malware.
- Other users, remote (SMB) callers and low-integrity / AppContainer processes are
  kept out by the pipe's DACL and `PIPE_REJECT_REMOTE_CLIENTS`.
- The release build is `uiAccess="true"` (`app.manifest`), so Windows-Hinting can
  act on elevated (UAC) windows. A pipe into it is therefore a potential UIPI
  bypass for medium-integrity callers. This is the one risk that matters and is
  handled by the caller policy below, not by the transport.
- Microsoft does not treat UAC as a security boundary. Size the UIAccess checks as
  hygiene; the controls to rely on are the narrow command surface and the visible
  overlay.

## Server

Replace the dormant `Services/NamedPipeService.cs` (and `NamedPipeClient/`,
`Examples/HintOverlayClientExamples.cs`) with `Services/PipeCommandService.cs`.

Endpoint

- Name: `\\.\pipe\WindowsHinting-<user SID>` where the SID comes from the process
  token (`WindowsIdentity.GetCurrent().User.Value`). No discovery file.
- `new NamedPipeServerStream(name, PipeDirection.InOut, maxNumberOfServerInstances: 1,
  PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly)`.
  `CurrentUserOnly` gives an owner-only DACL plus reject-remote-clients. If the name
  is already taken, log an error and run without RPC; do not retry silently.
- Serial accept loop: one client at a time, one request per connection.
- Verify once on the target machine that `CurrentUserOnly` is honored. A medium-plus
  mandatory label (`S:(ML;;NW;;;S-1-16-8448)`) is optional hardening; .NET may not
  combine it with `CurrentUserOnly`, so only add it if building the full descriptor
  by hand is acceptable.

Protocol

- One request line, one response line, UTF-8 JSON, `\n` terminated.
- Request: `{"protocol":1,"command":"select","args":["AB","right"],"id":"<opaque>"}`.
  Unknown fields rejected. Max 64 KiB. Read deadline 5 s after connect.
- Response: `{"protocol":1,"id":"<echo>","ok":true,"code":"OK","message":"...","data":{...}}`.
- Codes: `OK`, `ERR_UNKNOWN_COMMAND`, `ERR_INVALID_INPUT`, `ERR_NOT_ACTIVE`,
  `ERR_LABEL_NOT_FOUND`, `ERR_ELEVATED_TARGET`, `ERR_UNAUTHORIZED_CALLER`,
  `ERR_VERSION_MISMATCH`, `ERR_INTERNAL`.
- `protocol` higher than the server's is refused with `ERR_VERSION_MISMATCH`.

Command surface (deliberately tiny; no additions without revisiting this doc)

| command          | args                                                   | effect                                   |
|------------------|--------------------------------------------------------|------------------------------------------|
| `ping`           | none                                                   | `OK`, no data                            |
| `status`         | none                                                   | `data: {active, source, mode, pendingAction, hintCount}` |
| `toggle`         | none                                                   | `HintController.Toggle()`                |
| `toggle_taskbar` | none                                                   | `HintController.ToggleTaskbar()`         |
| `select`         | `label`, optional `left|right|double|move|ctrl|shift`  | `SelectHintByLabel` against `CurrentHints` only |
| `deactivate`     | none                                                   | `HintStateManager.Deactivate()`          |

Explicitly not offered: coordinate clicks, element names or text, hint lists,
config/preferences mutation, `exec`, any headless or zero-opacity mode.

Dispatch

- Parse and validate on the pipe thread; marshal the action onto the overlay
  thread with `OverlayManager.BeginInvoke` (same pattern as the keyboard hook
  path in `HintController`), wait for completion, then write the response.
- Session semantics are unchanged: `toggle` starts a visible session that ends by
  `AutoHideTimeoutSeconds`, by `deactivate`, or by `select` (which deactivates
  after activation, as `SelectHintByLabel` already does). Keep the existing
  200 ms toggle debounce; add a per-connection rate limit of one state-changing
  command per 100 ms.

Caller policy (hard-coded; logged; no preference to disable)

On every connection, before reading the request:

1. `GetNamedPipeClientProcessId`, then `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`,
   `QueryFullProcessImageName`, `OpenProcessToken(TOKEN_QUERY)`,
   `GetTokenInformation(TokenIntegrityLevel)` and `GetTokenInformation(TokenUIAccess)`.
   Open the process first and hold the handle for the connection's lifetime.
2. Log `pid`, image path, integrity level, UIAccess flag and the command. Log
   rejected connections at Warning.

Before executing `toggle`, `toggle_taskbar` or `select`:

3. Read the integrity level of the foreground window's process (for `select`, the
   process owning the hint's element window; fall back to foreground).
4. If the target is above medium integrity and the caller token does not carry
   UIAccess, refuse with `ERR_ELEVATED_TARGET`. Keyboard/hotkey-driven activation
   is unaffected.
5. If this process itself lacks UIAccess (debug build, portable location), treat
   elevated targets as unreachable for RPC regardless of caller.

Rationale: a UIAccess caller already bypasses UIPI, so the pipe grants it nothing
new; a medium caller must not gain it through us. Medium-plus (UIAccess) processes
are protected from injection by no-write-up, and UIAccess requires signature plus
secure location, so the flag is OS-attested rather than spoofable by a copied exe.

Known weaknesses accepted: AutoHotkey and Windows' own assistive binaries also
carry UIAccess; a medium process can launch them. The invariant is "no new
capability", not "only Talon".

CsWin32 additions to `NativeMethods.txt`: `GetNamedPipeClientProcessId`,
`OpenProcess`, `QueryFullProcessImageName`, `OpenProcessToken`,
`GetTokenInformation`, `TOKEN_INFORMATION_CLASS`, `TOKEN_MANDATORY_LABEL`,
`GetSidSubAuthority`, `GetSidSubAuthorityCount`, `PROCESS_ACCESS_RIGHTS`,
`TOKEN_ACCESS_MASK`.

Wiring

- `ServiceCollectionExtensions`: register `PipeCommandService` as singleton.
- `HintController`: inject it, subscribe to a `CommandReceived` event or pass a
  dispatcher delegate; start it after `_updateService.Initialize()`; stop in
  `Dispose`. Expose `SelectHintByLabel` result (found / not found / error) so the
  response code is accurate.
- `HintStateManager`: add a read-only snapshot for `status`.
- Preferences: an `Rpc.Enabled` bool (default true) is acceptable because turning
  the feature *off* is not a security-relevant direction. Nothing that loosens
  the caller policy goes in preferences.

## Talon client (`community/apps/windows_hinting/`)

Files

- `windows_hinting_pipe.py`: pure Python, no `talon` import, so it is unit-testable.
  - `current_user_sid()` via ctypes: `OpenProcessToken`, `GetTokenInformation(TokenUser)`,
    `ConvertSidToStringSidW`.
  - `open_pipe(name)`: `CreateFileW(name, GENERIC_READ|GENERIC_WRITE, 0, None,
    OPEN_EXISTING, SECURITY_SQOS_PRESENT | SECURITY_ANONYMOUS, None)`. Anonymous
    impersonation level stops a squatting server impersonating Talon.
  - Optional `verify_server(handle)`: `GetNamedPipeServerProcessId` and compare the
    server token's user SID to ours (mirror of Neru's client check).
  - `call(command, args, timeout_ms=1000) -> dict`: write one JSON line, read until
    `\n`, raise on `ok == false` with `code` and `message`.
- `windows_hinting.py`: Talon `Module`/`Context`; actions
  `windows_hinting_toggle()`, `windows_hinting_toggle_taskbar()`,
  `windows_hinting_select(label: str, action: str = "")`,
  `windows_hinting_deactivate()`, `windows_hinting_status() -> dict`.
  Keep the existing `[Active]` window-title scrape for `user.windows_hinting_active`
  (push, not pull). Do not use `tag: user.command_client` or override
  `command_server_directory`; call the pipe client directly to avoid colliding with
  the VS Code context when hints are shown over VS Code.
- `windows_hinting_active.talon`: replace each `key(shift-x)` / `key(user.hinting)`
  / `key(enter)` rule with `user.windows_hinting_select(hinting, "right")` etc.
  Labels are spoken as letters; join without spaces before sending.

Fallback: if the pipe is absent (`ERROR_FILE_NOT_FOUND`), log once and fall back to
the keystroke path so older Windows-Hinting builds keep working.

## Testing

Server (on the Windows VM; full `MSBuild.exe`, never `dotnet build`):

1. Build Debug. Run. Confirm the pipe exists:
   `[IO.Directory]::GetFiles("\\.\pipe\") | ? { $_ -like "*WindowsHinting*" }`.
2. PowerShell smoke client (`NamedPipeClientStream`): `ping`, `status`, `toggle`
   over a normal window, `status` shows active, `select <visible label>`,
   `deactivate`, unknown command, oversized request, bad JSON, `protocol: 99`.
3. Elevated-target policy: open an elevated `cmd`, `toggle` from a medium
   PowerShell -> `ERR_ELEVATED_TARGET`; from the installed Talon -> allowed.
   Debug build (uiAccess=false) must refuse for every caller.
4. Squatting: pre-create the pipe name in PowerShell, start Windows-Hinting,
   confirm it logs the conflict and runs without RPC.
5. Concurrency: two clients in parallel; second waits or times out, nothing runs twice.
6. Check the log viewer shows pid / image / integrity / UIAccess per connection.

Client:

- Unit tests for framing, SID parsing and error mapping with a fake transport
  (run anywhere; stub the `talon` module).
- On the VM, via the Talon REPL: `actions.user.windows_hinting_status()`, then a
  `toggle` + `select` cycle; confirm in the Windows-Hinting log that the caller
  shows `talon.exe` with UIAccess.

## Out of scope for this branch

Returning hint labels to Talon for grammar generation; a medium-plus mandatory
label on the pipe; signature verification of callers; any change to the
file-based `talon-rpc` protocol in community.

## Implementation notes

Where the code settles something this doc left open, or departs from it:

- The single server instance is reused (`Disconnect` + `WaitForConnectionAsync`), so the
  name is never released between clients for a squatter to take.
- Rate limiting delays a state-changing command until 100 ms after the previous one
  rather than refusing it (no error code fits). The accept loop is serial, so the limit
  is effectively global rather than per connection.
- `toggle` / `toggle_taskbar` wait for the hint scan to finish and return the `status`
  snapshot as `data`; a debounced toggle answers `OK` with message `Debounced`. A toggle
  that only dismisses hints skips the elevated-target check.
- For `select`, the target process is the hint element's cached UIA `ProcessId`
  (foreground window as fallback). An unreadable target integrity counts as elevated.
- `select` hides the hints before activating, like the keyboard path (the old
  `SelectHintByLabel` deactivated afterwards).
- Responses are written under a 2 s timeout, and the server waits up to 2 s for the
  client to close before disconnecting (`DisconnectNamedPipe` discards unread data).
- Builds go through `dotnet build` now; the MSBuild-only rule above predates the move of
  UI Automation to CsWin32.
