// =============================================================================
// PROTOTYPE — Taskbar ambient numbers + ADOPTED Taskbar session   (#81 reframe, #108)
// =============================================================================
// THROWAWAY. Lives only on branch `prototype/taskbar-ambient-csharp` (off
// `prototype/taskbar-overlay-probe`). Do NOT ship, do NOT merge to main.
// Companion to the HTML logic demo on `prototype/taskbar-ambient-logic`
// (docs/architecture/prototypes/PROTOTYPE-taskbar-ambient-layer.html).
//
// WHAT IT ANSWERS (on a real Win11 taskbar, driven by hand or by Talon):
//   1. Ambient layer: do identity-keyed numbers (labels owned by RuntimeId, not
//      assigned per scan) stay put on the real taskbar as apps open/close, the
//      app row reflows, and the display changes? How bad is churn per policy
//      (fresh / reuse / positional)?
//   2. Adopted session: can the SAME non-activating numbers window take focus
//      on the taskbar shortcut, add [Active] to its title, and handle the keys
//      itself (no low-level hook), fast enough that Talon's
//      "hotkey, digits, Enter" burst is not lost to the previous app?
//   3. Held updates: does deferring reconciles while adopted (freeze=all) feel
//      right on the real taskbar, versus freeze=positions (badges follow their
//      buttons, numbers frozen)? Is the held update applied exactly once?
//
// MECHANISM (reused from Probe 2a, TaskbarOverlayProbe2a.cs — not re-derived):
//   triggers = UIA StructureChanged (Subtree on Shell_TrayWnd) + container
//   BoundingRectangle + WinEvents on explorer + RegNotify on Explorer\Advanced
//   + SystemEvents.DisplaySettingsChanged; sender-identity filter; debounce;
//   full re-enumerate; event-gated settle loop. NO content poll.
//
// NEW HERE:
//   * AmbientLabelBook — the liftable bit: label <-> RuntimeId map with a
//     numbering policy. Survivors keep labels; new identities get fresh ones;
//     a RuntimeId that blinks out and returns reclaims its old number if free.
//   * AmbientNumbersForm — one passive window sized to the taskbar strip
//     (WS_EX_NOACTIVATE | TRANSPARENT | LAYERED | TOPMOST | TOOLWINDOW). On the
//     shortcut it drops NOACTIVATE, takes the foreground, and reads keys itself.
//   * Adopted-session controller on the STA thread: buffers keys until any
//     pending reconcile has been applied, no typed-prefix filtering (nothing
//     repaints while you type), Enter activates, Esc restores the previous
//     foreground window, focus loss ends the session.
//
// RUN:  Windows-Hinting.exe --prototype-taskbar-ambient [--numbering=positional|fresh|reuse] [--freeze=all|positions]
//   Default numbering = positional (owner, 2026-10-16: numbers always read left to right).
//   Close the real Windows-Hinting first: this prototype registers the app's
//   default taskbar hotkey (Ctrl+Alt+T) so existing Talon commands drive it.
//   Global hotkeys:
//     Ctrl+Alt+T          taskbar shortcut: adopt the numbers (again = dismiss);
//                         falls back to Ctrl+Alt+Shift+T if the real app holds it
//     Ctrl+Alt+Shift+N    cycle numbering policy (resets the map)
//     Ctrl+Alt+Shift+F    toggle freeze mode (all <-> positions)
//     Ctrl+Alt+Shift+R    force a reconcile
//     Ctrl+Alt+Shift+Q    quit
//   While adopted (numbers window has focus): digits, Backspace, Enter/Space, Esc.
//   Transcript: taskbar-ambient-<timestamp>.log next to the exe.
// =============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using UIAutomationClient;
using Windows.Win32;

namespace WindowsHinting
{
    internal enum NumberingPolicy { Fresh, Reuse, Positional }

    /// <summary>
    /// PROTOTYPE. The engine-owned label &lt;-&gt; identity map. Pure: no UIA, no UI.
    /// Mirrors the reducer in the HTML logic demo.
    /// </summary>
    internal sealed class AmbientLabelBook
    {
        public NumberingPolicy Policy { get; private set; }
        private Dictionary<string, string> _labelByRid = new();
        private readonly Dictionary<string, string> _nameByRid = new();
        private readonly Dictionary<string, (string rid, string name, long at)> _retiredByLabel = new();
        private readonly Dictionary<string, string> _lastLabelByRid = new(); // for reclaim
        private int _nextFresh = 1;

        public AmbientLabelBook(NumberingPolicy policy) { Policy = policy; }

        public IReadOnlyDictionary<string, string> LabelByRid => _labelByRid;

        public void Reset(NumberingPolicy policy)
        {
            Policy = policy;
            _labelByRid = new();
            _nameByRid.Clear();
            _retiredByLabel.Clear();
            _lastLabelByRid.Clear();
            _nextFresh = 1;
        }

        public sealed record Result(Dictionary<string, string> LabelByRid, List<string> Changes, List<string> Warnings);

