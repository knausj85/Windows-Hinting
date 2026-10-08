// =============================================================================
// PROTOTYPE — Probe 2a: pure-event numbered overlay glued to the SYSTEM TRAY   (issue #84)
// =============================================================================
// THROWAWAY. Lives only on branch `prototype/taskbar-overlay-probe` off `main`.
// Do NOT ship, do NOT merge to main. Sibling of TaskbarOverlayProbe.cs (#83);
// that telemetry probe stays as-is. This one builds the actual glued overlay.
//
// WHAT IT ANSWERS (#84, re-framed by #83's steer — architecture is now FIXED):
//   Probe 1 (#83) proved the Win11 WinUI taskbar raises NO per-element
//   BoundingRectangle for sliding tray icons — only container panes change
//   *width*. So the mechanism is NOT event-follow; it is an event-triggered
//   FULL RE-READ reconcile. This probe wires that reconcile to a PERSISTENT
//   numbered overlay over the always-visible system-tray icons (the smallest
//   clean surface, bottom rung of the ladder) and asks:
//
//     Does the event-triggered re-read reconcile keep the tray overlay glued
//     within the ~few-hundred-ms bar through gate C (tray add/remove) and
//     gate E (display change), with NO manual refresh and NO content poll —
//     and is a timeout backstop needed on top (-> option b)?
//
// SURFACE (operator's choice on #84, then EXTENDED 2026-10-07): originally the
//   system tray only (chevron + always-visible icons, clock/quick-settings
//   excluded) — that held, so the operator asked to climb to the WHOLE taskbar.
//   Now numbers EVERY taskbar button left-to-right: Start/Search/Task View/
//   Widgets, the running/pinned APP-ROW buttons (the reflow-heavy surface that
//   is Probe 2b / #85), the "Show Hidden Icons" chevron, the always-visible
//   tray icons, AND the network/volume/battery quick-settings button + the
//   clock/notification-center. This merges the 2a (tray) + 2b (app row) rungs
//   into one probe. The enumeration LOGS every candidate (class/name/ct/bounds)
//   at startup and on the `s` command so the included set is auditable.
//
// ARCHITECTURE (per #83 steer, #48, #82):
//   * THREE threads:
//       - Main STA thread: hosts the real app's OverlayManager (reused from
//         Forms/OverlayManager.cs) + Application.Run() message pump.
//       - MTA worker thread: ALL UIA subscribe/enumerate/activate (COM).
//       - Console reader thread: activation affordance + quit (not judged).
//   * TRIGGERS (no content poll): StructureChanged (Subtree on Shell_TrayWnd),
//     BoundingRectangle on the CONTAINER panes (TrayNotifyWnd/Shell_TrayWnd/
//     MSTaskSwWClass — their width changes on reflow), SystemEvents.
//     DisplaySettingsChanged (gate E).
//   * FILTER-THEN-DEBOUNCE: react only to real content senders (SystemTray.*
//     button, TaskListButton) or the container panes; IGNORE ToolTip (50022),
//     Menu/MenuItem (50009/50011), Xaml_WindowedPopupClass/PopupRoot/PopupHost/
//     DragVisualWnd (bare hover alone raises these — #83). Then coalesce the
//     2-4x duplicate structure fires into ONE reconcile per quiet window.
//   * REACTION: a debounced FULL re-enumerate + re-read of tray child bounds,
//     reconciled by RuntimeId (reuse/move/add/remove), pushed to the overlay.
//     Gate E waits out the ~400-600 ms display-settle lag before re-reading.
//
// NOTE ON THE WORKER LOOP: it is event-driven (AutoResetEvent). The only sleeps
//   are the debounce/settle waits, which are load-bearing (#83 steer), NOT a
//   content poll. Nothing re-enumerates the tray on a timer.
//
// RUN:  Windows-Hinting.exe --probe-tray-overlay
//   A console opens; numbered labels appear over the tray. Drive the scenarios
//   by hand, watch the reconcile log, then quit. Commands while running:
//     <number>+Enter = activate that tray icon (invoke; testability only)
//     r+Enter         = force a manual reconcile (to compare vs event-driven)
//     s+Enter         = dump the current tray enumeration (include/exclude)
//     Enter / q       = quit
//   A transcript is written next to the exe as tray-overlay-<timestamp>.log —
//   that file + the session notes are the deliverable for #84.
//
// SCENARIOS TO DRIVE (map #81 gated stress set, tray surface):
//   C  start/exit a background tray app      -> tray icon add/remove
//   E  change resolution/scale, connect/disconnect a monitor, move/auto-hide
//      the taskbar                           -> display-change re-read
//   (also worth poking: launch/close a windowed app to force a CENTERED-taskbar
//    reflow that slides the tray icons left/right with no tray add/remove —
//    the pure-reposition case that stresses the container-width trigger.)
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
using WindowsHinting.Forms;
using WindowsHinting.Logging;
using WindowsHinting.Models;

namespace WindowsHinting
{
    /// <summary>
    /// PROTOTYPE (#84). Throwaway event-driven tray overlay probe — see file header.
    /// </summary>
    internal static class TaskbarOverlayProbe2a
    {
        // ---- enumeration rules (EXTENDED scope: whole taskbar + full tray) -------
        // Number every taskbar/tray BUTTON left-to-right: Start/Search/Task View,
        // app-row buttons, chevron, tray icons, network/volume/battery, clock. A
        // candidate qualifies if its controlType is Button (50000) OR its class
        // starts "SystemTray." (belt-and-suspenders for odd tray control types),
        // minus the transient-noise classes/types in the ignore sets. Every
        // candidate is logged at startup + on `s` so the set is auditable.
        private const int CT_BUTTON = 50000;

