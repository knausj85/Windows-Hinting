# Named Pipe Interface - Architecture Diagrams

## System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                   External Applications                          │
├─────────────────┬──────────────────┬─────────────┬──────────────┤
│   C# App       │   PowerShell      │   Python    │   C++ App    │
│                │                   │             │              │
└────────┬────────┴────────┬─────────┴──────┬──────┴──────┬───────┘
         │                 │                │             │
         └─────────────────┼────────────────┼─────────────┘
                           │
            ┌──────────────▼───────────────┐
            │  HintOverlayClient (retry    │
            │  logic: 50x, 100ms)          │
            └──────────────┬───────────────┘
                           │
              ┌────────────▼──────────────┐
              │  Windows Named Pipe       │
              │  "HintOverlay_Pipe"       │
              │  (UTF-8, line-delimited)  │
              └────────────┬──────────────┘
                           │
              ┌────────────▼──────────────┐
              │  NamedPipeService         │
              │  (Server: async listener) │
              │  Max 10 connections       │
              └────────────┬──────────────┘
                           │
              ┌────────────▼──────────────┐
              │  HintController           │
              │  + CommandReceived event  │
              │  + SelectHintByLabel()    │
              └────────────┬──────────────┘
                           │
         ┌─────────────────┴─────────────────┐
         │                                   │
    ┌────▼─────────┐          ┌─────────────▼────┐
    │ HintState    │          │ Element Activators│
    │ Manager      │          │ + Invoke          │
    │ + Toggle     │          │ + SetFocus        │
    │ + Select     │          │ + TogglePattern   │
    │ + Deactivate │          │ + SelectionItem   │
    └──────────────┘          │ + ExpandCollapse  │
                              └──────────────────┘
```

## Command Flow - Toggle

```
External App                NamedPipeClient         NamedPipeService         HintController
     │                            │                        │                      │
     │─── client.Toggle() ────────►                        │                      │
     │                            │                        │                      │
     │                   ┌─ Retry Logic ──┐               │                      │
     │                   │ (50x, 100ms)    │               │                      │
     │                   └─────────────────┘               │                      │
     │                            │                        │                      │
     │                   Connect to pipe                   │                      │
     │                            │──── Send "TOGGLE\n" ──►                       │
     │                            │                        │─ Parse command ─────►│
     │                            │                        │                      │
     │                            │                        │ OnNamedPipeCommand   │
     │                            │                        │◄─ Raise Event ────│ │
     │                            │                        │ │                    │
     │                            │                        │ │  Toggle() called   │
     │◄─ Return true ─────────────│◄─ Close connection ────│─┘                    │
     │                            │                        │                      │
     │                            │                        │ HintState changes    │
     │                            │                        │ Events fired         │
```

## Command Flow - Select

```
External App              NamedPipeClient      NamedPipeService      HintController
     │                         │                     │                    │
     │─ client.SelectHint() ──►│                     │                    │
     │                         │                     │                    │
     │                    (retry logic)              │                    │
     │                         │                     │                    │
     │                   Connect to pipe            │                    │
     │                         │─ "SELECT A\n" ────►│                    │
     │                         │                     │─ Parse ─────────►│
     │                         │                     │                    │
     │                         │                     │ OnNamedPipeCommand
     │                         │                     │◄─ Raise Event ──│ │
     │                         │                     │ │                  │
     │                         │                     │ │ SelectHintByLabel
     │                         │                     │ │ ┌─ Find hint "A"
     │                         │                     │ │ │  
     │                         │                     │ │ ├─ TryActivate()
     │                         │                     │ │ │
     │                         │                     │ │ └─ Deactivate
     │◄─ Return true ─────────►│◄─ Close connection──│─┘
     │                         │                     │
```

## Retry Logic Flow

```
Client: SelectHint("A")
   │
   ├─ Attempt 1: Connect → Timeout → Retry
   │
   ├─ Wait 100ms
   │
   ├─ Attempt 2: Connect → Timeout → Retry
   │
   ├─ Wait 100ms
   │
   ├─ ... (up to 50 attempts, ~5 seconds)
   │
   ├─ Attempt 50: Connect → TIMEOUT → Return false
   │
   └─ OR

   ├─ Attempt N: Connect → SUCCESS → Send command → Return true
```

## State Transitions

```
HintMode State Machine:

                    ┌──────────────────┐
                    │    INACTIVE      │◄─── DEFAULT
                    │  (No hints shown)│
                    └─────────┬────────┘
                              │ Toggle() or external command
                              │ Activate()
                              ▼
                    ┌──────────────────┐
                    │    SCANNING      │
                    │ (Finding elements)│
                    └─────────┬────────┘
                              │ Hints found
                              │ SetHints()
                              ▼
                    ┌──────────────────┐
    ┌───────────────│    ACTIVE        │◄────────┐
    │               │  (Hints visible) │         │
    │               └─────────┬────────┘         │
    │                         │                 │
    │     SelectHint()        │  SelectHint()   │
    │    /Toggle()            │                 │
    │    /Deactivate()        │                 │
    │         │               │                 │
    │         └──────────────►├─────────────────┘
    │                         │ Hint activated
    │                         │ Element clicked
    │                         │ Deactivate()
    │                         ▼
    │            ┌──────────────────┐
    │            │    SELECTING     │
    │            │ (Hint committed) │
    │            └─────────┬────────┘
    │                      │
    └──────────────────────┘
         Return to INACTIVE
