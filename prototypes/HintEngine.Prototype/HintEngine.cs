// PROTOTYPE — THROWAWAY CODE. Never merges into the product.
//
// Question under test (issue #38): does the intent/effect model decided in
// "Decide: HintController decomposition and module boundaries" (#31) hold up
// ergonomically? This file is the portable part: the closed Intent/Effect
// record sets and a pure, synchronous HintEngine.Submit(Intent) -> Effects
// state machine covering the trickiest real flows of today's HintController:
//
//   - toggle while taskbar hints are active (source switching)
//   - hotkey pass-through while hints are active (don't consume the hotkey)
//   - scan timeout -> deactivate + notify
//   - selection commit -> hide overlay -> deferred activation
//   - foreground-window change -> auto-hide
//
// The engine holds all mode/debounce/filter/transition policy. It does no I/O,
// no threading, no logging. Async work (scans, timers) completes by posting
// new intents; stale completions are rejected by scan-token comparison.

namespace HintEnginePrototype;

public enum HintSource { ForegroundWindow, Taskbar }
public enum EngineMode { Inactive, Scanning, Active }
public enum ClickAction { Default, LeftClick, RightClick, DoubleClick, MouseMove, CtrlClick, ShiftClick }
public enum ScanFailure { Timeout, NoTargetWindow }

[Flags]
public enum KeyMods { None = 0, Shift = 1, Control = 2, Alt = 4 }

/// Opaque handle to a UIA element; COM types never cross this seam (#31 pt 4).
public sealed record ElementRef(string Id);
public sealed record HintRect(int X, int Y, int W, int H);

/// Core hint model: no view state (opacity lives in the renderer).
public sealed record HintItem(string Label, HintRect Rect, ElementRef Element);

public sealed record HotkeySpec(int VirtualKey, KeyMods Mods);

public sealed record EngineOptions(
    HotkeySpec Hotkey,
    HotkeySpec? TaskbarHotkey,
    int ToggleDebounceMs,
    int AutoHideMs,
    bool ClickShortcutsEnabled,
    IReadOnlyDictionary<int, ClickAction> ClickShortcutKeys)
{
    public static readonly EngineOptions Default = new(
        Hotkey: new HotkeySpec(0x48, KeyMods.Control | KeyMods.Alt),          // Ctrl+Alt+H
        TaskbarHotkey: new HotkeySpec(0x54, KeyMods.Control | KeyMods.Alt),   // Ctrl+Alt+T
        ToggleDebounceMs: 200,
        AutoHideMs: 8000,
        ClickShortcutsEnabled: true,
        ClickShortcutKeys: new Dictionary<int, ClickAction>
        {
            [0x4C] = ClickAction.LeftClick,   // Shift+L
            [0x52] = ClickAction.RightClick,  // Shift+R
            [0x44] = ClickAction.DoubleClick, // Shift+D
            [0x4D] = ClickAction.MouseMove,   // Shift+M
            [0x43] = ClickAction.CtrlClick,   // Shift+C
            [0x53] = ClickAction.ShiftClick,  // Shift+S
        });
}

// ---------------------------------------------------------------------------
// Intents — the closed set of everything that can happen TO the engine.
// The host stamps toggle intents with a timestamp at the funnel; the engine
// never reads a clock.
// ---------------------------------------------------------------------------

public abstract record Intent;
public sealed record ToggleHints(long AtMs) : Intent;
public sealed record ToggleTaskbarHints(long AtMs) : Intent;
public sealed record KeyPressed(int VirtualKey, KeyMods Mods) : Intent;
public sealed record ScanCompleted(int Token, string Window, IReadOnlyList<HintItem> Hints) : Intent;
public sealed record ScanFailed(int Token, ScanFailure Reason, int TimeoutMs) : Intent;
public sealed record ForegroundChanged(string Window) : Intent;
public sealed record AutoHideElapsed : Intent;
public sealed record DisplayChanged : Intent;
public sealed record OptionsApplied(EngineOptions Options) : Intent;

// ---------------------------------------------------------------------------
// Effects — the closed set of everything the engine can ask the host to do.
// All are fire-and-forget; none return values. ActivateElement is executed by
// posting to the message loop, so a Submit() running inside the keyboard hook
// callback returns before any activation work happens.
// ---------------------------------------------------------------------------

public abstract record Effect;
public sealed record BeginScan(int Token, HintSource Source) : Effect;
public sealed record ShowOverlay(IReadOnlyList<HintItem> Hints) : Effect;
public sealed record SetOverlayFilter(string Filter) : Effect;
public sealed record HideOverlay : Effect;
public sealed record SetInputCapture(bool On) : Effect;
public sealed record SetForegroundWatch(bool On) : Effect;
public sealed record SetAutoHideTimer(int? Ms) : Effect;      // null = stop
public sealed record SetTrayStatus(EngineMode Mode) : Effect;
public sealed record SetTrayClickAction(ClickAction Action) : Effect;
public sealed record SuppressKey : Effect;                    // hook consumes this key event
public sealed record ActivateElement(ElementRef Element, HintRect Rect, ClickAction Action) : Effect;
public sealed record Notify(string Title, string Message) : Effect;
public sealed record PlayErrorBeep : Effect;