        // Overlay look (operator, 2026-10-07): numbers BELOW the icons, readable.
        private const HintPosition LabelPosition = HintPosition.LowerCenter;
        private const float LabelFontScale = 1.8f;

        // Debounce / settle windows (ms). Load-bearing per #83; not a content poll.
        private const int DebounceQuietMs = 150;    // coalesce the 2-4x dup structure fires
        private const int DisplaySettleMs = 650;    // wait out the gate-E re-layout lag

        // Post-change SETTLE loop (option b, #74-style progressive reconcile): after a
        // change, re-read every SettlePollMs until the rid+position signature is stable
        // for two reads or the cap is hit. This catches a newly-launched app button
        // that is not yet laid out at debounce time (the #83 "ready-enough" race) and
        // rides out the transient empty-tree flicker during reflow. Event-GATED — it
        // runs only after an event, never on a steady-state timer.
        private const int SettlePollMs = 180;
        private const int SettleCapMs = 1800;        // content-change animation cap
        private const int DisplaySettleCapMs = 3000; // display-change cap (longer relayout)

        // Container panes whose *width* changes on reflow (a re-read trigger).
        private static readonly string[] CONTAINER_CLASSES =
        {
            "TrayNotifyWnd", "Shell_TrayWnd", "MSTaskSwWClass",
        };
        // Transient overlays that fire on bare hover/drag (#83) — never react.
        private static readonly string[] IGNORE_CLASSES =
        {
            "Xaml_WindowedPopupClass", "PopupRoot", "PopupHost", "DragVisualWnd",
        };
        // ControlTypes to ignore outright: ToolTip, Menu, MenuItem.
        private const int CT_TOOLTIP = 50022, CT_MENU = 50009, CT_MENUITEM = 50011;

        // ---- shared state --------------------------------------------------------
        private static readonly object LogGate = new();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static StreamWriter? _logFile;
        private static OverlayManager? _overlay;

        // Worker signalling.
        private static readonly AutoResetEvent _wake = new(false);
        private static readonly ManualResetEventSlim _shutdown = new(false);
        private static volatile bool _reconcilePending;
        private static volatile bool _displayPending;
        private static volatile bool _manualReconcile;
        private static long _lastReqMs;
        private static long _lastTriggerTag; // for logging what woke the reconcile

        // Activation + snapshot requests from the console thread -> MTA worker.
        private static readonly ConcurrentQueue<string> _activateQueue = new();
        private static volatile bool _snapshotRequested;

        // Current labelled tray set (MTA-thread owned; read under lock for activation).
        private static readonly object _setGate = new();
        private static Dictionary<string, IUIAutomationElement> _activatable = new();
        private static Dictionary<string, (string label, string rectStr)> _prevByRid = new();

        // WinEvent hook (#84 iter): a SECOND event pipeline (MSAA/WinEvents) to catch
        // the show/hide + state changes the XAML taskbar does NOT raise as UIA
        // StructureChanged — e.g. toggling the Task View / Search / Widgets system
        // buttons in Settings collapses them with no UIA structure signal. Scoped to
        // the taskbar's own Explorer thread; OUTOFCONTEXT so the callback runs on our
        // STA pump. The delegate is pinned in a static field so it is not GC-collected.
        private static WinEventProc? _winEventProc;
        private static IntPtr _winHookObj;   // CREATE..REORDER
        private static IntPtr _winHookState; // STATECHANGE

        // Global hotkeys (#84 iter): drive the probe regardless of whether the console
        // has interactive stdin — essential when launched detached.
        private static HotkeyWindow? _hotkeys;

        // Registry-notify watcher (#84 iter): the gate-F settings class (Task View,
        // Widgets, alignment, combine, search box) is SILENT to BOTH UIA and WinEvents
        // — confirmed on #84: toggling Task View adds/removes the button and reflows the
        // whole app row with zero events. RegNotifyChangeKeyValue on the Explorer
        // settings keys is event-driven (blocks until a value changes; no poll) and
        // fires the same debounced reconcile.
        private static Thread? _regWatch;

