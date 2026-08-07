// PROTOTYPE — THROWAWAY TUI SHELL. Drives HintEngine by hand.
//
// The TUI plays the host: it stamps a fake clock, executes effects against
// fake adapters (overlay, tray, hook, timers, scanner), and lets you complete
// scans / fire timers / switch foreground windows manually so you can
// interleave them with toggles in ways that are hard to reason about on paper.

using HintEnginePrototype;

const string Bold = "\x1b[1m";
const string Dim = "\x1b[2m";
const string Reset = "\x1b[0m";

var engine = new HintEngine(EngineOptions.Default);
var host = new FakeHost();
long clock = 0;

// Two host styles under comparison ([m] toggles live):
//   effects mode — the host executes every effect in the batch (decided model)
//   derived mode — the host executes only one-shot effects, then reconciles
//                  the continuous conditions (overlay/hook/watch/timer/tray)
//                  against HintEngine.DesiredConditions(state)
bool derivedMode = args.Contains("--derived");

// `dotnet run -- --demo [--derived]` scripts the five ticket flows and prints the trace.
if (args.Contains("--demo"))
{
    RunDemo();
    return;
}

Render();

while (true)
{
    var key = Console.ReadKey(intercept: true);
    clock += 40; // every keypress advances the fake clock a little

    if (key.Modifiers == 0)
    {
        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case 'q':
                Console.Clear();
                return;
            case '.':
                clock += 300;
                host.Note($"clock advanced to {clock}ms");
                Render();
                continue;
            case 'h': // press the main hotkey (Ctrl+Alt+H)
                PressHotkey(0x48, new ToggleHints(clock));
                Render();
                continue;
            case 't': // press the taskbar hotkey (Ctrl+Alt+T)
                PressHotkey(0x54, new ToggleTaskbarHints(clock));
                Render();
                continue;
            case 'c':
                CompleteOldestScan(scan => new ScanCompleted(scan.Token, host.ForegroundWindow, FakeHints(scan.Source)));
                Render();
                continue;
            case 'e':
                CompleteOldestScan(scan => new ScanCompleted(scan.Token, host.ForegroundWindow, []));
                Render();
                continue;
            case 'x':
                CompleteOldestScan(scan => new ScanFailed(scan.Token, ScanFailure.Timeout, 2000));
                Render();
                continue;
            case 'f':
                host.SwitchForegroundWindow();
                if (host.ForegroundWatch)
                    Submit(new ForegroundChanged(host.ForegroundWindow));
                else
                    host.Note($"foreground -> {host.ForegroundWindow} (watch off: no intent posted)");
                Render();
                continue;
            case 'w':
                // The timer adapter may race a deactivation; the engine ignores a stale fire.
                Submit(new AutoHideElapsed());
                Render();
                continue;
            case 'g':
                host.PumpMessageLoop();
                Render();
                continue;
            case 'm':
                derivedMode = !derivedMode;
                host.Note($"host switched to {(derivedMode ? "DERIVED" : "EFFECTS")} mode");
                Render();
                continue;
        }
    }

    // Everything else is a real keystroke as the low-level hook would see it.
    var vk = VirtualKeyOf(key);
    if (vk is null)
        continue;
    if (!host.InputCapture)
    {
        host.Note($"key '{KeyName(vk.Value, Mods(key))}' ignored (hook not running)");
        Render();
        continue;
    }
    Submit(new KeyPressed(vk.Value, Mods(key)));
    Render();
}

// -- scripted demo of the five ticket flows ----------------------------------