// ---------------------------------------------------------------------------
// Mode state — a discriminated union; "which intents are legal" reads off the
// case. (Today's fourth mode, Selecting, is never entered by the shipped code
// and is dropped.)
// ---------------------------------------------------------------------------

public abstract record ModeState;
public sealed record Inactive : ModeState;
public sealed record Scanning(HintSource Source, int Token) : ModeState;
public sealed record Active(
    HintSource Source,
    string Window,                 // window hints were scanned from (for auto-hide comparison)
    IReadOnlyList<HintItem> Hints,
    string Filter,
    ClickAction PendingAction) : ModeState;

// ---------------------------------------------------------------------------
// The engine.
// ---------------------------------------------------------------------------

public sealed class HintEngine
{
    private EngineOptions _options;
    private long _lastToggleAtMs = long.MinValue;
    private int _scanCounter;

    public ModeState State { get; private set; } = new Inactive();
    public int LatestScanToken => _scanCounter;
    public EngineOptions Options => _options;

    public HintEngine(EngineOptions options) => _options = options;

    // -- Option-2 variant: derived conditions -------------------------------
    // Everything *continuous* about a session — overlay contents, hook, watch,
    // timer, tray — computed from state instead of commanded by effects. A
    // host that reconciles against this after each Submit only consumes the
    // one-shot effects (BeginScan, ActivateElement, Notify, PlayErrorBeep,
    // SuppressKey); the Set*/Show/Hide effects become redundant.

    public sealed record Conditions(
        bool OverlayVisible,
        IReadOnlyList<HintItem> Hints,
        string Filter,
        bool InputCapture,
        bool ForegroundWatch,
        int? AutoHideMs,
        EngineMode Tray,
        ClickAction TrayAction);

    public static Conditions DesiredConditions(ModeState state, EngineOptions options) => state switch
    {
        Active a => new Conditions(
            true, a.Hints, a.Filter,
            true,
            a.Source == HintSource.ForegroundWindow,
            options.AutoHideMs > 0 ? options.AutoHideMs : null,
            EngineMode.Active, a.PendingAction),
        Scanning => new Conditions(false, [], "", true, false, null, EngineMode.Scanning, ClickAction.Default),
        _ => new Conditions(false, [], "", false, false, null, EngineMode.Inactive, ClickAction.Default),
    };

    public IReadOnlyList<Effect> Submit(Intent intent) => intent switch
    {
        ToggleHints t         => OnToggle(t.AtMs, HintSource.ForegroundWindow),
        ToggleTaskbarHints t  => OnToggle(t.AtMs, HintSource.Taskbar),
        KeyPressed k          => OnKey(k),
        ScanCompleted s       => OnScanCompleted(s),
        ScanFailed s          => OnScanFailed(s),
        ForegroundChanged f   => OnForegroundChanged(f),
        AutoHideElapsed       => State is Active ? Deactivate() : None(),
        DisplayChanged        => State is Inactive ? None() : Deactivate(),
        OptionsApplied o      => OnOptionsApplied(o),
        _ => throw new ArgumentOutOfRangeException(nameof(intent)), // closed set
    };

    // -- Toggles ------------------------------------------------------------
    // One rule covers both hotkeys: toggling the source you're already showing
    // deactivates; toggling the other source (or from Inactive) starts a scan
    // for it. This replaces today's two mirrored if-trees in Toggle() /
    // ToggleTaskbar().

    private List<Effect> OnToggle(long atMs, HintSource requested)
    {
        if (_lastToggleAtMs != long.MinValue && atMs - _lastToggleAtMs < _options.ToggleDebounceMs)
            return None(); // debounced

        _lastToggleAtMs = atMs;

        var current = State switch
        {
            Scanning s => s.Source,
            Active a => a.Source,
            _ => (HintSource?)null,
        };

        if (current == requested)
            return Deactivate();

        var fx = new List<Effect>();
        if (current is not null)
        {
            // Source switch: tear down the visible session, keep input capture alive.
            fx.Add(new HideOverlay());
            fx.Add(new SetOverlayFilter(""));
            fx.Add(new SetAutoHideTimer(null));
            fx.Add(new SetForegroundWatch(false));
            // Drift bug caught by the derived-conditions variant: this reset was
            // originally missing here, leaving a stale click action on the tray
            // after Active(pending=X) -> switch. DesiredConditions can't miss it.
            fx.Add(new SetTrayClickAction(ClickAction.Default));
        }
        fx.AddRange(StartScan(requested));
        return fx;
    }

    private List<Effect> StartScan(HintSource source)
    {
        _scanCounter++; // any in-flight scan is now stale by token
        State = new Scanning(source, _scanCounter);
        return
        [
            new SetInputCapture(true),
            new SetTrayStatus(EngineMode.Scanning),
            new BeginScan(_scanCounter, source),
        ];
    }

    // -- Scan results -------------------------------------------------------