        /// <param name="order">Current elements in reading order (left to right).</param>
        public Result Reconcile(IReadOnlyList<(string rid, string name)> order, long nowMs)
        {
            var prev = _labelByRid;
            var labels = new Dictionary<string, string>();
            var changes = new List<string>();
            var warnings = new List<string>();

            if (Policy == NumberingPolicy.Positional)
            {
                for (int i = 0; i < order.Count; i++)
                    labels[order[i].rid] = (i + 1).ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                foreach (var (rid, _) in order)
                    if (prev.TryGetValue(rid, out var l)) labels[rid] = l;
                var used = new HashSet<string>(labels.Values);

                foreach (var (rid, name) in order)
                {
                    if (labels.ContainsKey(rid)) continue;
                    string l;
                    // A RuntimeId that blinked out (transient empty read, reflow) and came
                    // back gets its old number back if nobody took it meanwhile.
                    if (_lastLabelByRid.TryGetValue(rid, out var old) && !used.Contains(old))
                    {
                        l = old;
                        changes.Add($"{name} came back as {l}");
                    }
                    else if (Policy == NumberingPolicy.Reuse)
                    {
                        int n = 1;
                        while (used.Contains(n.ToString(CultureInfo.InvariantCulture))) n++;
                        l = n.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        l = (_nextFresh++).ToString(CultureInfo.InvariantCulture);
                    }
                    used.Add(l);
                    labels[rid] = l;
                }
                int max = labels.Values.Select(v => int.Parse(v, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
                _nextFresh = Math.Max(_nextFresh, max + 1);
            }

            // Removals.
            foreach (var (rid, l) in prev)
            {
                if (labels.ContainsKey(rid)) continue;
                string name = _nameByRid.GetValueOrDefault(rid, rid);
                changes.Add($"{name} gone (was {l})");
                _retiredByLabel[l] = (rid, name, nowMs);
            }

            // Additions, renumbers, number-now-means-something-else hazards.
            var prevOwner = prev.ToDictionary(kv => kv.Value, kv => kv.Key);
            foreach (var (rid, name) in order)
            {
                string l = labels[rid];
                if (!prev.TryGetValue(rid, out var was)) { if (!changes.Contains($"{name} came back as {l}")) changes.Add($"{name} added as {l}"); }
                else if (was != l) changes.Add($"{name} renumbered {was} -> {l}");

                string? beforeName = null; long beforeAt = nowMs;
                if (prevOwner.TryGetValue(l, out var ownerRid) && ownerRid != rid)
                    beforeName = _nameByRid.GetValueOrDefault(ownerRid, ownerRid);
                else if (!prev.ContainsKey(rid) && _retiredByLabel.TryGetValue(l, out var ret) && ret.rid != rid)
                { beforeName = ret.name; beforeAt = ret.at; }
                if (beforeName != null && prev.Count > 0)
                    warnings.Add($"Number {l} now means {name}; it meant {beforeName} {(nowMs - beforeAt)} ms ago.");
            }

            foreach (var (rid, name) in order) _nameByRid[rid] = name;
            foreach (var (rid, l) in labels) _lastLabelByRid[rid] = l;
            _labelByRid = labels;
            return new Result(labels, changes, warnings);
        }
    }

    /// <summary>PROTOTYPE. See file header.</summary>
    internal static class TaskbarAmbientPrototype
    {
        // ---- tunables -------------------------------------------------------------
        private const int DebounceQuietMs = 150;
        private const int DisplaySettleMs = 650;
        private const int SettlePollMs = 180;
        private const int SettleCapMs = 1800;
        private const int DisplaySettleCapMs = 3000;
        private const float LabelFontScale = 1.8f;

        private const int CT_BUTTON = 50000, CT_TOOLTIP = 50022, CT_MENU = 50009, CT_MENUITEM = 50011;
        private static readonly string[] CONTAINER_CLASSES = { "TrayNotifyWnd", "Shell_TrayWnd", "MSTaskSwWClass" };
        private static readonly string[] IGNORE_CLASSES = { "Xaml_WindowedPopupClass", "PopupRoot", "PopupHost", "DragVisualWnd" };

        // ---- shared state ---------------------------------------------------------
        private static readonly object LogGate = new();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static StreamWriter? _logFile;
        private static AmbientNumbersForm? _form;

        private static readonly AutoResetEvent _wake = new(false);
        private static readonly ManualResetEventSlim _shutdown = new(false);
        private static volatile bool _reconcilePending;
        private static volatile bool _displayPending;
        private static volatile bool _manualReconcile;
        private static volatile bool _resetBook;
        private static NumberingPolicy _pendingPolicy;
        private static long _lastReqMs;

        // Adopted-session flags shared with the worker.
        private static volatile bool _adopted;          // STA sets; worker reads
        private static volatile bool _flushRequested;   // STA -> worker: apply pending before adopting
        private static volatile bool _sessionEnded;     // STA -> worker: apply held update once
        private static volatile bool _heldPending;      // worker: an update was held while adopted
        private static volatile bool _freezePositions;  // false = freeze all (decided #108 rule 14)
        private static int _heldAppliedCount;

        private static readonly ConcurrentQueue<string> _activateQueue = new();

        // Worker-owned (MTA): book + current snapshot.
        private static AmbientLabelBook _book = new(NumberingPolicy.Fresh);
        private static readonly object _setGate = new();
        private static Dictionary<string, IUIAutomationElement> _elementByLabel = new();
        private static Dictionary<string, string> _nameByLabel = new();
        private static List<(string rid, string label, Rectangle rect)> _shown = new();

        // ---- entry ------------------------------------------------------------------
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string key, string dflt) =>
                args.FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))?.Substring(key.Length + 1) ?? dflt;
            var policy = Arg("--numbering", "positional").ToLowerInvariant() switch
            {
                "reuse" => NumberingPolicy.Reuse,
                "fresh" => NumberingPolicy.Fresh,
                _ => NumberingPolicy.Positional, // owner 2026-10-16: numbers always read left to right

            };
            _book = new AmbientLabelBook(policy);
            _freezePositions = Arg("--freeze", "all").Equals("positions", StringComparison.OrdinalIgnoreCase);