void RunDemo()
{
    int logSeen = 0, notesSeen = 0;

    void Flush()
    {
        foreach (var e in host.Log.Skip(logSeen))
        {
            Console.WriteLine($"-> {e.Intent}");
            Console.WriteLine($"   {string.Join(", ", e.EffectNames)}");
        }
        logSeen = host.Log.Count;
        foreach (var n in host.Notes.Skip(notesSeen))
            Console.WriteLine($" * {n}");
        notesSeen = host.Notes.Count;
        Console.WriteLine($"   ENGINE: {FmtState()}");
        Console.WriteLine();
    }

    string FmtState() => engine.State switch
    {
        Scanning s => $"Scanning({s.Source}, scan #{s.Token})",
        Active a => $"Active({a.Source}, window={a.Window}, filter=\"{a.Filter}\", pending={a.PendingAction}, {a.Hints.Count} hints)",
        _ => "Inactive",
    };

    void Say(string s) => Console.WriteLine($"===== {s} =====");
    void CompleteScan() =>
        CompleteOldestScan(s => new ScanCompleted(s.Token, host.ForegroundWindow, FakeHints(s.Source)));

    Say("Flow 3: scan timeout -> deactivate + notify");
    clock += 300; PressHotkey(0x48, new ToggleHints(clock)); Flush();
    CompleteOldestScan(s => new ScanFailed(s.Token, ScanFailure.Timeout, 2000)); Flush();

    Say("Flow 1: toggle while taskbar hints active (source switch) + stale-scan rejection");
    clock += 300; PressHotkey(0x54, new ToggleTaskbarHints(clock)); Flush();
    CompleteScan(); Flush();
    clock += 300; PressHotkey(0x48, new ToggleHints(clock)); Flush();          // switch: taskbar -> FG scan
    clock += 300; PressHotkey(0x54, new ToggleTaskbarHints(clock)); Flush();   // switch again before scan completes
    CompleteScan(); Flush();                                                   // older scan lands: stale, ignored
    CompleteScan(); Flush();                                                   // live scan lands: taskbar hints show

    Say("Flow 2: hotkey pass-through while active (hook does not consume; toggle still arrives)");
    clock += 300; PressHotkey(0x54, new ToggleTaskbarHints(clock)); Flush();   // taskbar active -> toggles off

    Say("Flow 4: selection commit -> hide overlay -> deferred activation");
    clock += 300; PressHotkey(0x48, new ToggleHints(clock)); Flush();
    CompleteScan(); Flush();
    Submit(new KeyPressed(0x4A, KeyMods.None)); Flush();                       // 'j'
    Submit(new KeyPressed(0x4B, KeyMods.None)); Flush();                       // 'k' -> filter "JK"
    Submit(new KeyPressed(0x0D, KeyMods.None)); Flush();                       // Enter commits
    host.PumpMessageLoop(); Flush();

    Say("Flow 5: foreground-window change -> auto-hide");
    clock += 300; PressHotkey(0x48, new ToggleHints(clock)); Flush();
    CompleteScan(); Flush();
    host.SwitchForegroundWindow();
    Submit(new ForegroundChanged(host.ForegroundWindow)); Flush();

    Say("Extras: debounced double-press, stale auto-hide fire");
    clock += 300; PressHotkey(0x48, new ToggleHints(clock)); Flush();
    clock += 40; PressHotkey(0x48, new ToggleHints(clock)); Flush();           // 40ms later: debounced
    Submit(new AutoHideElapsed()); Flush();                                    // no timer armed: ignored
}

// -- host <-> engine plumbing ------------------------------------------------

void PressHotkey(int vk, Intent toggle)
{
    // Real sequence: the hook sees the chord first (and must not consume it),
    // then the registered-hotkey message arrives and drives the toggle.
    if (host.InputCapture)
        Submit(new KeyPressed(vk, KeyMods.Control | KeyMods.Alt));
    Submit(toggle);
}

void CompleteOldestScan(Func<(int Token, HintSource Source), Intent> makeIntent)
{
    if (host.InFlightScans.Count == 0)
    {
        host.Note("no scan in flight");
        return;
    }
    var scan = host.InFlightScans[0];
    host.InFlightScans.RemoveAt(0);
    Submit(makeIntent(scan));
}

void Submit(Intent intent)
{
    var effects = engine.Submit(intent);
    var applied = derivedMode ? effects.Where(IsOneShot).ToList() : effects.ToList();

    var entry = new LogEntry(FmtIntent(intent), applied.Select(FmtEffect).ToList());
    foreach (var fx in applied)
        host.Apply(fx);

    if (derivedMode)
    {
        var changes = host.Reconcile(HintEngine.DesiredConditions(engine.State, engine.Options));
        entry.EffectNames.AddRange(changes.Count == 0
            ? ["~ reconcile: state unchanged"]
            : changes.Select(c => $"~ {c}"));
    }

    if (intent is KeyPressed && !effects.Any(e => e is SuppressKey))
        entry.EffectNames.Add("(no SuppressKey — key passes through to the OS)");
    else if (effects.Count == 0 && !derivedMode)
        entry.EffectNames.Add("(no effects — ignored)");
    host.Log.Add(entry);
}

