# Research: Talon's planned RPC and secure local IPC options

Resolves [#28](https://github.com/knausj85/Windows-Hinting/issues/28). Facts gathered 2026-08-06 from primary sources (official docs, vendor documentation, source code); each claim carries its citation. Claims are labeled **documented fact**, **community convention**, or **not publicly documented**.

---

## 1. Talon Voice: RPC today and on the roadmap

### 1.1 What Talon speaks today (inbound control of Talon)

- Talon ships a `repl` executable in the `bin` subdirectory of the Talon home folder (`%APPDATA%\Talon` on Windows; the Windows entry point is `repl.bat`). The community wiki states verbatim: *"You can pipe REPL commands into that and they will be executed in the running Talon environment. This is often used as a RPC interface to Talon."* This is line-oriented arbitrary Python piped over stdin — no structured protocol, no schema, no authentication. **Community-documented fact.** Source: [talon.wiki — Tips and tricks](https://talon.wiki/Customization/misc-tips/); the official docs only document the GUI REPL (`Scripting -> Open REPL`): [talonvoice.com/docs](https://talonvoice.com/docs/).
- The underlying transport behind the `repl` binary (socket path, pipe, wire format) is **not publicly documented** anywhere — not in the official docs, the [full changelog](https://talonvoice.com/dl/latest/changelog.html), the wiki, or GitHub. It must be treated as an internal implementation detail; the supported surface is the `repl` binary itself.

### 1.2 What Talon speaks today (outbound, i.e. controlling other apps)

- Talon is *"scriptable with Python 3 (embedded CPython)"* ([talonvoice.com](https://talonvoice.com/)). User scripts run in-process with a full CPython (3.11.4 as of Talon v0.4.0, per the [changelog](https://talonvoice.com/dl/latest/changelog.html)), so outbound integration is plain Python: stdlib `socket`, `subprocess`, `urllib`, file I/O — there is **no Talon-specific outbound IPC layer**. **Documented fact.**
- Extra packages can be installed into Talon's private venv via `~/.talon/bin/pip` (pywin32 is importable in practice on Windows), but the wiki explicitly discourages distributing scripts that require venv installs: *"it is discouraged to ask users of any public package you build to install things in their venv."* Source: [talon.wiki](https://talon.wiki/Customization/misc-tips/). This is why popular bridges are stdlib-only.

### 1.3 Planned first-class RPC API

- **No public statement of an RPC roadmap was found.** Searched: [talonvoice.com/docs](https://talonvoice.com/docs/), the complete 0.1.x–0.4.0 [changelog](https://talonvoice.com/dl/latest/changelog.html) including the 0.4.0 beta-only features section (no RPC/socket/HTTP/IPC item appears), the [talonvoice/talon issue tracker](https://api.github.com/search/issues?q=repo:talonvoice/talon+rpc), talon.wiki, and general web search. If lunixbochs has stated plans, it was in the Talon Slack (private, unindexed). **Absence of public documentation is itself the finding: do not design against a "planned Talon RPC" — none is publicly committed.**

### 1.4 De-facto community integration patterns

- The dominant app-bridge pattern is the **command-server family**: file-based JSON RPC with a keypress as the signaling mechanism. [cursorless-dev/command-server](https://github.com/cursorless-dev/command-server) (VS Code, used by Cursorless) writes `request.json` to a per-user temp dir, Talon presses a hotkey to trigger execution, the server writes `response.json`; requests older than 3 seconds are refused. Generalized as [cursorless-dev/talon-rpc](https://github.com/cursorless-dev/talon-rpc) with the Talon-side [talon-command-client](https://github.com/cursorless-dev/talon-command-client); ported to Neovim ([hands-free-vim](https://github.com/fidgetingbits/talon-python-command-server)). **Community convention.**
- Other bridges use localhost HTTP or subprocess (e.g. [C-Loftus/talon-ai-tools](https://github.com/C-Loftus/talon-ai-tools)); see [awesome-talon](https://github.com/trillium/awesome-talon).
- Conclusion: whatever transport Windows-Hinting exposes, the Talon side will be hand-written Python. A transport reachable from Python with stdlib or bundled/installable packages (raw named pipe via `open()`/pywin32, localhost TCP, file-based) fits; a transport requiring a gRPC-over-named-pipes client does not (§2.2).

---

## 2. Secure local IPC options on Windows (app runs as the interactive user)

### 2.1 Named pipes with ACLs + client identity (recommended baseline)

- **Default DACL is permissive.** With a null security descriptor, *"The ACLs in the default security descriptor for a named pipe grant full control to the LocalSystem account, administrators, and the creator owner. They also grant read access to members of the Everyone group and the anonymous account."* Source: [Named Pipe Security and Access Rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights), [CreateNamedPipeA](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea).
- **Named pipes are network-reachable by design** (`\\ServerName\pipe\Name` over SMB/IPC$) if the DACL admits the caller ([Pipe Names](https://learn.microsoft.com/en-us/windows/win32/ipc/pipe-names)). The default mode is `PIPE_ACCEPT_REMOTE_CLIENTS`; mitigations are `PIPE_REJECT_REMOTE_CLIENTS` (not set by the managed implementation) or a DACL using the logon SID / denying `NT AUTHORITY\NETWORK` ([CreateNamedPipeA](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea), [Named Pipe Security and Access Rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)).
- **`PipeOptions.CurrentUserOnly` is the one-flag fix.** On the server it replaces the DACL with a single full-control ACE for the current user's owner SID (elevated vs. non-elevated differ, so elevation level is effectively checked too); on a .NET client it verifies the server's owner SID after connecting. Verified against [dotnet/runtime source](https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Windows.cs) and [PipeOptions docs](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions). Because enforcement is an ordinary DACL, **a raw Python client running as the same non-elevated user still connects with no changes** (named-pipe clients are just `CreateFile` on `\\.\pipe\name` — [Pipe Names](https://learn.microsoft.com/en-us/windows/win32/ipc/pipe-names)); it breaks only across users or elevation levels, which is the point.
- **Custom ACLs in modern .NET** go through `NamedPipeServerStreamAcl.Create(...)` (the `PipeSecurity` constructor overloads are .NET Framework-only; the ACL API was added via [dotnet/runtime#31112](https://github.com/dotnet/runtime/issues/31112)). Note: if `options` includes `CurrentUserOnly`, the passed `PipeSecurity` is ignored ([NamedPipeServerStreamAcl.Create](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstreamacl.create)).
- **Pipe-name squatting** is countered with `PipeOptions.FirstPipeInstance` (`FILE_FLAG_FIRST_PIPE_INSTANCE`) so a second process cannot create another instance of the name ([CreateNamedPipeA](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea), [PipeOptions](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions)).
- **Client identity checks are advisory only.** [`GetNamedPipeClientProcessId`](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid) yields the client PID and `GetImpersonationUserName`/[`ImpersonateNamedPipeClient`](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-impersonatenamedpipeclient) yield the user, but PIDs are recycled ([Raymond Chen](https://devblogs.microsoft.com/oldnewthing/20110107-00/?p=11803)) and any same-user process can launch or inject into a "trusted" exe, so PID/exe/signature checks are spoofable hardening, not a boundary (see §2.6).

### 2.2 gRPC over named pipes (ASP.NET Core)

- Supported since .NET 8 via Kestrel `ListenNamedPipe` with HTTP/2, usable inside WinForms apps via a `Microsoft.AspNetCore.App` framework reference; security via `NamedPipeTransportOptions.PipeSecurity` or (from .NET 9) a `CreateNamedPipeServerStream` callback honoring `CurrentUserOnly`. Sources: [gRPC + named pipes](https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes), [gRPC IPC overview](https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess).
- **Dead end for this integration: Python cannot speak it.** gRPC C-core (wrapped by grpc-python) supports `dns:`/`unix:`/`ipv4:`/`ipv6:`/`vsock:` — no named-pipe scheme ([grpc naming doc](https://github.com/grpc/grpc/blob/master/doc/naming.md)); Windows named-pipe transport has been an open request since 2017 ([grpc/grpc#13447](https://github.com/grpc/grpc/issues/13447)). [GrpcDotNetNamedPipes](https://www.nuget.org/packages/GrpcDotNetNamedPipes/) is .NET-only with a custom wire protocol. It also drags the full Kestrel/ASP.NET Core stack into a small tray/overlay app.

### 2.3 Localhost TCP + auth token

- Pattern: bind `127.0.0.1`, write port + cryptographically random token to a user-ACL'd file; client presents the token first (precedents: [Jupyter Server token auth](https://jupyter-server.readthedocs.io/en/latest/operators/security.html), Chrome DevTools `DevToolsActivePort`).
- Weaknesses: any local user can complete a TCP connect to a loopback port — the OS provides no peer identity or ACL on the socket; the token file's NTFS ACL is the entire access control. Microsoft's IPC guidance lists OS-integrated security as an advantage pipes have over TCP ([gRPC IPC overview](https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess)). Peer PID is obtainable via [`GetExtendedTcpTable`](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable) with the same PID-reuse caveat. Trivial from Talon (stdlib `socket`), but the weakest Windows-native access control.

### 2.4 Unix domain sockets (AF_UNIX) on Windows

- OS-supported since Windows 10 1803, stream-only, filesystem-ACL-secured ([AF_UNIX comes to Windows](https://devblogs.microsoft.com/commandline/af_unix-comes-to-windows/)); .NET supports it ([UnixDomainSocketEndPoint](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.unixdomainsocketendpoint)).
- **Dead end for the client: CPython still does not expose `socket.AF_UNIX` on Windows** — [python/cpython#77589](https://github.com/python/cpython/issues/77589) remains open (latest attempt [PR #137420](https://github.com/python/cpython/pull/137420), unmerged as of Aug 2025).

### 2.5 COM and Windows RPC

- Out-of-proc COM gives real caller access control via [`CoInitializeSecurity`](https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-coinitializesecurity) (and defaults to allowing all callers if never called — Microsoft strongly discourages that), but demands CLSID/AppID registration and lifetime plumbing; Python needs pywin32. Highest ceremony for a point-to-point command channel.
- Windows RPC `ncalrpc` (ALPC) is machine-local with authenticated callers ([Protocol Sequence Constants](https://learn.microsoft.com/en-us/windows/win32/rpc/protocol-sequence-constants)) but is a C/MIDL world with no supported .NET or Python bindings — impractical here.

### 2.6 Threat-model ceiling: same user, same session is not a boundary

- Microsoft's [Windows Security Servicing Criteria](https://www.microsoft.com/en-us/msrc/windows-security-servicing-criteria) defines serviced boundaries between users and between logon sessions; it offers no boundary between two processes of the same user in the same session.
- Concretely for this app: [`SendInput`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) permits input injection at equal-or-lesser integrity level, so any same-user medium-integrity malware can already synthesize the exact clicks the overlay produces without touching the IPC channel. The realistic security goals are therefore: (a) block **other users / other elevation levels** (`CurrentUserOnly` or explicit ACL), and (b) block **network reach** (deny-NETWORK ACE or `PIPE_REJECT_REMOTE_CLIENTS`). Authenticating *which same-user program* is calling is spoofable hardening, not a boundary.

---

## 3. Current integration: concrete security weaknesses

Server: `Windows-Hinting/Services/NamedPipeService.cs`; bundled .NET client: `Windows-Hinting/NamedPipeClient/HintOverlayClient.cs`; Talon-side client: [talonhub/community @ knausj-personal-experiments_rust](https://github.com/talonhub/community/tree/knausj-personal-experiments_rust) (`core/hinting/hinting_windows.py`, `apps/windows_hinting/`).

1. **Default DACL, no `CurrentUserOnly`, no `PipeSecurity`.** The server creates the pipe as `new NamedPipeServerStream("WindowsHinting_Pipe", PipeDirection.In, 10, Byte, Asynchronous)` — no ACL, no `PipeOptions.CurrentUserOnly`. Who can connect is decided entirely by the permissive OS default DACL (§2.1): full control for the owner, admins, and SYSTEM; read for Everyone and Anonymous. Any same-user process — and any elevated/SYSTEM process of any user — can open the pipe for write and drive the overlay.
2. **Zero client authentication.** No `GetNamedPipeClientProcessId`, no impersonation/identity check, no token or handshake. Any process that can open `\\.\pipe\WindowsHinting_Pipe` can send `TOGGLE` / `SELECT <label> <action>` / `DEACTIVATE` / `TOGGLETASKBAR`; `SELECT` synthesizes real mouse clicks (left/right/double/ctrl/shift) at hint locations — an unauthenticated command channel into input synthesis. (Per §2.6 the same-user increment is limited since same-user malware has `SendInput` anyway; the unmitigated cross-user/elevated and network exposure is the real gap.)
3. **Network exposure not closed.** Neither `PIPE_REJECT_REMOTE_CLIENTS` nor a deny-`NETWORK` ACE is applied; named pipes are remotely addressable via `\\host\pipe\...` subject to the DACL (§2.1).
4. **Pipe-name squatting / server spoofing.** `PipeOptions.FirstPipeInstance` is not set, and the pipe is inbound-only, so clients get no response and never verify the server's identity. Any same-user process can pre-create `\\.\pipe\WindowsHinting_Pipe` (or additional instances) and capture the Talon client's commands.
5. **No message-size or rate limits.** `ReadLineAsync` on an unbounded line allows a client to stream an arbitrarily long line (memory growth) and connections are accepted in an unbounded loop of fire-and-forget handler tasks.
6. **Talon-side client is fire-and-forget with a mismatched pipe name.** `core/hinting/hinting_windows.py` writes UTF-8 newline-terminated commands via pywin32 `win32file.CreateFile(r"\\.\pipe\HintOverlay_Pipe", GENERIC_WRITE, ...)` — note **`HintOverlay_Pipe`, not `WindowsHinting_Pipe`**; errors are printed and dropped; no retries, no auth. Only `TOGGLE` is actually wired to the pipe; `SELECT`/`DEACTIVATE` helpers exist but are never called. Selection currently happens via **simulated keystrokes** into the focused overlay window (`apps/windows_hinting/windows_hinting_active.talon` sends letters + Enter with shift-modifier prefixes), gated on the window title containing `[Active]`.
7. **Protocol is guessable plaintext on a well-known name.** Fixed public pipe name plus a trivial space-separated text protocol means zero effort for any local process to discover and drive it.

---

## 4. Facts for the decision

- **Talon has no publicly committed RPC roadmap.** Design for what exists: Talon-side clients are hand-written Python (stdlib preferred; pywin32 works but venv installs are discouraged for distributed scripts). Do not wait for, or design against, a native Talon protocol. (§1.3)
- **gRPC-over-named-pipes and AF_UNIX are eliminated by the client side**: grpc-python has no named-pipe transport ([grpc/grpc#13447](https://github.com/grpc/grpc/issues/13447)) and CPython has no `AF_UNIX` on Windows ([cpython#77589](https://github.com/python/cpython/issues/77589)). (§2.2, §2.4)
- **The cheapest meaningful fix keeps the current transport**: create the pipe with `NamedPipeServerStreamAcl.Create` / `PipeOptions.CurrentUserOnly | FirstPipeInstance | Asynchronous`. That closes cross-user, cross-elevation, and (via the owner-only DACL plus optionally a deny-NETWORK ACE) network access, and blocks name-squatting — while the existing Python client keeps working unchanged as the same non-elevated user. (§2.1)
- **Same-user process authentication is not achievable as a boundary** (Microsoft servicing criteria; `SendInput` is same-user-open anyway). PID/exe checks via `GetNamedPipeClientProcessId` are optional hardening at best. (§2.6)
- **Localhost TCP + token** is the fallback only if pipe access from Talon ever becomes a problem — it is strictly weaker (no OS peer identity) and pipes are already reachable from Talon's Python. (§2.3)
- **Independent of transport**: fix the pipe-name mismatch (`HintOverlay_Pipe` vs `WindowsHinting_Pipe`), add a bounded read (max line length), and consider a duplex pipe so the client can verify the server owner and get acks. (§3)