        // Entry point, reached from Program.Main when args contain --probe-tray-overlay.
        public static void Run()
        {
            OpenConsole();
            string logPath = Path.Combine(
                AppContext.BaseDirectory,
                $"tray-overlay-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _logFile = new StreamWriter(logPath, append: false) { AutoFlush = true };

            Log("=== PROBE 2a — EVENT-DRIVEN TRAY OVERLAY (#84) ===");
            Log($"log file: {logPath}");
            Log($"OS: {Environment.OSVersion}  .NET: {Environment.Version}  64-bit proc: {Environment.Is64BitProcess}");
            Log("Reusing the real app's OverlayManager/OverlayForm rendering (Forms/).");
            Log("Triggers = StructureChanged + container BoundingRectangle + DisplaySettingsChanged. NO content poll.");
            Log("");

            // Overlay lives on this (main) STA thread with the message pump.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var logger = new ProbeLogger();
            _overlay = new OverlayManager(logger);
            _overlay.ApplyHintPosition(LabelPosition);  // numbers below the icons
            _overlay.ApplyFontScale(LabelFontScale);    // readable size
            _overlay.SetEnabled(true);
            _overlay.SetActiveState(true);

            using var ready = new ManualResetEventSlim(false);
            Exception? startupError = null;

            var worker = new Thread(() => WorkerMain(ready, ref startupError))
            {
                Name = "TrayOverlayProbe-UIA-MTA",
                IsBackground = true,
            };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();

            ready.Wait();
            if (startupError != null)
            {
                Log($"!! STARTUP FAILED: {startupError.Message}");
                Log("Press Enter to exit.");
                Console.ReadLine();
                _shutdown.Set();
                _wake.Set();
                CloseConsole();
                return;
            }

            Log("");
            Log(">>> READY. Numbers should be on the WHOLE taskbar + tray. Drive gate A/C + gate E now. <<<");
            Log(">>> <number>+Enter = activate,  r = manual reconcile,  s = dump tray,  Enter/q = quit. <<<");
            Log("");

            // Console reader runs on its own background thread so it never blocks the pump.
            var reader = new Thread(ConsoleReaderMain) { Name = "TrayOverlayProbe-Console", IsBackground = true };
            reader.Start();

            // Install the WinEvent hook on THIS (STA) thread so OUTOFCONTEXT delivery
            // lands on the Application.Run message pump below. _hwnd was resolved by the
            // worker before ready.Set(); events queue until the pump starts.
            InstallWinEventHook();

            // Global hotkeys on the STA thread — the drive channel that works even with
            // no interactive console (WM_HOTKEY is pumped by Application.Run below).
            _hotkeys = new HotkeyWindow();

            // Third trigger pipeline: registry-notify for the UIA+WinEvent-silent
            // settings class (Task View toggle et al).
            StartRegistryWatch();

            // Pump the overlay UI on this thread until asked to exit.
            Application.Run();

            Log("");
            Log("Stopping — unsubscribing...");
            try { _hotkeys?.Teardown(); } catch { }
            RemoveWinEventHook();
            _shutdown.Set();
            _wake.Set();
            worker.Join(3000);
            try { _regWatch?.Join(1000); } catch { }
            try { _overlay.Dispose(); } catch { }
            Log("=== PROBE 2a ENDED ===");
            _logFile.Flush();
            _logFile.Dispose();
            CloseConsole();
        }

        private static void ConsoleReaderMain()
        {
            while (!_shutdown.IsSet)
            {
                string? line = Console.ReadLine();
                if (line == null)
                {
                    // No interactive stdin (e.g. launched detached): ReadLine hits EOF.
                    // Do NOT quit — the overlay stays live, driven by events, and the
                    // global hotkeys (Ctrl+Alt+Shift+S/R/Q) take over for dump/quit.
                    Log("stdin is not interactive (EOF) — typed console commands disabled.");
                    Log(">>> Drive with GLOBAL HOTKEYS: Ctrl+Alt+Shift+S = dump,  +R = reconcile,  +Q = quit. <<<");
                    return;
                }
                string cmd = line.Trim();
                if (cmd.Length == 0 || cmd.Equals("q", StringComparison.OrdinalIgnoreCase))
                {
                    RequestExit();
                    return;
                }
                if (cmd.Equals("r", StringComparison.OrdinalIgnoreCase))
                {
                    _manualReconcile = true; _reconcilePending = true;
                    Interlocked.Exchange(ref _lastReqMs, Clock.ElapsedMilliseconds);
                    _wake.Set();
                }
                else if (cmd.Equals("s", StringComparison.OrdinalIgnoreCase))
                {
                    _snapshotRequested = true; _wake.Set();
                }
                else
                {
                    // Treat anything else as an activation label (the number shown).
                    _activateQueue.Enqueue(cmd);
                    _wake.Set();
                }
            }
        }

        // Ask the UI pump to exit; marshal to the STA thread to be safe.
        private static void RequestExit()
        {
            try { _overlay?.BeginInvoke(() => Application.Exit()); }
            catch { try { Application.Exit(); } catch { } }
        }

        // ---- MTA worker: all UIA work happens here ------------------------------

        private static IUIAutomation? _automation;
        private static IntPtr _hwnd;
        private static IUIAutomationCacheRequest? _cache;

        private static void WorkerMain(ManualResetEventSlim ready, ref Exception? startupError)
        {
            try
            {
                _automation = new CUIAutomation();

                _hwnd = PInvoke.FindWindow("Shell_TrayWnd", null);
                if (_hwnd == IntPtr.Zero)
                    throw new InvalidOperationException("FindWindow('Shell_TrayWnd') returned 0 — no primary taskbar?");
                Log($"Shell_TrayWnd hwnd = 0x{_hwnd.ToInt64():X}");

                IUIAutomationElement root = _automation.ElementFromHandle(_hwnd)
                    ?? throw new InvalidOperationException("ElementFromHandle(Shell_TrayWnd) returned null.");

                _cache = _automation.CreateCacheRequest();
                _cache.TreeScope = TreeScope.TreeScope_Element;
                _cache.AddProperty(UIA_PropertyIds.UIA_NamePropertyId);
                _cache.AddProperty(UIA_PropertyIds.UIA_ClassNamePropertyId);
                _cache.AddProperty(UIA_PropertyIds.UIA_ControlTypePropertyId);
                _cache.AddProperty(UIA_PropertyIds.UIA_BoundingRectanglePropertyId);
                _cache.AddProperty(UIA_PropertyIds.UIA_AutomationIdPropertyId);

                var structHandler = new StructHandler();
                var propHandler = new PropHandler();

                _automation.AddStructureChangedEventHandler(root, TreeScope.TreeScope_Subtree, _cache, structHandler);
                Log("Subscribed: StructureChanged (Subtree on Shell_TrayWnd).");
                _automation.AddPropertyChangedEventHandler(root, TreeScope.TreeScope_Subtree, _cache, propHandler,
                    new[] { UIA_PropertyIds.UIA_BoundingRectanglePropertyId });
                Log("Subscribed: BoundingRectangle PropertyChanged (Subtree on Shell_TrayWnd).");
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                Log("Subscribed: SystemEvents.DisplaySettingsChanged (gate E).");

                ready.Set();

                // First paint: enumerate and push to the overlay.
                ReconcileUntilStable("initial", displayChange: false);

                // Event-driven loop. No timer re-enumerates the tray.
                while (!_shutdown.IsSet)
                {
                    _wake.WaitOne();
                    if (_shutdown.IsSet) break;

                    // Activation + snapshot are serviced immediately (no debounce).
                    while (_activateQueue.TryDequeue(out var label))
                        Activate(label);
                    if (_snapshotRequested)
                    {
                        _snapshotRequested = false;
                        Log("---- TASKBAR ENUMERATION SNAPSHOT (on request) ----");
                        try { EnumerateTaskbar(_automation.ElementFromHandle(_hwnd), logEach: true); }
                        catch (Exception ex) { Log($"snapshot failed: {ex.Message}"); }
                        Log("---- END SNAPSHOT ----");
                    }

                    if (_reconcilePending)
                    {
                        // Debounce: wait for a quiet window (or the longer display-settle
                        // lag on gate E) with no new request, then reconcile ONCE.
                        int settle = _displayPending ? DisplaySettleMs : DebounceQuietMs;
                        while (!_shutdown.IsSet)
                        {
                            long quietFor = Clock.ElapsedMilliseconds - Interlocked.Read(ref _lastReqMs);
                            long remaining = settle - quietFor;
                            if (remaining <= 0) break;
                            Thread.Sleep((int)Math.Min(remaining, 200));
                        }
                        if (_shutdown.IsSet) break;

                        bool display = _displayPending;
                        bool manual = _manualReconcile;
                        _reconcilePending = false;
                        _displayPending = false;
                        _manualReconcile = false;

                        string trig = manual ? "manual(r)" : (display ? "display-change(E)" : "structure/brect(C)");
                        ReconcileUntilStable(trig, display);
                    }
                }

                try { _automation.RemoveAllEventHandlers(); } catch (Exception ex) { Log($"RemoveAllEventHandlers: {ex.Message}"); }
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            }
            catch (Exception ex)
            {
                startupError = ex;
                ready.Set();
            }
        }

        private static void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Log($"[DISPLAY] DisplaySettingsChanged (primary now {Screen.PrimaryScreen?.Bounds}) — will re-read after {DisplaySettleMs}ms settle.");
            _displayPending = true;
            _reconcilePending = true;
            Interlocked.Exchange(ref _lastReqMs, Clock.ElapsedMilliseconds);
            _wake.Set();
        }