```

## File Organization

```
HintOverlay/
│
├── Services/
│   └── NamedPipeService.cs          ✨ NEW - Server implementation
│       NamedPipeCommand enum/class  ✨ NEW - Command types
│
├── NamedPipeClient/
│   └── HintOverlayClient.cs         ✨ NEW - Client API
│
├── NamedPipeClient.Tests/
│   └── NamedPipeClientTests.cs      ✨ NEW - Test suite
│
├── Examples/
│   └── HintOverlayClientExamples.cs ✨ NEW - Usage examples
│
├── HintController.cs                📝 MODIFIED - Integrated pipe service
│
├── NAMED_PIPE_INTERFACE.md          ✨ NEW - Detailed documentation
├── NAMED_PIPE_QUICK_REFERENCE.md    ✨ NEW - Quick start guide
├── IMPLEMENTATION_SUMMARY.md        ✨ NEW - Implementation overview
└── ARCHITECTURE_DIAGRAMS.md         ✨ NEW - This file
```

## Sequence Diagram - Complete Toggle Workflow

```
┌──────────┐        ┌──────────────┐      ┌─────────────┐      ┌──────────────┐
│External  │        │HintOverlay   │      │NamedPipe    │      │HintState     │
│App       │        │Client        │      │Service      │      │Manager       │
└─────┬────┘        └──────┬───────┘      └──────┬──────┘      └──────┬───────┘
      │                     │                    │                    │
      │ new Client()        │                    │                    │
      ├────────────────────►│                    │                    │
      │ Toggle()            │                    │                    │
      ├────────────────────►│                    │                    │
      │                     │ Connect to pipe   │                    │
      │                     ├───────────────────►│                    │
      │                     │ Write "TOGGLE\n"  │                    │
      │                     ├───────────────────►│                    │
      │                     │ Close connection  │                    │
      │                     │◄───────────────────┤                    │
      │ return true         │                    │ Parse & raise      │
      │◄────────────────────┤                    │ event              │
      │                     │                    ├───────────────────►│
      │                     │                    │ Activate()         │
      │                     │                    │ SetMode(SCANNING)  │
      │                     │                    │                    │
      │                     │                    │ EventFired         │
      │                     │                    │◄────────────────────┤
      │                     │                    │                    │
      │ (app continues)     │                    │ ScanForHints()     │
      │                     │                    │ SetHints()         │
      │                     │                    │ SetMode(ACTIVE)    │
      │                     │                    │                    │
      │                     │                    │ ModeChanged event  │
      │                     │                    │ HintsChanged event │
```

## Integration Points

```
┌─────────────────────────────────────────────────────────┐
│         HintOverlay Application                         │
│                                                         │
│  ┌───────────────────────────────────────────────────┐ │
│  │ HintController                                    │ │
│  │                                                   │ │
│  │  ┌─────────────────────────────────────────────┐ │ │
│  │  │ NamedPipeService                            │ │ │
│  │  │ • Starts in constructor                     │ │ │
│  │  │ • Listens for connections                   │ │ │
│  │  │ • Raises CommandReceived event              │ │ │
│  │  │ • Stops in Dispose()                        │ │ │
│  │  │                                             │ │ │
│  │  │ Handler: OnNamedPipeCommandReceived()       │ │ │
│  │  │ • Processes TOGGLE command                  │ │ │
│  │  │ • Processes SELECT command                  │ │ │
│  │  │ • Processes DEACTIVATE command              │ │ │
│  │  │ • Calls SelectHintByLabel() for SELECT      │ │ │
│  │  └─────────────────────────────────────────────┘ │ │
│  │                                                   │ │
│  │  Existing Components (unchanged):                 │ │
│  │  • HintStateManager                              │ │
│  │  • HintInputHandler                              │ │
│  │  • OverlayForm                                   │ │
│  │  • UIAutomationService                           │ │
│  │  • etc.                                          │ │
│  │                                                   │ │
│  └───────────────────────────────────────────────────┘ │
│                                                         │
└─────────────────────────────────────────────────────────┘
         ▲
         │ Named Pipe Connection
         │ (UTF-8 text, line-delimited)
         │
┌────────┴──────────────────────────────────────────────┐
│  External Applications (Any Language/Platform)        │
│  • C# apps using HintOverlayClient                    │
│  • PowerShell scripts                                 │
│  • Python scripts                                     │
│  • C++ applications                                   │
│  • Any app that can write to named pipes             │
└───────────────────────────────────────────────────────┘
```

## Error Handling Flow

```
Client attempts to send command:
    │
    ├─ Can't connect (pipe not ready)
    │  ├─ Increment retry counter
    │  ├─ If retries < 50:
    │  │  └─ Wait 100ms and retry
    │  └─ If retries >= 50:
    │     └─ Return false
    │
    ├─ Connected successfully
    │  ├─ Send command
    │  └─ Return true
    │
    └─ Other error
       └─ Return false (no retry)

Server receives command:
    │
    ├─ Parse error
    │  └─ Log warning, continue listening
    │
    ├─ Parse successful
    │  ├─ Create NamedPipeCommand
    │  └─ Raise CommandReceived event
    │
    └─ Connection error
       └─ Log error, accept next connection
```