            PInvoke.AllocConsole();
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true });
            try { Console.Title = "PROTOTYPE — taskbar ambient numbers"; } catch { }
            string logPath = Path.Combine(AppContext.BaseDirectory, $"taskbar-ambient-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _logFile = new StreamWriter(logPath, append: false) { AutoFlush = true };

            Log("=== PROTOTYPE: taskbar ambient numbers + adopted session ===");
            Log($"log file: {logPath}");
            Log($"OS: {Environment.OSVersion}  numbering={policy}  freeze={(_freezePositions ? "positions" : "all")}");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _form = new AmbientNumbersForm(LabelFontScale);
            _form.KeyIntent += OnKeyIntent;
            _form.FocusLost += () => EndSession(_activationInFlight ? "activation (target took focus)" : "focus lost");
            _form.Show();

            using var ready = new ManualResetEventSlim(false);
            Exception? startupError = null;
            var worker = new Thread(() => WorkerMain(ready, ref startupError)) { Name = "Ambient-UIA-MTA", IsBackground = true };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            ready.Wait();
            if (startupError != null)
            {
                Log($"!! STARTUP FAILED: {startupError.Message}");
                Thread.Sleep(5000);
                return;
            }

            InstallWinEventHook();
            var hotkeys = new HotkeyWindow();
            var regWatch = new Thread(RegWatchMain) { Name = "Ambient-RegWatch", IsBackground = true };
            regWatch.Start();

            Log(">>> READY. Ctrl+Alt+T = adopt, digits + Enter = activate, Esc = back. Ctrl+Alt+Shift+N/F/R/Q = numbering/freeze/reconcile/quit. <<<");
            Application.Run();

            Log("Stopping...");
            hotkeys.Teardown();
            RemoveWinEventHook();
            _shutdown.Set();
            _wake.Set();
            worker.Join(3000);
            try { _form.Dispose(); } catch { }
            Log($"=== ENDED (held updates applied at session end: {_heldAppliedCount}) ===");
            _logFile.Dispose();
            try { PInvoke.FreeConsole(); } catch { }
        }

        // =============================================================================
        // STA: adopted-session controller
        // =============================================================================

        private static bool _session;          // a Taskbar session has adopted the window
        private static bool _sessionReady;     // pending reconcile applied; keys can be processed
        private static string _typed = "";
        private static readonly Queue<string> _keyBuffer = new();
        private static IntPtr _prevForeground;
        private static long _tShortcut;
        private static bool _firstKeyLogged;
        private static bool _activationInFlight;

        private static void OnTaskbarShortcut()
        {
            if (_session) { EndSession("taskbar shortcut again = dismiss", restoreFocus: true); return; }

            _tShortcut = Clock.ElapsedMilliseconds;
            _prevForeground = GetForegroundWindow();
            _typed = "";
            _keyBuffer.Clear();
            _firstKeyLogged = false;
            _session = true;
            _adopted = true;

            bool pending = _reconcilePending || _inSettleLoop;
            _sessionReady = !pending;
            if (pending) { _flushRequested = true; _wake.Set(); }

            bool fg = _form!.Adopt();
            long dt = Clock.ElapsedMilliseconds - _tShortcut;
            Log($"[SESSION] ADOPT: SetForegroundWindow={fg} in {dt}ms; foreground now ours={GetForegroundWindow() == _form.Handle}; " +
                $"prev=0x{_prevForeground.ToInt64():X}; pending reconcile={(pending ? "yes -> flushing before keys" : "no")}");
            if (!fg) Log("[SESSION] !! could not take the foreground — keys will go to the previous app.");
        }

        private static void OnFlushDone()
        {
            if (!_session) return;
            _sessionReady = true;
            Log($"[SESSION] pending reconcile applied +{Clock.ElapsedMilliseconds - _tShortcut}ms after the shortcut; replaying {_keyBuffer.Count} buffered key(s).");
            while (_session && _keyBuffer.Count > 0) HandleKey(_keyBuffer.Dequeue());
        }

        private static void OnKeyIntent(string key)
        {
            if (!_session) return;
            if (!_firstKeyLogged)
            {
                _firstKeyLogged = true;
                Log($"[SESSION] first key '{key}' reached our window +{Clock.ElapsedMilliseconds - _tShortcut}ms after the shortcut.");
            }
            if (!_sessionReady) { _keyBuffer.Enqueue(key); return; }
            HandleKey(key);
        }

        private static void HandleKey(string key)
        {
            switch (key)
            {
                case "Esc":
                    EndSession("Esc", restoreFocus: true);
                    return;
                case "Back":
                    if (_typed.Length > 0) _typed = _typed[..^1];
                    Log($"[SESSION] typed '{_typed}'");
                    return;
                case "Enter":
                    if (_activationInFlight) return;
                    if (_typed.Length == 0 || !_form!.HasLabel(_typed))
                    {
                        Log($"[SESSION] Enter with '{_typed}': no such number. Beep.");
                        System.Media.SystemSounds.Beep.Play();
                        _typed = "";
                        return;
                    }
                    _activationInFlight = true;
                    _activateQueue.Enqueue(_typed);
                    _wake.Set();
                    return;
                default: // a digit
                    string cand = _typed + key;
                    if (!_form!.HasLabelPrefix(cand))
                    {
                        Log($"[SESSION] '{cand}' starts no number. Beep.");
                        System.Media.SystemSounds.Beep.Play();
                        return;
                    }
                    _typed = cand; // no filtering: nothing repaints
                    Log($"[SESSION] typed '{_typed}'");
                    return;
            }
        }

        // Called on the STA by the worker after an activation attempt.
        private static void OnActivationResult(string label, bool ok, string name)
        {
            _activationInFlight = false;
            if (ok)
            {
                Log($"[SESSION] activated {label} = '{name}'.");
                EndSession("activation");
            }
            else
            {
                Log($"[SESSION] number {label} ('{name}') could not be activated — button gone? Beep; session stays open.");
                System.Media.SystemSounds.Beep.Play();
                _typed = "";
            }
        }

        private static void EndSession(string reason, bool restoreFocus = false)
        {
            if (!_session) return;
            _session = false;
            _adopted = false;
            _sessionReady = false;
            _keyBuffer.Clear();
            _form!.Release();
            if (restoreFocus && _prevForeground != IntPtr.Zero) SetForegroundWindow(_prevForeground);
            Log($"[SESSION] ended ({reason}) after {Clock.ElapsedMilliseconds - _tShortcut}ms; typed='{_typed}'; held update={(_heldPending ? "yes -> applying once" : "none")}.");
            _typed = "";
            _sessionEnded = true;
            _wake.Set();
        }

        // =============================================================================
        // MTA worker: UIA subscribe / enumerate / reconcile / activate
        // =============================================================================

        private static IUIAutomation? _automation;
        private static IntPtr _hwnd;
        private static IUIAutomationCacheRequest? _cache;
        private static volatile bool _inSettleLoop;

        private static void WorkerMain(ManualResetEventSlim ready, ref Exception? startupError)
        {
            try
            {
                _automation = new CUIAutomation();
                _hwnd = PInvoke.FindWindow("Shell_TrayWnd", null);
                if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("no Shell_TrayWnd");
                var root = _automation.ElementFromHandle(_hwnd);

                _cache = _automation.CreateCacheRequest();
                _cache.TreeScope = TreeScope.TreeScope_Element;
                _cache.AddProperty(UIA_PropertyIds.UIA_NamePropertyId);
                _cache.AddProperty(UIA_PropertyIds.UIA_ClassNamePropertyId);
                _cache.AddProperty(UIA_PropertyIds.UIA_ControlTypePropertyId);

                _automation.AddStructureChangedEventHandler(root, TreeScope.TreeScope_Subtree, _cache, new StructHandler());
                _automation.AddPropertyChangedEventHandler(root, TreeScope.TreeScope_Subtree, _cache, new PropHandler(),
                    new[] { UIA_PropertyIds.UIA_BoundingRectanglePropertyId });
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                Log("Subscribed: StructureChanged + container BoundingRectangle + DisplaySettingsChanged.");
                ready.Set();

                ReconcileUntilStable("initial", displayChange: false);

                while (!_shutdown.IsSet)
                {
                    _wake.WaitOne();
                    if (_shutdown.IsSet) break;
                    ServiceUrgent();

                    if (_resetBook)
                    {
                        _resetBook = false;
                        _book.Reset(_pendingPolicy);
                        ReadAndPush("numbering policy changed", 1, force: true);
                    }

                    if (_reconcilePending)
                    {
                        int settle = _displayPending ? DisplaySettleMs : DebounceQuietMs;
                        while (!_shutdown.IsSet && !_flushRequested)
                        {
                            long remaining = settle - (Clock.ElapsedMilliseconds - Interlocked.Read(ref _lastReqMs));
                            if (remaining <= 0) break;
                            Thread.Sleep((int)Math.Min(remaining, 50));
                            DrainActivations();
                        }
                        if (_shutdown.IsSet) break;
                        if (_flushRequested) { ServiceUrgent(); continue; }

                        bool display = _displayPending, manual = _manualReconcile;
                        _reconcilePending = _displayPending = _manualReconcile = false;
                        ReconcileUntilStable(manual ? "manual" : display ? "display change" : "taskbar event", display);
                    }
                }
                try { _automation.RemoveAllEventHandlers(); } catch { }
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            }
            catch (Exception ex)
            {
                startupError = ex;
                ready.Set();
            }
        }

        // Flush-before-adopt, held-update-at-end, activations. Called between every pass.
        private static void ServiceUrgent()
        {
            if (_flushRequested)
            {
                _flushRequested = false;
                _reconcilePending = _displayPending = _manualReconcile = false;
                ReadAndPush("pending update applied before adopting", 1, force: true);
                _form?.BeginInvoke(new Action(OnFlushDone));
            }
            if (_sessionEnded)
            {
                _sessionEnded = false;
                if (_heldPending)
                {
                    // Applied by the normal reconcile path (or the settle loop already running).
                    _heldPending = false;
                    Interlocked.Increment(ref _heldAppliedCount);
                    Log("[HOLD] session over — applying the held update once.");
                    _reconcilePending = true;
                    Interlocked.Exchange(ref _lastReqMs, 0);
                }
            }
            DrainActivations();
        }

        private static void DrainActivations()
        {
            while (_activateQueue.TryDequeue(out var label)) Activate(label);
        }

        private static void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Log($"[DISPLAY] DisplaySettingsChanged — {(_adopted ? "session open: numbers stay up" : "hiding numbers")} until the taskbar settles.");
            if (!_adopted) { _forcePush = true; _form?.BeginInvoke(new Action(() => _form.SetHidden(true))); }
            _displayPending = true;
            _reconcilePending = true;
            Interlocked.Exchange(ref _lastReqMs, Clock.ElapsedMilliseconds);
            _wake.Set();
        }

        private static void ReconcileUntilStable(string trigger, bool displayChange)
        {
            _inSettleLoop = true;
            try
            {
                if (displayChange) { Thread.Sleep(DisplaySettleMs); }
                long start = Clock.ElapsedMilliseconds;
                int cap = displayChange ? DisplaySettleCapMs : SettleCapMs;
                string? lastSig = null;
                for (int pass = 1; !_shutdown.IsSet; pass++)
                {
                    ServiceUrgent();
                    var (sig, suspectEmpty) = ReadAndPush(trigger, pass, force: false);
                    long elapsed = Clock.ElapsedMilliseconds - start;
                    if (!suspectEmpty && sig == lastSig) break;
                    if (!suspectEmpty) lastSig = sig;
                    if (elapsed > cap) { Log($"[RECONCILE {trigger}] settle cap hit ({elapsed}ms)"); break; }
                    Thread.Sleep(SettlePollMs);
                }
            }
            finally { _inSettleLoop = false; }
        }

        // One read -> (hold | follow positions | reconcile labels) -> push. Returns a
        // rid+position signature for the settle loop.
        private static (string sig, bool suspectEmpty) ReadAndPush(string trigger, int pass, bool force)
        {
            List<(IUIAutomationElement el, string rid, string name, Rectangle rect)> items;
            try { items = EnumerateTaskbar(_automation!.ElementFromHandle(_hwnd)); }
            catch (Exception ex) { Log($"[RECONCILE {trigger}] enumerate failed: {ex.Message}"); return ("<error>", true); }

            if (items.Count == 0 && _shown.Count > 0)
            {
                Log($"[RECONCILE {trigger} p{pass}] suspect empty read — ignored");
                return ("<empty>", true);
            }
            string sig = string.Join("|", items.Select(i => $"{i.rid}:{i.rect.Left}"));

            if (_adopted && !force)
            {
                if (!_heldPending) Log($"[HOLD] update due ({trigger}) while adopted — {(_freezePositions ? "following positions, numbers frozen" : "held until the session ends")}.");
                _heldPending = true;
                if (_freezePositions)
                {
                    var rectByRid = items.ToDictionary(i => i.rid, i => i.rect);
                    var moved = _shown.Select(s => rectByRid.TryGetValue(s.rid, out var r) ? (s.rid, s.label, r) : s).ToList();
                    int n = moved.Where((m, i) => m.Item3 != _shown[i].rect).Count();
                    _shown = moved;
                    if (n > 0) { Log($"[HOLD] {n} badge(s) followed their buttons."); Push(); }
                }
                return (sig, false);
            }

            var result = _book.Reconcile(items.Select(i => (i.rid, i.name)).ToList(), Clock.ElapsedMilliseconds);

            var byLabel = new Dictionary<string, IUIAutomationElement>();
            var names = new Dictionary<string, string>();
            var shown = new List<(string, string, Rectangle)>();
            foreach (var i in items)
            {
                string l = result.LabelByRid[i.rid];
                byLabel[l] = i.el;
                names[l] = i.name;
                shown.Add((i.rid, l, i.rect));
            }
            int movedCount = shown.Count(s => _shown.Any(o => o.rid == s.Item1 && o.rect != s.Item3));
            lock (_setGate) { _elementByLabel = byLabel; _nameByLabel = names; }
            _shown = shown;
            Push();

            foreach (var w in result.Warnings) Log($"  [!] {w}");
            if (result.Changes.Count > 0 || movedCount > 0 || pass == 1 && trigger == "initial")
                Log($"[RECONCILE {trigger} p{pass}] {items.Count} buttons: {(result.Changes.Count > 0 ? string.Join("; ", result.Changes) : "no membership change")}" +
                    (movedCount > 0 ? $"; {movedCount} slid" : ""));
            if (trigger == "initial" && pass == 1)
                Log("  " + string.Join("  ", shown.Select(s => $"{s.Item2}={Trunc(names[s.Item2], 18)}")));
            return (sig, false);
        }

        private static string _lastPushSig = "";
        private static volatile bool _forcePush; // after a display-change hide

        private static void Push()
        {
            var snapshot = _shown.Select(s => (s.rect, s.label)).ToList();
            GetWindowRect(_hwnd, out RECT tb);
            var tbRect = Rectangle.FromLTRB(tb.Left, tb.Top, tb.Right, tb.Bottom);
            // Repaint only when something visible changed: an unchanged re-read must not
            // touch the window (each repaint of the layered window is a visible flicker).
            string sig = tbRect + "|" + string.Join("|", snapshot.Select(x => $"{x.label}@{x.rect}"));
            if (sig == _lastPushSig && !_forcePush) return;
            _lastPushSig = sig;
            _forcePush = false;
            _form?.BeginInvoke(new Action(() =>
            {
                _form.SetTaskbarBounds(tbRect);
                _form.SetHints(snapshot);
                _form.SetHidden(false);
                _form.EnsureTopmost();
            }));
        }

        private static List<(IUIAutomationElement el, string rid, string name, Rectangle rect)> EnumerateTaskbar(IUIAutomationElement root)
        {
            var found = new List<(IUIAutomationElement, string, string, Rectangle)>();
            CollectButtons(root, found, 0);
            return found
                .Where(f => !string.IsNullOrEmpty(f.Item2) && f.Item4.Width > 1 && f.Item4.Height > 1)
                .GroupBy(f => f.Item2).Select(g => g.First())
                .OrderBy(f => f.Item4.Left)
                .ToList();
        }

        private static void CollectButtons(IUIAutomationElement element, List<(IUIAutomationElement, string, string, Rectangle)> into, int depth)
        {
            if (element == null || depth > 12) return;
            string cls = ""; int ct = 0;
            try { cls = element.CurrentClassName ?? ""; } catch { }
            try { ct = element.CurrentControlType; } catch { }
            if (ct is CT_TOOLTIP or CT_MENU or CT_MENUITEM || IGNORE_CLASSES.Contains(cls)) return;

            if (ct == CT_BUTTON || cls.StartsWith("SystemTray.", StringComparison.Ordinal))
            {
                string name = "", rid = ""; tagRECT r = default;
                try { name = element.CurrentName ?? ""; } catch { }
                try { r = element.CurrentBoundingRectangle; } catch { }
                try { rid = string.Join(".", element.GetRuntimeId().Cast<object>()); } catch { }
                into.Add((element, rid, name, Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom)));
                return;
            }
            var walker = _automation!.ControlViewWalker;
            IUIAutomationElement? child = null;
            try { child = walker.GetFirstChildElement(element); } catch { }
            while (child != null)
            {
                IUIAutomationElement? next = null;
                try { next = walker.GetNextSiblingElement(child); } catch { }
                CollectButtons(child, into, depth + 1);
                child = next;
            }
        }

        private static void Activate(string label)
        {
            IUIAutomationElement? el; string name;
            lock (_setGate) { _elementByLabel.TryGetValue(label, out el); name = _nameByLabel.GetValueOrDefault(label, "?"); }
            bool ok = false;
            if (el != null)
            {
                try
                {
                    if (el.GetCurrentPattern(UIA_PatternIds.UIA_InvokePatternId) is IUIAutomationInvokePattern inv) { inv.Invoke(); ok = true; }
                    else if (el.GetCurrentPattern(UIA_PatternIds.UIA_LegacyIAccessiblePatternId) is IUIAutomationLegacyIAccessiblePattern leg) { leg.DoDefaultAction(); ok = true; }
                }
                catch (Exception ex) { Log($"[ACTIVATE {label}] failed: {ex.Message}"); }
            }
            _form?.BeginInvoke(new Action(() => OnActivationResult(label, ok, name)));
        }

        // ---- event handlers: filter by sender identity, then flag a re-read ----------

        private sealed class StructHandler : IUIAutomationStructureChangedEventHandler
        {
            public void HandleStructureChangedEvent(IUIAutomationElement sender, StructureChangeType changeType, Array runtimeId)
            {
                if (!IsTransient(sender)) FlagReconcile($"STRUCT {changeType}");
            }
        }

        private sealed class PropHandler : IUIAutomationPropertyChangedEventHandler
        {
            public void HandlePropertyChangedEvent(IUIAutomationElement sender, int propertyId, object newValue)
            {
                string cls = ""; try { cls = sender.CachedClassName ?? ""; } catch { }
                if (CONTAINER_CLASSES.Contains(cls)) FlagReconcile($"BRECT {cls}");
            }
        }

        private static bool IsTransient(IUIAutomationElement sender)
        {
            string cls = ""; int ct = 0;
            try { cls = sender.CachedClassName ?? ""; } catch { }
            try { ct = sender.CachedControlType; } catch { }
            return ct is CT_TOOLTIP or CT_MENU or CT_MENUITEM || IGNORE_CLASSES.Contains(cls);
        }

        private static void FlagReconcile(string trigger)
        {
            if (!_reconcilePending) Log($"  [event] {trigger}");
            _reconcilePending = true;
            Interlocked.Exchange(ref _lastReqMs, Clock.ElapsedMilliseconds);
            _wake.Set();
        }

        // ---- WinEvents on explorer (second pipeline) -----------------------------------

        private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        private static WinEventProc? _winEventProc;
        private static IntPtr _winHookObj, _winHookState, _winHookFg;

        private static void InstallWinEventHook()
        {
            GetWindowThreadProcessId(_hwnd, out uint pid);
            _winEventProc = (h, ev, hwnd, o, c, t, tm) =>
            {
                if (_form == null || hwnd == _form.Handle) return;
                if (ev == 0x0003) // EVENT_SYSTEM_FOREGROUND (any app): clicking the taskbar or
                {                 // opening Start raises Shell_TrayWnd over us — climb back at once.
                    _form.EnsureTopmost();
                    return;
                }
                // Only the taskbar's own windows trigger a re-read. Process-wide explorer
                // events (Start, flyouts, File Explorer, tooltips) flood otherwise; the
                // settings-class changes they caught in Probe 2a are covered by RegNotify.
                if (GetAncestor(hwnd, 2 /*GA_ROOT*/) != _hwnd) return;
                if (ev == 0x8004) _form.EnsureTopmost(); // REORDER on the taskbar
                FlagReconcile($"WINEVENT 0x{ev:X}");
            };
            _winHookObj = SetWinEventHook(0x8000, 0x8004, IntPtr.Zero, _winEventProc, pid, 0, 0);
            _winHookState = SetWinEventHook(0x800A, 0x800A, IntPtr.Zero, _winEventProc, pid, 0, 0);
            _winHookFg = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, _winEventProc, 0, 0, 0);
            Log($"Subscribed: WinEvents on explorer pid {pid} (obj={_winHookObj != IntPtr.Zero}, state={_winHookState != IntPtr.Zero}).");
        }

        private static void RemoveWinEventHook()
        {
            if (_winHookObj != IntPtr.Zero) UnhookWinEvent(_winHookObj);
            if (_winHookState != IntPtr.Zero) UnhookWinEvent(_winHookState);
            if (_winHookFg != IntPtr.Zero) UnhookWinEvent(_winHookFg);
        }

        // ---- RegNotify on Explorer settings (third pipeline) ---------------------------

        private static void RegWatchMain()
        {
            var hkcu = new UIntPtr(0x80000001u);
            RegOpenKeyExW(hkcu, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", 0, 0x0010, out IntPtr adv);
            RegOpenKeyExW(hkcu, @"Software\Microsoft\Windows\CurrentVersion\Search", 0, 0x0010, out IntPtr search);
            using var advEvt = new AutoResetEvent(false);
            using var searchEvt = new AutoResetEvent(false);
            void Arm(IntPtr k, AutoResetEvent e) { if (k != IntPtr.Zero) RegNotifyChangeKeyValue(k, false, 0x0004, e.SafeWaitHandle.DangerousGetHandle(), true); }
            Arm(adv, advEvt); Arm(search, searchEvt);
            WaitHandle[] handles = { advEvt, searchEvt, _shutdown.WaitHandle };
            while (true)
            {
                int i = WaitHandle.WaitAny(handles);
                if (i == 2) break;
                FlagReconcile(i == 0 ? @"REGNOTIFY Explorer\Advanced" : @"REGNOTIFY Search");
                if (i == 0) Arm(adv, advEvt); else Arm(search, searchEvt);
            }
            if (adv != IntPtr.Zero) RegCloseKey(adv);
            if (search != IntPtr.Zero) RegCloseKey(search);
        }

        // ---- global hotkeys --------------------------------------------------------------

        private sealed class HotkeyWindow : NativeWindow
        {
            private const int HK_ADOPT = 1, HK_POLICY = 2, HK_FREEZE = 3, HK_RECONCILE = 4, HK_QUIT = 5;
            private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000;

            public HotkeyWindow()
            {
                CreateHandle(new CreateParams());
                const uint m3 = MOD_CONTROL | MOD_ALT | MOD_SHIFT;
                bool a = RegisterHotKey(Handle, HK_ADOPT, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x54); // Ctrl+Alt+T
                // Fallback when the real app holds Ctrl+Alt+T: Ctrl+Alt+Shift+T.
                bool fallback = !a && RegisterHotKey(Handle, HK_ADOPT, m3 | MOD_NOREPEAT, 0x54);
                if (fallback) Log("Ctrl+Alt+T is taken (real Windows-Hinting running?) — using Ctrl+Alt+Shift+T for the taskbar shortcut.");
                bool n = RegisterHotKey(Handle, HK_POLICY, m3, 0x4E);
                bool f = RegisterHotKey(Handle, HK_FREEZE, m3, 0x46);
                bool r = RegisterHotKey(Handle, HK_RECONCILE, m3, 0x52);
                bool q = RegisterHotKey(Handle, HK_QUIT, m3, 0x51);
                Log($"Hotkeys: Ctrl+Alt+T adopt={a}  +Shift N policy={n} F freeze={f} R reconcile={r} Q quit={q}" +
                    (a || fallback ? "" : "  !! no taskbar shortcut could be registered."));
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0312)
                {
                    switch (m.WParam.ToInt32())
                    {
                        case HK_ADOPT:
                            OnTaskbarShortcut();
                            break;
                        case HK_POLICY:
                            if (_session) { Log("Finish the session before changing numbering."); break; }
                            var next = (NumberingPolicy)(((int)_book.Policy + 1) % 3);
                            _pendingPolicy = next;
                            Log($"Numbering policy -> {next} (map reset).");
                            _resetBook = true; _wake.Set();
                            break;
                        case HK_FREEZE:
                            _freezePositions = !_freezePositions;
                            Log($"During a session -> {(_freezePositions ? "follow positions, freeze numbers" : "freeze everything")}.");
                            break;
                        case HK_RECONCILE:
                            _manualReconcile = true; FlagReconcile("manual");
                            break;
                        case HK_QUIT:
                            Application.Exit();
                            break;
                    }
                }
                base.WndProc(ref m);
            }

            public void Teardown()
            {
                for (int i = 1; i <= 5; i++) UnregisterHotKey(Handle, i);
                DestroyHandle();
            }
        }

        // ---- helpers / interop -------------------------------------------------------------

        internal static void Log(string message)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} +{Clock.ElapsedMilliseconds,7}ms  {message}";
            lock (LogGate) { Console.WriteLine(line); _logFile?.WriteLine(line); }
        }

        private static string Trunc(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegOpenKeyExW(UIntPtr hKey, string sub, uint opt, uint sam, out IntPtr res);
        [DllImport("advapi32.dll")] private static extern int RegNotifyChangeKeyValue(IntPtr hKey, bool subtree, uint filter, IntPtr evt, bool async);
        [DllImport("advapi32.dll")] private static extern int RegCloseKey(IntPtr hKey);
    }

    /// <summary>
    /// PROTOTYPE. The ambient numbers window: passive (non-activating, click-through)
    /// until a Taskbar session adopts it, then it takes focus and reads keys itself.
    /// Sized to the taskbar strip only, so a foreground topmost window never covers
    /// the whole monitor (which could trip fullscreen detection).
    /// </summary>
    internal sealed class AmbientNumbersForm : Form
    {
        private const string BaseTitle = "Windows Hinting Taskbar Numbers";
        private const int GWLP_HWNDPARENT = -8;
        private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000;
        private const int WM_ACTIVATE = 0x0006, WM_KEYDOWN = 0x0100, WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;

        private List<(Rectangle rect, string label)> _hints = new();
        private bool _hidden;
        private bool _adopted;
        private readonly float _fontScale;
        private Font _font;

        public event Action<string>? KeyIntent;
        public event Action? FocusLost;

        public AmbientNumbersForm(float fontScale)
        {
            _fontScale = fontScale;
            Text = BaseTitle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(0, 0, 1, 1);
            BackColor = Color.LimeGreen;
            TransparencyKey = Color.LimeGreen;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _font = MakeFont();
        }

        private Font MakeFont()
        {
            var baseFont = SystemFonts.CaptionFont!;
            float dpi = DeviceDpi > 0 ? DeviceDpi : 96f;
            return new Font(baseFont.FontFamily, baseFont.SizeInPoints * dpi / 72f * _fontScale, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x20 | 0x80 | 0x80000 | 0x8 | WS_EX_NOACTIVATE; // TRANSPARENT TOOLWINDOW LAYERED TOPMOST NOACTIVATE
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var old = _font; _font = MakeFont(); old.Dispose();

            // Owned by the taskbar: Windows keeps an owned window above its owner, so when
            // the taskbar is raised (Start, right-click, clicking it) we move up with it in
            // the same z-order operation instead of a frame later. Re-asserting topmost after
            // the foreground event was not enough — that still flickered (owner, 2026-10-16).
            // Caveat to measure: a cross-process owner can attach input queues with explorer.
            IntPtr tray = FindWindowW("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
            {
                SetWindowLongPtr(Handle, GWLP_HWNDPARENT, tray);
                TaskbarAmbientPrototype.Log($"Numbers window owned by Shell_TrayWnd 0x{tray.ToInt64():X} (owner now 0x{GetWindowLongPtr(Handle, GWLP_HWNDPARENT).ToInt64():X}).");
            }
        }

        public void SetTaskbarBounds(Rectangle r) { if (Bounds != r) Bounds = r; }
        public void SetHints(List<(Rectangle rect, string label)> hints) { _hints = hints; Invalidate(); }
        public void SetHidden(bool hidden) { if (_hidden != hidden) { _hidden = hidden; Invalidate(); } }
        public bool HasLabel(string l) => _hints.Any(h => h.label == l);
        public bool HasLabelPrefix(string p) => _hints.Any(h => h.label.StartsWith(p, StringComparison.Ordinal));

        public void EnsureTopmost() => SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);

        /// <summary>Same window takes focus: drop NOACTIVATE, take the foreground, mark [Active].</summary>
        public bool Adopt()
        {
            _adopted = true;
            SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64() & ~WS_EX_NOACTIVATE));
            Text = BaseTitle + " [Active]";
            bool ok = TaskbarAmbientPrototype.SetForegroundWindow(Handle);
            Activate();
            Focus();
            return ok;
        }

        public void Release()
        {
            _adopted = false;
            Text = BaseTitle;
            SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64() | WS_EX_NOACTIVATE));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!_adopted) return base.ProcessCmdKey(ref msg, keyData);
            Keys k = keyData & Keys.KeyCode;
            string? intent = k switch
            {
                >= Keys.D0 and <= Keys.D9 when (keyData & Keys.Modifiers) == 0 => ((char)('0' + (k - Keys.D0))).ToString(),
                >= Keys.NumPad0 and <= Keys.NumPad9 => ((char)('0' + (k - Keys.NumPad0))).ToString(),
                Keys.Enter or Keys.Space => "Enter",
                Keys.Escape => "Esc",
                Keys.Back => "Back",
                _ => null,
            };
            if (intent != null) KeyIntent?.Invoke(intent);
            return true; // the adopted window eats every key
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = new IntPtr(MA_NOACTIVATE); return; }
            // Fallback if ProcessCmdKey was bypassed for this message.
            if (m.Msg == WM_KEYDOWN && _adopted) { var kd = (Keys)(int)m.WParam | ModifierKeys; ProcessCmdKey(ref m, kd); return; }
            base.WndProc(ref m);
            if (m.Msg == WM_ACTIVATE && (m.WParam.ToInt64() & 0xFFFF) == 0 && _adopted)
                BeginInvoke(new Action(() => { if (_adopted) FocusLost?.Invoke(); }));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_hidden) return;
            var g = e.Graphics;
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            using var bg = new SolidBrush(Color.FromArgb(230, 0, 0, 0));
            int padX = Math.Max(1, DeviceDpi * 2 / 96);
            foreach (var (rect, label) in _hints)
            {
                // Bottom-left quarter of the button: the badge is a quarter of the button's
                // height, so the icon above it stays visible.
                int h = Math.Max(8, rect.Height / 4);
                var font = FontForHeight(h);
                var size = TextRenderer.MeasureText(g, label, font, Size.Empty, flags);
                var box = new Rectangle(rect.Left - Left, rect.Bottom - h - Top, size.Width + padX * 2, h);
                g.FillRectangle(bg, box);
                TextRenderer.DrawText(g, label, font, box, Color.FromArgb(255, 255, 255, 0),
                    flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // Fonts keyed by badge height; text is sized to fill the badge.
        private readonly Dictionary<int, Font> _fontsByHeight = new();
        private Font FontForHeight(int h)
        {
            if (!_fontsByHeight.TryGetValue(h, out var f))
            {
                f = new Font(SystemFonts.CaptionFont!.FontFamily, h * 0.85f, FontStyle.Bold, GraphicsUnit.Pixel);
                _fontsByHeight[h] = f;
            }
            return f;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _font.Dispose(); foreach (var f in _fontsByHeight.Values) f.Dispose(); }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string cls, string? title);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    }
}