        // ---- the reconcile: event-triggered FULL re-read, settled (not event-follow) ----

        private static void ReconcileUntilStable(string trigger, bool displayChange)
        {
            // Gate E: display topology may have changed — rebuild per-monitor overlay
            // forms first, then re-enable (new forms default to disabled).
            if (displayChange && _overlay != null)
            {
                bool posted = _overlay.BeginInvoke(() =>
                {
                    _overlay.RebuildOverlays();
                    _overlay.ApplyHintPosition(LabelPosition);
                    _overlay.ApplyFontScale(LabelFontScale);
                    _overlay.SetEnabled(true);
                    _overlay.SetActiveState(true);
                });
                if (posted) Thread.Sleep(DisplaySettleMs); // wait out the gate-E relayout
            }

            // Progressive re-read until the taskbar stops changing (see SettlePollMs
            // comment). Each pass pushes the current read; we stop once two consecutive
            // reads have the same rid+position signature, or the cap is hit.
            long start = Clock.ElapsedMilliseconds;
            int cap = displayChange ? DisplaySettleCapMs : SettleCapMs;
            string? lastSig = null;
            int pass = 0;
            while (!_shutdown.IsSet)
            {
                pass++;
                var (sig, suspectEmpty) = ReadAndPush(trigger, pass);
                long elapsed = Clock.ElapsedMilliseconds - start;

                if (suspectEmpty)
                {
                    // Transient empty tree during reflow — do NOT accept it (would blank
                    // the overlay). Keep polling until it repopulates or we hit the cap.
                    if (elapsed > cap) { Log($"[RECONCILE {trigger}] gave up waiting out empty tree ({elapsed}ms)"); break; }
                    Thread.Sleep(SettlePollMs);
                    continue;
                }
                if (sig == lastSig) break;          // two identical reads → settled
                lastSig = sig;
                if (elapsed > cap) { Log($"[RECONCILE {trigger}] settle cap hit ({elapsed}ms, still changing)"); break; }
                Thread.Sleep(SettlePollMs);
            }
        }