static bool IsOneShot(Effect e) =>
    e is BeginScan or SuppressKey or ActivateElement or Notify or PlayErrorBeep;

static IReadOnlyList<HintItem> FakeHints(HintSource source) => source switch
{
    HintSource.ForegroundWindow => Hints("JJ", "JK", "JL", "KJ", "KK", "KL"),
    _ => Hints("J", "K", "L", "M", "N"),
};

static IReadOnlyList<HintItem> Hints(params string[] labels) =>
    labels.Select((l, i) => new HintItem(l, new HintRect(100 + i * 60, 200, 50, 22), new ElementRef($"elem-{l}")))
          .ToList();

static int? VirtualKeyOf(ConsoleKeyInfo key) => key.Key switch
{
    >= ConsoleKey.A and <= ConsoleKey.Z => (int)key.Key,
    ConsoleKey.Backspace => 0x08,
    ConsoleKey.Escape => 0x1B,
    ConsoleKey.Enter => 0x0D,
    ConsoleKey.Spacebar => 0x20,
    _ => null,
};

static KeyMods Mods(ConsoleKeyInfo key) =>
    (key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? KeyMods.Shift : 0)
    | (key.Modifiers.HasFlag(ConsoleModifiers.Control) ? KeyMods.Control : 0)
    | (key.Modifiers.HasFlag(ConsoleModifiers.Alt) ? KeyMods.Alt : 0);

static string KeyName(int vk, KeyMods mods)
{
    var parts = new List<string>();
    if (mods.HasFlag(KeyMods.Control)) parts.Add("Ctrl");
    if (mods.HasFlag(KeyMods.Alt)) parts.Add("Alt");
    if (mods.HasFlag(KeyMods.Shift)) parts.Add("Shift");
    parts.Add(vk switch
    {
        0x08 => "Bksp", 0x1B => "Esc", 0x0D => "Enter", 0x20 => "Space",
        _ => ((char)vk).ToString(),
    });
    return string.Join("+", parts);
}

static string FmtIntent(Intent i) => i switch
{
    ToggleHints t => $"ToggleHints @{t.AtMs}ms",
    ToggleTaskbarHints t => $"ToggleTaskbarHints @{t.AtMs}ms",
    KeyPressed k => $"KeyPressed({KeyName(k.VirtualKey, k.Mods)})",
    ScanCompleted s => $"ScanCompleted(#{s.Token}, {s.Window}, {s.Hints.Count} hints)",
    ScanFailed s => $"ScanFailed(#{s.Token}, {s.Reason})",
    ForegroundChanged f => $"ForegroundChanged({f.Window})",
    AutoHideElapsed => "AutoHideElapsed",
    DisplayChanged => "DisplayChanged",
    OptionsApplied => "OptionsApplied",
    _ => i.ToString()!,
};

static string FmtEffect(Effect e) => e switch
{
    BeginScan b => $"BeginScan(#{b.Token}, {b.Source})",
    ShowOverlay s => $"ShowOverlay({s.Hints.Count} hints)",
    SetOverlayFilter f => $"SetOverlayFilter(\"{f.Filter}\")",
    HideOverlay => "HideOverlay",
    SetInputCapture c => $"SetInputCapture({(c.On ? "on" : "off")})",
    SetForegroundWatch w => $"SetForegroundWatch({(w.On ? "on" : "off")})",
    SetAutoHideTimer t => t.Ms is null ? "SetAutoHideTimer(stop)" : $"SetAutoHideTimer({t.Ms}ms)",
    SetTrayStatus s => $"SetTrayStatus({s.Mode})",
    SetTrayClickAction a => $"SetTrayClickAction({a.Action})",
    SuppressKey => "SuppressKey",
    ActivateElement a => $"ActivateElement({a.Element.Id}, {a.Action}) -> posted to message loop",
    Notify n => $"Notify(\"{n.Title}\")",
    PlayErrorBeep => "PlayErrorBeep",
    _ => e.ToString()!,
};