    private List<Effect> OnScanCompleted(ScanCompleted s)
    {
        if (State is not Scanning scan || scan.Token != s.Token)
            return None(); // stale scan — a toggle/switch/deactivate happened since

        if (s.Hints.Count == 0)
            return Deactivate();

        State = new Active(scan.Source, s.Window, s.Hints, Filter: "", PendingAction: ClickAction.Default);

        var fx = new List<Effect>
        {
            new ShowOverlay(s.Hints),
            new SetTrayStatus(EngineMode.Active),
        };
        if (scan.Source == HintSource.ForegroundWindow)
            fx.Add(new SetForegroundWatch(true)); // watch starts only once we know the window
        if (_options.AutoHideMs > 0)
            fx.Add(new SetAutoHideTimer(_options.AutoHideMs));
        return fx;
    }

    private List<Effect> OnScanFailed(ScanFailed s)
    {
        if (State is not Scanning scan || scan.Token != s.Token)
            return None(); // stale

        return s.Reason == ScanFailure.Timeout
            ? Deactivate(new Notify(
                "Hint Timeout",
                $"Hint population timed out after {s.TimeoutMs}ms. Try increasing the timeout in preferences."))
            : Deactivate();
    }

    // -- Keyboard -----------------------------------------------------------
    // Called synchronously from the low-level hook adapter. The hook consumes
    // the key iff the returned effects contain SuppressKey; everything else is
    // executed after the hook callback has already returned.

    private List<Effect> OnKey(KeyPressed k)
    {
        if (State is Inactive)
            return None();

        // Hotkey pass-through: never consume our own hotkeys, so the registered
        // hotkey message still arrives and drives the toggle.
        if (Matches(_options.Hotkey, k) || (_options.TaskbarHotkey is { } th && Matches(th, k)))
            return None();

        var (hints, filter) = State is Active a
            ? (a.Hints, a.Filter)
            : ((IReadOnlyList<HintItem>)[], "");

        // Shift+<key> toggles the pending click action.
        if (State is Active act && k.Mods == KeyMods.Shift && _options.ClickShortcutsEnabled
            && _options.ClickShortcutKeys.TryGetValue(k.VirtualKey, out var mapped))
        {
            var next = act.PendingAction == mapped ? ClickAction.Default : mapped;
            State = act with { PendingAction = next };
            return [new SetTrayClickAction(next), new SuppressKey()];
        }

        if (k.Mods != KeyMods.None)
            return None(); // other chords are not ours; let them through

        if (k.VirtualKey is >= 0x41 and <= 0x5A) // A-Z extends the filter
        {
            var candidate = filter + (char)k.VirtualKey;
            if (!hints.Any(h => h.Label.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)))
                return [new PlayErrorBeep(), new SuppressKey()];
            return SetFilter(candidate);
        }

        if (k.VirtualKey == 0x08) // Backspace
            return filter.Length == 0 ? [new SuppressKey()] : SetFilter(filter[..^1]);

        if (k.VirtualKey == 0x1B) // Escape: clear filter first, then exit
        {
            if (filter.Length > 0)
                return SetFilter("");
            return Deactivate(new SuppressKey());
        }

        if (k.VirtualKey is 0x20 or 0x0D) // Space / Enter commit the selection
        {
            var match = filter.Length == 0
                ? null
                : hints.FirstOrDefault(h => h.Label.Equals(filter, StringComparison.OrdinalIgnoreCase));
            if (match is null || State is not Active a2)
                return [new SuppressKey()];

            // Teardown effects come first so the overlay is gone before the
            // (message-loop-posted) activation runs.
            return Deactivate(
                new ActivateElement(match.Element, match.Rect, a2.PendingAction),
                new SuppressKey());
        }

        return None(); // not ours; let it through
    }

    private List<Effect> SetFilter(string filter)
    {
        if (State is Active a)
            State = a with { Filter = filter };
        return [new SetOverlayFilter(filter), new SuppressKey()];
    }

    private static bool Matches(HotkeySpec spec, KeyPressed k) =>
        k.VirtualKey == spec.VirtualKey && k.Mods == spec.Mods;

    // -- Environment --------------------------------------------------------

    private List<Effect> OnForegroundChanged(ForegroundChanged f)
    {
        if (State is Active a && a.Source == HintSource.ForegroundWindow && f.Window != a.Window)
            return Deactivate();
        return None();
    }

    private List<Effect> OnOptionsApplied(OptionsApplied o)
    {
        _options = o.Options; // hotkey (re)registration is the host's job
        return None();
    }

    // -- Shared teardown ----------------------------------------------------

    private List<Effect> Deactivate(params Effect[] tail)
    {
        State = new Inactive();
        var fx = new List<Effect>
        {
            new HideOverlay(),
            new SetOverlayFilter(""),
            new SetInputCapture(false),
            new SetForegroundWatch(false),
            new SetAutoHideTimer(null),
            new SetTrayStatus(EngineMode.Inactive),
            new SetTrayClickAction(ClickAction.Default),
        };
        fx.AddRange(tail);
        return fx;
    }

    private static List<Effect> None() => [];
}