        // One read → push → log. Returns the rid+position signature and whether this
        // read is a suspect transient-empty (0 icons while we had some before).
        private static (string sig, bool suspectEmpty) ReadAndPush(string trigger, int pass)
        {
            long t0 = Clock.ElapsedMilliseconds;
            List<(IUIAutomationElement el, string label, string rid, tagRECT rect, string desc)> tray;
            try
            {
                var root = _automation!.ElementFromHandle(_hwnd);
                tray = EnumerateTaskbar(root, logEach: trigger == "initial" && pass == 1);
            }
            catch (Exception ex)
            {
                Log($"[RECONCILE {trigger} p{pass}] ENUMERATE FAILED: {ex.Message}");
                return ("<error>", true); // treat as suspect → retry within the cap
            }

            if (tray.Count == 0 && _prevByRid.Count > 0)
            {
                Log($"[RECONCILE {trigger} p{pass}] suspect empty (0 while had {_prevByRid.Count}) — not pushing");
                return ("<empty>", true);
            }

            // Build overlay hints + activation map; diff vs previous by RuntimeId.
            var hints = new List<HintItem>(tray.Count);
            var newActivatable = new Dictionary<string, IUIAutomationElement>();
            var newByRid = new Dictionary<string, (string, string)>();
            int added = 0, moved = 0, reused = 0;
            var seenRids = new HashSet<string>();

            foreach (var (el, label, rid, rect, _) in tray)
            {
                var r = new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
                hints.Add(new HintItem { Rect = r, Label = label, Element = el });
                newActivatable[label] = el;
                string rectStr = $"{label}@({r.X},{r.Y},{r.Width}x{r.Height})";
                newByRid[rid] = (label, rectStr);
                seenRids.Add(rid);

                if (_prevByRid.TryGetValue(rid, out var prev))
                {
                    if (prev.rectStr != rectStr) moved++; else reused++;
                }
                else added++;
            }
            int removed = _prevByRid.Keys.Count(k => !seenRids.Contains(k));

            lock (_setGate)
            {
                _activatable = newActivatable;
                _prevByRid = newByRid;
            }

            bool pushed = _overlay?.BeginInvoke(() =>
            {
                _overlay.SetHints(hints);
                _overlay.EnsureTopmost();
            }) ?? false;

            long dt = Clock.ElapsedMilliseconds - t0;
            Log($"[RECONCILE {trigger} p{pass}] {tray.Count} icon(s) in {dt}ms  (+{added} ~{moved}move ={reused}reuse -{removed})  pushed={pushed}");
            if (trigger == "initial" && pass == 1)
                Log($"           {string.Join(" ", tray.Select(t => $"{t.label}:{t.desc}"))}");

            if (!pushed)
            {
                Thread.Sleep(80); // overlay handle not ready (first-paint race) — retry
                _overlay?.BeginInvoke(() => { _overlay.SetHints(hints); _overlay.EnsureTopmost(); });
            }

            return (string.Join("|", tray.Select(t => $"{t.rid}:{t.rect.left}")), false);
        }

        // ---- taskbar enumeration: every button, left-to-right (EXTENDED scope) ---

        private static List<(IUIAutomationElement el, string label, string rid, tagRECT rect, string desc)>
            EnumerateTaskbar(IUIAutomationElement root, bool logEach)
        {
            // Collect every candidate button under Shell_TrayWnd (ControlView).
            var found = new List<(IUIAutomationElement el, string name, string cls, int ct, tagRECT rect, string rid)>();
            CollectButtons(root, found, 0, maxDepth: 12);

            // De-dup by RuntimeId (the subtree can surface an element twice), sort L→R.
            var byRid = new Dictionary<string, (IUIAutomationElement el, string name, string cls, int ct, tagRECT rect, string rid)>();
            foreach (var c in found)
                if (!string.IsNullOrEmpty(c.rid)) byRid[c.rid] = c;
            var cands = byRid.Values.ToList();
            cands.Sort((a, b) => a.rect.left.CompareTo(b.rect.left));

            var result = new List<(IUIAutomationElement, string, string, tagRECT, string)>();
            int n = 0;
            foreach (var c in cands)
            {
                // Skip zero-area / off-screen ghosts (collapsed or not-yet-laid-out).
                int w = c.rect.right - c.rect.left, h = c.rect.bottom - c.rect.top;
                bool degenerate = w <= 1 || h <= 1;
                bool include = !degenerate;

                if (include)
                {
                    n++;
                    string label = n.ToString(CultureInfo.InvariantCulture);
                    string desc = $"{c.cls}'{Trunc(c.name, 24)}'";
                    result.Add((c.el, label, c.rid, c.rect, desc));
                    if (logEach) Log($"  [INCLUDE] {label,3}  ct={ControlTypeName(c.ct)} {c.cls} name='{Trunc(c.name, 36)}' bounds={RectToString(c.rect)}");
                }
                else if (logEach)
                {
                    Log($"  [skip   ]      ct={ControlTypeName(c.ct)} {c.cls} name='{Trunc(c.name, 36)}' bounds={RectToString(c.rect)} (zero-area)");
                }
            }
            return result;
        }