void Render()
{
    Console.Clear();
    Action<string> w = Console.Write;

    w($"{Bold}HINTENGINE PROTOTYPE{Reset} {Dim}— intent/effect model under test (issue #38). Throwaway.{Reset}\n");
    w($"{Dim}clock{Reset} {clock}ms   {Dim}foreground window{Reset} {host.ForegroundWindow}   ");
    w(derivedMode
        ? $"{Bold}DERIVED mode{Reset} {Dim}(one-shot effects + state reconcile){Reset}\n\n"
        : $"{Bold}EFFECTS mode{Reset} {Dim}(host executes full effect batch){Reset}\n\n");

    // Engine state
    w($"{Bold}ENGINE{Reset}  ");
    switch (engine.State)
    {
        case Inactive:
            w($"Inactive\n");
            break;
        case Scanning s:
            w($"Scanning({s.Source}, scan #{s.Token})\n");
            break;
        case Active a:
            w($"Active({a.Source}) {Dim}window{Reset} {a.Window}  {Dim}filter{Reset} \"{a.Filter}\"  {Dim}pending action{Reset} {a.PendingAction}\n");
            break;
    }

    // Overlay (renderer derives dimming from the filter — no opacity in the model)
    w($"{Bold}OVERLAY{Reset} ");
    if (!host.OverlayVisible)
        w($"{Dim}hidden{Reset}\n");
    else
    {
        foreach (var h in host.OverlayHints)
        {
            bool lit = host.OverlayFilter.Length == 0
                       || h.Label.StartsWith(host.OverlayFilter, StringComparison.OrdinalIgnoreCase);
            w(lit ? $"{Bold}[{h.Label}]{Reset} " : $"{Dim}[{h.Label}]{Reset} ");
        }
        w("\n");
    }

    // Host adapters
    w($"{Bold}HOST{Reset}    ");
    w($"{Dim}hook{Reset} {(host.InputCapture ? "ON " : "off")}  ");
    w($"{Dim}fg-watch{Reset} {(host.ForegroundWatch ? "ON " : "off")}  ");
    w($"{Dim}auto-hide{Reset} {(host.AutoHideMs is { } ms ? $"{ms}ms armed" : "off")}  ");
    w($"{Dim}tray{Reset} {host.TrayStatus}/{host.TrayClickAction}\n");
    w($"        {Dim}in-flight scans{Reset} ");
    w(host.InFlightScans.Count == 0
        ? "none"
        : string.Join("  ", host.InFlightScans.Select(s => $"#{s.Token}({s.Source})")));
    w($"   {Dim}posted activations{Reset} {host.PendingActivations.Count}");
    w(host.PendingActivations.Count > 0 ? $"  {Bold}[g] pumps the message loop{Reset}\n" : "\n");
    if (host.Toast is not null)
        w($"        {Bold}TOAST{Reset} {host.Toast}\n");

    // Log
    w($"\n{Bold}LOG{Reset} {Dim}(newest last){Reset}\n");
    foreach (var entry in host.Log.TakeLast(6))
    {
        w($"  {Bold}-> {entry.Intent}{Reset}\n");
        w($"     {Dim}{string.Join(", ", entry.EffectNames)}{Reset}\n");
    }
    foreach (var note in host.Notes.TakeLast(2))
        w($"  {Dim}* {note}{Reset}\n");

    // Keys
    w($"\n{Bold}[h]{Reset}{Dim} main hotkey {Reset}{Bold}[t]{Reset}{Dim} taskbar hotkey {Reset}{Bold}[c]{Reset}{Dim} scan completes {Reset}{Bold}[e]{Reset}{Dim} scan empty {Reset}{Bold}[x]{Reset}{Dim} scan times out{Reset}\n");
    w($"{Bold}[f]{Reset}{Dim} foreground changes {Reset}{Bold}[w]{Reset}{Dim} auto-hide fires {Reset}{Bold}[g]{Reset}{Dim} pump message loop {Reset}{Bold}[m]{Reset}{Dim} effects/derived {Reset}{Bold}[.]{Reset}{Dim} +300ms {Reset}{Bold}[q]{Reset}{Dim} quit{Reset}\n");
    w($"{Dim}hint keys go to the engine: {Reset}j k l{Dim}, Bksp, Esc, Enter/Space, Shift+L/R/D/M/C/S = click action{Reset}\n");
}

sealed record LogEntry(string Intent, List<string> EffectNames);

