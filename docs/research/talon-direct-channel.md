# Research: Talon REPL / talon-rpc as a direct control channel

Resolves [#93](https://github.com/knausj85/Windows-Hinting/issues/93) (child of [#62](https://github.com/knausj85/Windows-Hinting/issues/62)). Builds on [#28](https://github.com/knausj85/Windows-Hinting/issues/28) ([`research/talon-rpc`](https://github.com/knausj85/Windows-Hinting/blob/research/talon-rpc/docs/research/talon-rpc.md)), the decision in [#33](https://github.com/knausj85/Windows-Hinting/issues/33), and the v1 surface in [#66](https://github.com/knausj85/Windows-Hinting/issues/66).

Gathered 2026-10-12 against the installed **Talon 1.0** (`C:\Program Files\Talon`, `release.md` heading "Talon 1.0 (Sep 20, 2026)", binaries dated 2026-10-11) and the operator's user dir (`%APPDATA%\talon\user`). Read-only: Talon was not running, nothing was connected to, nothing in the Talon directories was modified.

Each claim is tagged **[fact]** (read directly from a primary source), **[inferred]** (deduced from binary strings or stubs, not observed at runtime), or **[unknown]**.

---

## Verdicts

| Direction | Mechanism | Verdict |
|---|---|---|
| **App → Talon** (state push) | Talon REPL pipe (`\\.\pipe\talon_repl`) | **NO-GO** |
| | `talon-rpc` (`\\.\pipe\talon-rpc`) | **LATER**: re-check when Lunixbochs documents the protocol |
| | Overlay window title (#66), the status quo | **Keep.** It is already a working, level-triggered state push that any client can read |
| **Talon → app** (commands) | Talon REPL or `talon-rpc` | **NO-GO, structurally.** Both are servers hosted by Talon. They let others drive Talon, not let Talon drive us |
| | App-hosted hardened pipe (the #33 blueprint) | **LATER.** It is the only shape this direction can take, and it does not depend on Talon shipping anything. Resume it on a concrete trigger (below), not on Talon news |
| | Keystrokes (#66), the status quo | **Keep for v1** |

**What this means for the paused RPC effort.** The operator's condition was "pause unless the Talon REPL would be viable". It is not viable, so the effort stays paused. #33 also framed the pause as "until Talon ships RPC". That premise only ever applied to app → Talon. The Talon → app channel was always going to be a listener inside the app, whatever Talon ships, so it should be revisited when keystrokes actually hurt, not when Talon changes.

---

## 1. What Talon 1.0 actually ships

### 1.1 The REPL pipe (`talon_repl`)

- **Transport [fact]:** `repl.py` connects to `\\.\pipe\talon_repl` on Windows (`~/.talon/.sys/repl.sock` on Unix). It opens the pipe with plain `open(path, "r+b", buffering=0)` and switches it to `PIPE_READMODE_MESSAGE` through ctypes `SetNamedPipeHandleState`, with a 1 MiB read buffer. It needs no pywin32 (`repl.py`, `WindowsPipe.connect`).
- **Framing [fact]:** each message is one JSON object plus `\n`, keyed by `cmd`. Client → Talon: `input{text}`, `complete{text,state}`, `complete_import{line}`, `stdin{text}`, `reset`, `imports`. Talon → client: `prompt{prompt}`, `stdout{text}`, `stdin{mode,n}`, `exit` (`repl.py`, `interact()` and `CustomConsole`).
- **Semantics [fact]:** `input.text` is Python source that runs inside Talon's interpreter. Nothing structured comes back: you get a stream of `stdout` text (tracebacks included) followed by the next `prompt`. Non-tty mode (`echo … | repl`) sends one `input`, relays `stdout` and quits at the second `prompt`. No message carries a result value, an error flag or a request id.
- **Server side [inferred]:** `talon/windows/named_pipe.pyi` declares `NamedPipeServer(socketserver.BaseServer)` and `TextPipeFile`. `talon.dll` contains the SDDL fragment `D:P(A;;GA;;;SY)(A;;GA;;;` right next to a native pipe class exposing `read/write/readline/recv/send` and `accept_connection`. That reads as a protected DACL granting SYSTEM plus a runtime-appended SID (presumably the current user). If so, the REPL pipe is already scoped to the current user. This was not verified at runtime.
- **Documentation status [fact]:** the official docs (talonvoice.com/docs: talon, official, guide.*, commands, globals, python) do not document the REPL pipe or its protocol. The only documented contract is the community wiki line about the `repl` binary: *"You can pipe REPL commands into that … This is often used as a RPC interface to Talon"* ([talon.wiki misc-tips](https://talon.wiki/Customization/misc-tips/)). The wire protocol is private. A third-party integrator reached the same conclusion and wrote its own bridge instead: *"the REPL protocol is private and interactive"* ([jvictor0/Sheaf design.md](https://github.com/jvictor0/Sheaf/blob/main/openspec/changes/archive/2026-06-14-replace-talon-lite-with-talon-control/design.md)).

### 1.2 `talon-rpc`, the new thing

- **Changelog [fact]:** Talon 1.0 lists exactly one line under Scripting: *"Add preliminary RPC interface."* (`release.md` line 162; same text in the [public changelog](https://talonvoice.com/dl/latest/changelog.html)). No other changelog entry, no docs page and no `.pyi` stub describe it.
- **Client binary [fact, from strings]:** `talon-rpc.exe` (254 KB, Rust, signed "Talon Voice, LLC", source path `talon\src\bin\talon-rpc.rs`) resolves `TALON_HOME` from `APPDATA`. It connects to `\\.\pipe\talon-rpc` (Unix: `.sys/talon-rpc.sock`). Its only error strings are `open talon-rpc pipe`, `stdin error`, `rpc write error`, `rpc read error`, `stdout error` and `message too large:`. In other words it is a **dumb stdin ↔ pipe ↔ stdout relay with a message-size cap**, in the same family as `repl`. This matches the observed "open talon-rpc pipe: NotFound" panic.
- **Server [inferred, from `talon.dll` strings]:** a Rust actor `RpcSystem` (`talon\src\system\rpc.rs`) logs `[rpc] connect (` and `[rpc] disconnect (` and creates `\\.\pipe\talon-rpc` (`CreateNamedPipeW`, `ConnectNamedPipe`, `GetNamedPipeClientProcessId` are imported, so the log line probably carries the client PID). It exposes a Python-facing callback surface: `start(self, path)`, `handle_connect(self, client)`, `handle_message(self, client_id, msg)`, `handle_disconnect(self, client_id)` and `send_message(self, client_id, msg)`. So Rust owns the transport, and the message semantics live in Python in `talon/plugins/rpc.py4`. That file is 2,505 bytes and encrypted, so its content is **[unknown]**. Because `send_message(client_id, …)` exists, the server can push unsolicited messages to a connected client.
- **What the protocol says [unknown]:** the message schema, whether it evaluates code (as the REPL does) or dispatches named actions, any auth or handshake, and any versioning promise. Nothing in the operator's user dir (community, cursorless-talon, rango-talon, talon-gaze-ocr) uses it. A GitHub code search for `talon-rpc` only finds the unrelated Cursorless library of the same name (next bullet).
- **Name collision, not to be confused:** [`cursorless-dev/talon-rpc`](https://github.com/cursorless-dev/talon-rpc) is the npm *file-based* command-server library ("File based RPC server compatible with the Talon community command client"). It is not Talon's pipe.

### 1.3 Prior art: how the ecosystem does Talon → app today

- **command-server / community `command_client` [fact]:** Talon writes `request.json` into `%TEMP%\<name>` (Windows needs no per-user suffix "because the temp dir is user-specific"). It then **presses a hotkey** so that only the focused app instance executes the request, and polls `response.json` with exponential backoff from 0.5 ms up to a 3 s timeout. The request carries a uuid echo, and the server refuses requests older than 3 s (`community/core/command_client/rpc_client/*.py`; [command-server README](https://github.com/cursorless-dev/command-server)). The keypress is deliberate: it gives the channel focus-scoping without any auth.
- **Rango [fact]:** the same idea over the clipboard. Talon writes JSON to the clipboard, presses a hotkey, and polls the clipboard for `{"type":"response"}` (`rango-talon/src/command_server.py`).
- **Sheaf [fact]:** a Talon *user script* hosts a loopback HTTP server and an external app calls it. It explicitly rejected both the REPL and `talon-rpc` as private (link above).
- **Takeaway:** no shipping integration uses Talon's REPL or `talon-rpc` as its control channel. Every app-integration pattern is "Talon-side Python talks to an app-side listener". Some are signalled by a keypress for focus-scoping. Windows-Hinting's deleted pipe followed the same pattern.

### 1.4 Today's Windows-Hinting ↔ Talon surface [fact]

`community/apps/windows_hinting/windows_hinting.py` registers `ui.register("win_title", on_title)`. For app `Windows-Hinting(.exe)` it sets the scope `windows_hinting_active` to `"[Active]" in win.title`. `windows_hinting_active.talon` sends letter keys plus `enter`, with shift-prefixed action modifiers. Several of those commands contain a **`sleep(100ms)`** between the modifier key and the label, which is the one visible keystroke-surface tax.

---

## 2. Direction 1: app → Talon (state push)

### Via the REPL: NO-GO

- **Protocol sketch (what it would take):** on each mode change the host opens `\\.\pipe\talon_repl` in message mode and writes `{"cmd":"input","text":"actions.user.windows_hinting_set_mode('scroll')"}\n`. It then drains `stdout` until a `prompt` arrives and closes the pipe. (Or it keeps one connection open, but then it has to track the `prompt` handshake.)
- **Why NO-GO:**
  1. **Undocumented and private.** The wire protocol is an internal detail of `repl.py`, which Talon can change in any release. The only documented contract is "pipe text into the `repl` binary". The app would have to spawn `repl.exe` per message or reimplement a private protocol in C#.
  2. **The capability is arbitrary code execution.** The app would be in the business of generating Python source and running it inside the process that owns the microphone, input injection and `keychain`. (`repl.py` even special-cases `keychain.add(` to keep it out of history.) One quoting bug in a string the app builds becomes code injection into Talon. That is the opposite of #33's "no listener is the strongest security answer". Here *we* become the client of a code-exec endpoint.
  3. **No result channel.** Success and failure are indistinguishable except by scraping `stdout` for a traceback. There is no request id, so the app cannot know whether Talon applied the state.
  4. **Talon-only and Talon-script-coupled.** It works only if the operator's user scripts define the action named in the source string. Stream Deck and AutoHotkey gain nothing.
  5. **Edge-triggered.** A push made while Talon is down or restarting is simply lost (connect → `NotFound`, exactly as observed for `talon-rpc`). The app would need retry, backoff and resync-on-reconnect logic, and the REPL offers no "Talon is up" notification to resync on.

### Via talon-rpc: LATER

- `talon-rpc` is the right *shape* for this direction: a Talon-hosted, Rust-owned pipe with per-client ids and server push. But today it is labelled "preliminary", it is undocumented, and its schema sits in an encrypted Python plugin (§1.2). Designing against it now would mean reverse-engineering a moving target.
- **Re-open trigger:** a published `talon-rpc` message schema (docs page, `.pyi` stub, or changelog entry) that offers either (a) calling a named action with JSON arguments without `eval`, or (b) setting a user scope or tag from outside. With either one, the app-side adapter is about 100 lines (see cost below).

### Keep: the window title (#66)

The title is already a state push, and it compares favourably on every criterion in #93:

| Criterion | Overlay title (#66) | REPL | talon-rpc |
|---|---|---|---|
| Documented / stable | Win32 window text, and Talon's documented `ui.register("win_title")` event | Private protocol | "Preliminary", undocumented |
| Security | Read-only signal, so no new attack surface | Code exec in Talon | Unknown |
| Latency | Event-driven (`win_title`), no polling. Not measured, but it is the path the hint overlay already uses in production | Pipe connect + Python exec on Talon's side per push | Unknown |
| Talon absent / restart | **Level-triggered.** The state persists in the title, so a restarted Talon can re-read it | Push lost, needs resync logic | Push lost, needs resync logic |
| Non-Talon clients | Any tool can read a window title (AHK `WinExist`, Stream Deck plugins via Win32) | Talon-only | Talon-only |
| Modernize cost | Host/display concern only (#66 item 3) | New adapter plus retry state | New adapter plus retry state |

One small fix is worth recording for the community script, outside this repo's scope: `windows_hinting.py` only updates on *changes*. After a Talon restart while the overlay is up, the scope stays `False` until the next title change. Reading the current overlay title in `on_ready` (via `ui.windows()` filtered by app) closes that gap and makes full use of level-triggering.

---

## 3. Direction 2: Talon → app (commands)

### Via the REPL or talon-rpc: NO-GO (structural)

Both pipes are **created by Talon**, and the app could only connect to them as a client. A client can make Talon run things, but neither channel gives Talon a way to invoke code in *another* process. In theory `talon-rpc`'s `send_message(client_id, …)` could push commands to an app that stays connected. That would still need a Talon-side script that decides what to push over an undocumented, preliminary channel, plus an app that holds a persistent client connection with reconnect logic. That is strictly more moving parts than the app owning its own listener, and it buys nothing for Stream Deck or AHK. Waiting for Talon RPC was never going to unblock this direction.

### Via an app-hosted hardened pipe: LATER (blueprint ready, trigger-gated)

This is the #33 blueprint, unchanged. This research confirms it is cheap and reachable from Talon:

- **Reachability [fact]:** Talon's own `repl.py` shows that its bundled free-threaded CPython 3.14t opens a Windows named pipe with stdlib `open()` plus ctypes. The Talon-side client therefore needs no pywin32 and no venv install, which talon.wiki discourages for public packages (#28 §1.2).
- **Transport:** `NamedPipeServerStreamAcl.Create("WindowsHinting.v1", InOut, 1, Message, PipeOptions.CurrentUserOnly | FirstPipeInstance | Asynchronous)`. Optionally add a deny-NETWORK ACE (#28 §2.1). Use message mode, the same framing Talon's own REPL uses.
- **Protocol sketch:** one JSON object per message, request/response, with an id echoed for correlation (the command-server uuid idea, minus the files).
  ```
  → {"v":1,"id":"7","op":"activate","label":"AF","click":"right"}
  ← {"v":1,"id":"7","ok":true}
  → {"v":1,"id":"8","op":"scroll.enter"}            ← {"v":1,"id":"8","ok":true,"mode":"scroll"}
  → {"v":1,"id":"9","op":"scroll.target","n":3}
  → {"v":1,"id":"10","op":"scroll.hold","dir":"down"}  /  {"op":"scroll.release"}
  → {"v":1,"id":"11","op":"exit"}
  ← {"v":1,"id":"…","ok":false,"error":"no-such-label"}
  ```
  Ops map one-to-one onto intents. The op set is closed, and there is no eval, by construction. The response can carry the post-intent mode, which gives Talon an acked state read without the title. That covers most of what direction 1 wanted, over a channel we control.
- **Talon side:** about 40 lines of stdlib Python in community: `open(r'\\.\pipe\WindowsHinting.v1','r+b',buffering=0)`, message mode via ctypes, write, read, close. On `FileNotFoundError`, fall back to keystrokes so behaviour degrades gracefully when the app is absent.
- **Security:** as #33 and #28 §2.6 concluded. `CurrentUserOnly` closes cross-user and cross-elevation access, `FirstPipeInstance` closes squatting, and the ACL or `PIPE_REJECT_REMOTE_CLIENTS` closes network reach. The closed op set means the worst a same-user caller can do is what `SendInput` already lets it do. Add a bounded message size, which Talon's own `talon-rpc` client also enforces ("message too large"), and one instance with sequential handling.
- **Talon absent/restart:** irrelevant to the listener. The app serves regardless and Talon connects per command. There is no session state to resync.
- **Generality:** AHK (`FileOpen("\\.\pipe\…")`), Stream Deck plugins (Node `net.connect` to a pipe path), PowerShell and Python can all speak it. This is the only option in this document that serves non-Talon clients better than keystrokes.
- **Modernize cost:** exactly the #33 attachment point. One host adapter does accept, parse, `Post(intent)` onto the UI-thread funnel and reply. The reply needs the post-`Submit` mode, which the host already reconciles against after each `Submit`. Rough size: about 200–300 lines of C# plus tests. **Zero engine changes**, provided the intent set already meets #33's requirement (explicit optional `ClickAction` on activation) and scroll's intents from A #63 / C #65 exist. The one new requirement for the scroll keymap: hold and release must be **distinct intents**, not a key-down/key-up pair inferred inside the keyboard hook. The pipe has no key-up event. Recording that in the D #66 / F #68 intent list costs nothing now and keeps the attachment point honest.

**Resume triggers.** Resume the paused effort on any one of these:
1. Keystroke delivery fails in practice: focus is stolen, a `sleep(100ms)`-style race in hint actions shows up, or keys get swallowed by an elevated foreground app (UIPI blocks injected input into higher-integrity windows, and a pipe to our own process does not have that problem).
2. Scroll hold/release over keystrokes proves unreliable (auto-repeat, lost key-up).
3. A concrete non-Talon client (Stream Deck, AHK) is requested.
4. Talon needs an *acked* state read that the title cannot give.

None of these has been observed yet, so the verdict is LATER, not GO.

---

## 4. Uncertainties

- **`talon-rpc` semantics are unknown.** `plugins/rpc.py4` is encrypted. Everything in §1.2 beyond the pipe name, the relay behaviour and the callback names comes from string inference. The cheapest way to find out is to ask Lunixbochs in Slack what `talon-rpc` is for and whether its schema will be public. That beats probing it at runtime, which this ticket forbade anyway.
- **The REPL pipe's DACL** (SYSTEM + current user) is inferred from an SDDL fragment in `talon.dll`. The string was not tied to `talon_repl` at runtime. Treat it as likely, not confirmed.
- **Latency figures were not measured.** No Talon was running, by constraint. The comparison in §2 is structural (events vs per-push connect+exec), not numeric.
- The UIPI point in trigger 1 is standard Windows behaviour (`SendInput` does not reach higher-integrity windows). Whether it bites in practice depends on whether the overlay or hook ever needs keys while an elevated app is in the foreground. Today the hook is in-process, so injected keys from medium-integrity Talon still reach a medium-integrity hook. Re-check this only if the app is ever run elevated.