        // A candidate = controlType Button OR a SystemTray.* class, minus the
        // transient-noise classes/types. A button has no button descendants we
        // care about, so we stop descending once one is collected.
        private static void CollectButtons(IUIAutomationElement element,
            List<(IUIAutomationElement, string, string, int, tagRECT, string)> into, int depth, int maxDepth)
        {
            if (element == null || depth > maxDepth) return;

            string cls = "", name = ""; int ct = 0;
            try { cls = element.CurrentClassName ?? ""; } catch { }
            try { ct = element.CurrentControlType; } catch { }

            // Transient overlay? Do not collect it or descend into it.
            if (ct is CT_TOOLTIP or CT_MENU or CT_MENUITEM) return;
            foreach (var ig in IGNORE_CLASSES)
                if (cls.Equals(ig, StringComparison.Ordinal)) return;

            bool isButton = ct == CT_BUTTON || cls.StartsWith("SystemTray.", StringComparison.Ordinal);
            if (isButton)
            {
                tagRECT rect = default; string rid = "";
                try { name = element.CurrentName ?? ""; } catch { }
                try { rect = element.CurrentBoundingRectangle; } catch { }
                try { rid = RidToString(element.GetRuntimeId()); } catch { }
                into.Add((element, name, cls, ct, rect, rid));
                return; // stop — a button's children aren't separately numbered
            }

            IUIAutomationTreeWalker walker = _automation!.ControlViewWalker;
            IUIAutomationElement? child = null;
            try { child = walker.GetFirstChildElement(element); } catch { }
            while (child != null)
            {
                IUIAutomationElement? next = null;
                try { next = walker.GetNextSiblingElement(child); } catch { }
                CollectButtons(child, into, depth + 1, maxDepth);
                child = next;
            }
        }

        // ---- activation affordance (testability only; NOT judged) ----------------

        private static void Activate(string label)
        {
            IUIAutomationElement? el;
            lock (_setGate) { _activatable.TryGetValue(label, out el); }
            if (el == null) { Log($"[ACTIVATE {label}] no tray icon with that number."); return; }

            string name = ""; try { name = el.CurrentName ?? ""; } catch { }
            // Try InvokePattern, then LegacyIAccessible.DoDefaultAction (cross-process,
            // so no same-process pump deadlock — unlike the real app's own UI).
            try
            {
                if (el.GetCurrentPattern(UIA_PatternIds.UIA_InvokePatternId) is IUIAutomationInvokePattern inv)
                {
                    inv.Invoke();
                    Log($"[ACTIVATE {label}] Invoke() -> '{Trunc(name, 30)}'");
                    return;
                }
            }
            catch (Exception ex) { Log($"[ACTIVATE {label}] Invoke failed: {ex.Message}"); }
            try
            {
                if (el.GetCurrentPattern(UIA_PatternIds.UIA_LegacyIAccessiblePatternId) is IUIAutomationLegacyIAccessiblePattern leg)
                {
                    leg.DoDefaultAction();
                    Log($"[ACTIVATE {label}] LegacyIAccessible.DoDefaultAction() -> '{Trunc(name, 30)}'");
                    return;
                }
            }
            catch (Exception ex) { Log($"[ACTIVATE {label}] DoDefaultAction failed: {ex.Message}"); }
            Log($"[ACTIVATE {label}] no usable activation pattern on '{Trunc(name, 30)}'.");
        }

        // ---- COM event handlers: filter by sender identity, then flag reconcile --

        private sealed class StructHandler : IUIAutomationStructureChangedEventHandler
        {
            public void HandleStructureChangedEvent(IUIAutomationElement sender, StructureChangeType changeType, Array runtimeId)
            {
                // Any structure change that is not transient hover/popup noise
                // (ToolTip/menu/popup/drag — #83) triggers a full re-read.
                if (!IsTransient(sender, out string who))
                    FlagReconcile($"STRUCT {changeType} {who}");
            }
        }

        private sealed class PropHandler : IUIAutomationPropertyChangedEventHandler
        {
            public void HandlePropertyChangedEvent(IUIAutomationElement sender, int propertyId, object newValue)
            {
                if (propertyId != UIA_PropertyIds.UIA_BoundingRectanglePropertyId) return;
                // #83: per-element BRECT never fires for sliding icons; only the
                // CONTAINER panes' width changes on reflow. React to those ONLY —
                // ignore the DragVisualWnd flood and popup/tooltip per-element BRECTs.
                if (IsContainer(sender, out string who))
                    FlagReconcile($"BRECT {who}");
            }
        }