// The fake host: executes effects against in-memory adapters.
sealed class FakeHost
{
    public bool InputCapture;
    public bool ForegroundWatch;
    public int? AutoHideMs;
    public bool OverlayVisible;
    public IReadOnlyList<HintItem> OverlayHints = [];
    public string OverlayFilter = "";
    public EngineMode TrayStatus = EngineMode.Inactive;
    public ClickAction TrayClickAction = ClickAction.Default;
    public string? Toast;
    public string ForegroundWindow = "win-1";
    public List<(int Token, HintSource Source)> InFlightScans = [];
    public List<ActivateElement> PendingActivations = [];
    public List<LogEntry> Log = [];
    public List<string> Notes = [];

    private int _windowCounter = 1;

    public void SwitchForegroundWindow() => ForegroundWindow = $"win-{++_windowCounter}";

    public void Note(string text) => Notes.Add(text);

    public void PumpMessageLoop()
    {
        if (PendingActivations.Count == 0)
        {
            Note("message loop: nothing pending");
            return;
        }
        foreach (var a in PendingActivations)
            Note($"message loop: activated {a.Element.Id} ({a.Action}) — after the hook returned");
        PendingActivations.Clear();
    }

    // Derived mode: diff the continuous conditions against what's currently
    // running, apply the deltas, and report only what actually changed.
    public List<string> Reconcile(HintEngine.Conditions d)
    {
        var changes = new List<string>();
        if (OverlayVisible != d.OverlayVisible || (d.OverlayVisible && !ReferenceEquals(OverlayHints, d.Hints)))
        {
            changes.Add(d.OverlayVisible ? $"overlay: show {d.Hints.Count} hints" : "overlay: hide");
            OverlayVisible = d.OverlayVisible;
            OverlayHints = d.Hints;
        }
        if (OverlayFilter != d.Filter)
        {
            changes.Add($"filter: \"{OverlayFilter}\" -> \"{d.Filter}\"");
            OverlayFilter = d.Filter;
        }
        if (InputCapture != d.InputCapture)
        {
            changes.Add($"hook: {OnOff(InputCapture)} -> {OnOff(d.InputCapture)}");
            InputCapture = d.InputCapture;
        }
        if (ForegroundWatch != d.ForegroundWatch)
        {
            changes.Add($"fg-watch: {OnOff(ForegroundWatch)} -> {OnOff(d.ForegroundWatch)}");
            ForegroundWatch = d.ForegroundWatch;
        }
        if (AutoHideMs != d.AutoHideMs)
        {
            changes.Add($"auto-hide: {(AutoHideMs is { } a ? $"{a}ms" : "off")} -> {(d.AutoHideMs is { } b ? $"{b}ms" : "off")}");
            AutoHideMs = d.AutoHideMs;
        }
        if (TrayStatus != d.Tray)
        {
            changes.Add($"tray: {TrayStatus} -> {d.Tray}");
            TrayStatus = d.Tray;
        }
        if (TrayClickAction != d.TrayAction)
        {
            changes.Add($"tray action: {TrayClickAction} -> {d.TrayAction}");
            TrayClickAction = d.TrayAction;
        }
        return changes;

        static string OnOff(bool b) => b ? "on" : "off";
    }

    public void Apply(Effect fx)
    {
        switch (fx)
        {
            case BeginScan b: InFlightScans.Add((b.Token, b.Source)); break;
            case ShowOverlay s: OverlayVisible = true; OverlayHints = s.Hints; break;
            case SetOverlayFilter f: OverlayFilter = f.Filter; break;
            case HideOverlay: OverlayVisible = false; OverlayHints = []; break;
            case SetInputCapture c: InputCapture = c.On; break;
            case SetForegroundWatch wt: ForegroundWatch = wt.On; break;
            case SetAutoHideTimer t: AutoHideMs = t.Ms; break;
            case SetTrayStatus s: TrayStatus = s.Mode; break;
            case SetTrayClickAction a: TrayClickAction = a.Action; break;
            case SuppressKey: break; // the hook adapter reads this from the batch
            case ActivateElement a: PendingActivations.Add(a); break; // BeginInvoke equivalent
            case Notify n: Toast = $"{n.Title}: {n.Message}"; break;
            case PlayErrorBeep: Note("BEEP (invalid hint letter, consumed)"); break;
        }
    }
}