        private static bool IsTransient(IUIAutomationElement sender, out string who)
        {
            string cls = "", name = ""; int ct = 0;
            try { cls = sender.CachedClassName ?? ""; } catch { }
            try { name = sender.CachedName ?? ""; } catch { }
            try { ct = sender.CachedControlType; } catch { }
            who = $"{cls}'{Trunc(name, 20)}'";

            if (ct is CT_TOOLTIP or CT_MENU or CT_MENUITEM) return true;
            foreach (var ig in IGNORE_CLASSES)
                if (cls.Equals(ig, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsContainer(IUIAutomationElement sender, out string who)
        {
            string cls = "", name = "";
            try { cls = sender.CachedClassName ?? ""; } catch { }
            try { name = sender.CachedName ?? ""; } catch { }
            who = $"{cls}'{Trunc(name, 20)}'";
            foreach (var cc in CONTAINER_CLASSES)
                if (cls.Equals(cc, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void FlagReconcile(string trigger)
        {
            // Raw per-event trace (diagnostic): shows every event that passed the
            // identity filter. A visible drift with NO [event] line here means the
            // taskbar moved with no UIA signal (gate F — labels/never-combine mode).
            Log($"  [event] {trigger}");
            _reconcilePending = true;
            Interlocked.Exchange(ref _lastReqMs, Clock.ElapsedMilliseconds);
            Interlocked.Increment(ref _lastTriggerTag);
            _wake.Set();
        }

        // ---- logging + formatting helpers ---------------------------------------

        private static void Log(string message)
        {
            string stamped = $"{DateTime.Now:HH:mm:ss.fff} +{Clock.ElapsedMilliseconds,7}ms  {message}";
            lock (LogGate)
            {
                Console.WriteLine(stamped);
                _logFile?.WriteLine(stamped);
            }
        }

        /// <summary>Tiny ILogger that tees the real overlay's own logging into our transcript.</summary>
        private sealed class ProbeLogger : ILogger
        {
            public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;
            public void Debug(string m, string mn = "", string fp = "") { if (MinimumLevel <= LogLevel.Debug) Log($"  [overlay:dbg] {m}"); }
            public void Info(string m, string mn = "", string fp = "") => Log($"  [overlay] {m}");
            public void Warning(string m, string mn = "", string fp = "") => Log($"  [overlay:WARN] {m}");
            public void Error(string m, Exception? ex = null, string mn = "", string fp = "") => Log($"  [overlay:ERR] {m} {ex?.Message}");
        }

        private static string RidToString(Array? rid)
        {
            if (rid == null) return "";
            var parts = new List<string>(rid.Length);
            foreach (var v in rid) parts.Add(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "");
            return string.Join(".", parts);
        }

        private static string RectToString(tagRECT r) =>
            $"(x={r.left},y={r.top},w={r.right - r.left},h={r.bottom - r.top})";

        private static string Trunc(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

        private static string ControlTypeName(int ct) => ct switch
        {
            CT_BUTTON => "Button",
            50032 => "Window",
            50033 => "Pane",
            50026 => "Group",
            50020 => "Text",
            CT_TOOLTIP => "ToolTip",
            _ => ct.ToString(),
        };

        // ---- WinEvent hook: a second (MSAA) pipeline for the UIA-silent cases ----

        private delegate void WinEventProc(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const uint EVENT_OBJECT_CREATE = 0x8000;      // range start, hook A
        private const uint EVENT_OBJECT_REORDER = 0x8004;     // range end,   hook A
        private const uint EVENT_OBJECT_STATECHANGE = 0x800A; // hook B
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private static void InstallWinEventHook()
        {
            uint tbThread = GetWindowThreadProcessId(_hwnd, out uint pid);
            if (tbThread == 0)
            {
                Log("!! WinEvent: GetWindowThreadProcessId(Shell_TrayWnd) returned 0 — not hooking.");
                return;
            }

            _winEventProc = OnWinEvent; // pin against GC

            // SCOPE (#84 iter 2026-10-08): hook PROCESS-WIDE on explorer.exe
            // (idProcess=pid, idThread=0) rather than thread-scoped to the taskbar
            // thread. The Task View toggle's MSAA events fire on the DESKTOP window
            // (#32769 / WorkerW) — a DIFFERENT explorer thread than Shell_TrayWnd —
            // so a taskbar-thread-scoped hook missed them, making last session's
            // "did a WinEvent fire?" ambiguous. Both the tray and the desktop belong
            // to explorer, so a process-wide hook covers every candidate thread while
            // still excluding unrelated apps. OnWinEvent logs the source thread id so
            // we can tell WHICH explorer thread the dependable signal rides.
            // Hook A: CREATE/DESTROY/SHOW/HIDE/REORDER (0x8000..0x8004) — add/remove +
            // the visibility-collapse case (Task View toggle, if it HIDEs the button).
            _winHookObj = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_REORDER,
                IntPtr.Zero, _winEventProc, pid, 0, WINEVENT_OUTOFCONTEXT);
            // Hook B: STATECHANGE (0x800A) — enabled/visible state flips.
            _winHookState = SetWinEventHook(EVENT_OBJECT_STATECHANGE, EVENT_OBJECT_STATECHANGE,
                IntPtr.Zero, _winEventProc, pid, 0, WINEVENT_OUTOFCONTEXT);

            Log($"Subscribed: WinEvents PROCESS-WIDE on explorer pid {pid} (taskbar thread {tbThread}) — " +
                $"CREATE..REORDER hooked={_winHookObj != IntPtr.Zero}, STATECHANGE hooked={_winHookState != IntPtr.Zero}. " +
                "Second pipeline for the UIA-StructureChanged-silent cases (e.g. Task View toggle).");
        }

        private static void RemoveWinEventHook()
        {
            try { if (_winHookObj != IntPtr.Zero) UnhookWinEvent(_winHookObj); } catch { }
            try { if (_winHookState != IntPtr.Zero) UnhookWinEvent(_winHookState); } catch { }
            _winHookObj = _winHookState = IntPtr.Zero;
            _winEventProc = null;
        }

        // Fires on the STA pump thread (OUTOFCONTEXT). Logs the raw event, then flags a
        // (debounced) reconcile. Scoped to the taskbar thread so volume is bounded; if a
        // specific event floods (e.g. the clock), the log will show it and we narrow.
        private static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd,
            int idObject, int idChild, uint thread, uint time)
        {
            string cls = GetClassOf(hwnd);
            string name = WinEventName(ev);
            Log($"  [winevent] {name} thread={thread} hwnd=0x{hwnd.ToInt64():X} {cls} obj={idObject} child={idChild}");
            FlagReconcile($"WINEVENT {name} {cls} t{thread}");
        }

        private static string GetClassOf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "<null hwnd>";
            var sb = new StringBuilder(256);
            int n = GetClassName(hwnd, sb, sb.Capacity);
            return n > 0 ? sb.ToString() : "<?>";
        }

        private static string WinEventName(uint ev) => ev switch
        {
            0x8000 => "CREATE",
            0x8001 => "DESTROY",
            0x8002 => "SHOW",
            0x8003 => "HIDE",
            0x8004 => "REORDER",
            0x800A => "STATECHANGE",
            _ => $"0x{ev:X}",
        };

        // ---- global hotkeys: drive the probe without interactive stdin ----------

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004;
        private const int HK_SNAPSHOT = 1, HK_RECONCILE = 2, HK_QUIT = 3;
        private const uint VK_S = 0x53, VK_R = 0x52, VK_Q = 0x51;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // A message-only NativeWindow on the STA thread that owns the WM_HOTKEY queue.
        private sealed class HotkeyWindow : NativeWindow
        {
            public HotkeyWindow()
            {
                CreateHandle(new CreateParams());
                const uint mods = MOD_CONTROL | MOD_ALT | MOD_SHIFT;
                bool s = RegisterHotKey(Handle, HK_SNAPSHOT, mods, VK_S);
                bool r = RegisterHotKey(Handle, HK_RECONCILE, mods, VK_R);
                bool q = RegisterHotKey(Handle, HK_QUIT, mods, VK_Q);
                Log($"Global hotkeys registered: Ctrl+Alt+Shift+S dump={s}, +R reconcile={r}, +Q quit={q}. " +
                    "(false = combo already taken by another app — tell me and I'll rebind.)");
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    switch (m.WParam.ToInt32())
                    {
                        case HK_SNAPSHOT:
                            _snapshotRequested = true; _wake.Set();
                            break;
                        case HK_RECONCILE:
                            _manualReconcile = true; _reconcilePending = true;
                            Interlocked.Exchange(ref _lastReqMs, Clock.ElapsedMilliseconds); _wake.Set();
                            break;
                        case HK_QUIT:
                            RequestExit();
                            break;
                    }
                }
                base.WndProc(ref m);
            }

            public void Teardown()
            {
                try { UnregisterHotKey(Handle, HK_SNAPSHOT); } catch { }
                try { UnregisterHotKey(Handle, HK_RECONCILE); } catch { }
                try { UnregisterHotKey(Handle, HK_QUIT); } catch { }
                try { DestroyHandle(); } catch { }
            }
        }

        // ---- registry-notify watcher: the UIA+WinEvent-silent settings class ----

        private const uint KEY_NOTIFY = 0x0010;
        private const uint REG_NOTIFY_CHANGE_LAST_SET = 0x0004;
        private static readonly UIntPtr HKEY_CURRENT_USER = new(0x80000001u);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegOpenKeyExW(UIntPtr hKey, string subKey, uint ulOptions, uint samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegNotifyChangeKeyValue(IntPtr hKey,
            [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree, uint dwNotifyFilter,
            IntPtr hEvent, [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr hKey);

        private static void StartRegistryWatch()
        {
            _regWatch = new Thread(RegWatchMain) { Name = "TrayOverlayProbe-RegWatch", IsBackground = true };
            _regWatch.Start();
        }

        // Dedicated thread. Opens the two Explorer settings keys for change-notify and
        // waits (asynchronously) on them; any value write fires the reconcile. The
        // notify is one-shot, so each key is re-armed after it signals. No polling.
        private static void RegWatchMain()
        {
            const string advPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
            const string searchPath = @"Software\Microsoft\Windows\CurrentVersion\Search";

            int r1 = RegOpenKeyExW(HKEY_CURRENT_USER, advPath, 0, KEY_NOTIFY, out IntPtr advKey);
            int r2 = RegOpenKeyExW(HKEY_CURRENT_USER, searchPath, 0, KEY_NOTIFY, out IntPtr searchKey);
            Log($"Reg-watch: opened Explorer\\Advanced (rc={r1}) + Search (rc={r2}) — " +
                "catches Task View / Widgets / alignment / combine / search-box. " +
                "Event-driven (RegNotifyChangeKeyValue), NO poll.");

            using var advEvt = new AutoResetEvent(false);
            using var searchEvt = new AutoResetEvent(false);

            void ArmAdv()
            {
                if (advKey != IntPtr.Zero)
                    RegNotifyChangeKeyValue(advKey, false, REG_NOTIFY_CHANGE_LAST_SET, advEvt.SafeWaitHandle.DangerousGetHandle(), true);
            }
            void ArmSearch()
            {
                if (searchKey != IntPtr.Zero)
                    RegNotifyChangeKeyValue(searchKey, false, REG_NOTIFY_CHANGE_LAST_SET, searchEvt.SafeWaitHandle.DangerousGetHandle(), true);
            }

            ArmAdv();
            ArmSearch();

            WaitHandle[] handles = { advEvt, searchEvt, _shutdown.WaitHandle };
            while (!_shutdown.IsSet)
            {
                int idx = WaitHandle.WaitAny(handles);
                if (idx == 2 || _shutdown.IsSet) break;
                if (idx == 0) { FlagReconcile(@"REGNOTIFY Explorer\Advanced"); ArmAdv(); }
                else if (idx == 1) { FlagReconcile(@"REGNOTIFY Explorer\Search"); ArmSearch(); }
            }

            if (advKey != IntPtr.Zero) RegCloseKey(advKey);
            if (searchKey != IntPtr.Zero) RegCloseKey(searchKey);
        }

        // ---- console wiring (WinExe has no console by default) -------------------

        private static void OpenConsole()
        {
            PInvoke.AllocConsole();
            var stdout = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdout);
            var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            Console.SetIn(stdin);
            try { Console.Title = "Probe 2a — Event-Driven Tray Overlay (#84)"; } catch { }
        }

        private static void CloseConsole()
        {
            try { PInvoke.FreeConsole(); } catch { }
        }
    }
}
