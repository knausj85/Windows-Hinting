// =============================================================================
// PROTOTYPE — Taskbar / tray overlay event-vs-poll TELEMETRY PROBE   (issue #83)
// =============================================================================
// THROWAWAY. Lives only on branch `prototype/taskbar-overlay-probe` off `main`.
// Do NOT ship, do NOT merge to main. Reuses the real app's UIAutomationClient
// COM interop + CsWin32 so results are representative (map #81, Substrate note).
//
// WHAT IT ANSWERS (PF-1, the map's biggest open risk):
//   Do the Win11 primary-taskbar parents under Shell_TrayWnd actually RAISE
//   StructureChanged (icon add/remove) and BoundingRectangle property-changes
//   (reflow/move) — and at what latency / volume — or not? If the signals are
//   absent/useless on both the tray parent and the app-row parent, that is the
//   NO-GO-on-pure-events off-ramp (#48 flags P2 "do the real surfaces raise it?"
//   and P3 "actual volume").
//
// HOW (per prior art #48 / #82):
//   * All UIA subscribe/handle on a dedicated MTA, non-UI thread.
//   * A cache request is passed to the handlers so the sender arrives pre-cached
//     and we read Cached* in the callback (no cross-apartment Current* reads).
//   * One subscription per signal, TreeScope.Subtree on the Shell_TrayWnd ROOT,
//     so BOTH the tray parent and the app-row parent are blanketed — PF-1 can't
//     be missed by guessing the wrong tree path. The per-fire sender identity +
//     an initial subtree dump attribute each fire to tray vs app-row.
//   * BoundingRectangle has no OS coalescing (#82): an N-icon reflow fires N
//     events, so we log raw fires AND a debounced burst summary to read volume.
//   * Gate E (display change) uses SystemEvents.DisplaySettingsChanged, which
//     self-hosts its own message-pump thread (#82) — no HWND/WndProc needed.
//
// RUN:  Windows-Hinting.exe --probe-taskbar
//   A console opens. Drive the scenarios by hand, watch the fires, then press
//   Enter to stop. A full transcript is written next to the exe as
//   taskbar-probe-<timestamp>.log — that file is the deliverable for #83.
//   Commands while running:  [S]+Enter = re-dump the subtree snapshot (compare
//   child bounds before/after a reflow),  Enter = quit.
//
// SCENARIOS TO DRIVE (map #81 gated stress set):
//   C  start/exit a background tray app      -> tray icon add/remove
//   A  launch/close a windowed app           -> app-row button add/remove
//   B  (centered taskbar) add a button       -> do the OTHER buttons' bounds
//                                               fire BoundingRectangle (reflow)?
//   E  change resolution/scale, connect/disconnect a monitor -> display signal
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using UIAutomationClient;
using Windows.Win32;

namespace WindowsHinting
{
    /// <summary>
    /// PROTOTYPE (#83). Throwaway console telemetry probe — see file header.
    /// </summary>
    internal static class TaskbarOverlayProbe
    {
        private static readonly object LogGate = new();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static StreamWriter? _logFile;

        // Entry point, reached from Program.Main when args contain --probe-taskbar.
        public static void Run()
        {
            OpenConsole();
            string logPath = Path.Combine(
                AppContext.BaseDirectory,
                $"taskbar-probe-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _logFile = new StreamWriter(logPath, append: false) { AutoFlush = true };

            Log("=== TASKBAR OVERLAY EVENT PROBE (#83) ===");
            Log($"log file: {logPath}");
            Log($"OS: {Environment.OSVersion}  .NET: {Environment.Version}  64-bit proc: {Environment.Is64BitProcess}");
            Log("Subscribing on a dedicated MTA thread (per #48)...");
            Log("");

            using var ready = new ManualResetEventSlim(false);
            using var shutdown = new ManualResetEventSlim(false);
            Exception? startupError = null;

            var worker = new Thread(() => WorkerMain(ready, shutdown, ref startupError))
            {
                Name = "TaskbarProbe-UIA-MTA",
                IsBackground = true,
            };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();

            // Wait for subscription to finish (or fail) before taking input.
            ready.Wait();
            if (startupError != null)
            {
                Log($"!! STARTUP FAILED: {startupError.Message}");
                Log("Press Enter to exit.");
                Console.ReadLine();
                shutdown.Set();
                CloseConsole();
                return;
            }

            Log("");
            Log(">>> READY. Drive scenarios A/B/C/E now. [S]+Enter = snapshot, Enter = quit. <<<");
            Log("");

            while (true)
            {
                string? line = Console.ReadLine();
                if (line == null || line.Trim().Length == 0)
                    break;
                if (line.Trim().Equals("S", StringComparison.OrdinalIgnoreCase))
                    _snapshotRequested.Set();
                else
                    break;
            }

            Log("");
            Log("Stopping — unsubscribing...");
            shutdown.Set();
            worker.Join(3000);
            Log("=== PROBE ENDED ===");
            _logFile.Flush();
            _logFile.Dispose();
            CloseConsole();
        }

        // A snapshot request made from the console, serviced on the MTA thread.
        private static readonly ManualResetEventSlim _snapshotRequested = new(false);

        // ---- MTA worker: all UIA work happens here ------------------------------

        private static void WorkerMain(ManualResetEventSlim ready, ManualResetEventSlim shutdown, ref Exception? startupError)
        {
            IUIAutomation? automation = null;
            try
            {
                automation = new CUIAutomation();

                IntPtr hwnd = PInvoke.FindWindow("Shell_TrayWnd", null);
                if (hwnd == IntPtr.Zero)
                    throw new InvalidOperationException("FindWindow('Shell_TrayWnd') returned 0 — no primary taskbar?");
                Log($"Shell_TrayWnd hwnd = 0x{hwnd.ToInt64():X}");

                IUIAutomationElement root = automation.ElementFromHandle(hwnd);
                if (root == null)
                    throw new InvalidOperationException("ElementFromHandle(Shell_TrayWnd) returned null.");

                // Cache request: pre-populate what the handlers read off the sender,
                // so callbacks never do a cross-apartment Current* read (#48/#82).
                var cache = automation.CreateCacheRequest();
                cache.TreeScope = TreeScope.TreeScope_Element;
                cache.AddProperty(UIA_PropertyIds.UIA_NamePropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_ClassNamePropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_ControlTypePropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_BoundingRectanglePropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_AutomationIdPropertyId);

                // --- Initial subtree dump: half the result (identify the parents). ---
                Log("---- INITIAL Shell_TrayWnd SUBTREE (ControlView, depth<=5) ----");
                DumpTree(automation, root, 0, maxDepth: 5, maxChildrenPerLevel: 60);
                Log("---- END SUBTREE ----");

                var structHandler = new StructHandler();
                var propHandler = new PropHandler();

                // One subscription per signal, Subtree on the root — blankets tray + app row.
                automation.AddStructureChangedEventHandler(
                    root, TreeScope.TreeScope_Subtree, cache, structHandler);
                Log("Subscribed: StructureChanged (Subtree on Shell_TrayWnd root).");

                automation.AddPropertyChangedEventHandler(
                    root, TreeScope.TreeScope_Subtree, cache, propHandler,
                    new[] { UIA_PropertyIds.UIA_BoundingRectanglePropertyId });
                Log("Subscribed: BoundingRectangle PropertyChanged (Subtree on Shell_TrayWnd root).");

                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                Log("Subscribed: SystemEvents.DisplaySettingsChanged (gate E).");

                ready.Set();

                // Idle until shutdown; service snapshot requests. MTA event callbacks
                // arrive on RPC worker threads, so no message pump is needed here.
                while (!shutdown.IsSet)
                {
                    if (_snapshotRequested.IsSet)
                    {
                        _snapshotRequested.Reset();
                        Log("---- SNAPSHOT (on request) ----");
                        try
                        {
                            var freshRoot = automation.ElementFromHandle(hwnd);
                            DumpTree(automation, freshRoot, 0, maxDepth: 5, maxChildrenPerLevel: 60);
                        }
                        catch (Exception ex) { Log($"snapshot failed: {ex.Message}"); }
                        Log("---- END SNAPSHOT ----");
                    }
                    Thread.Sleep(100);
                }

                // Shutdown: remove handlers OFF the callback path (#48).
                try { automation.RemoveAllEventHandlers(); } catch (Exception ex) { Log($"RemoveAllEventHandlers: {ex.Message}"); }
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
            Log($"[DISPLAY] SystemEvents.DisplaySettingsChanged  (primary bounds now {System.Windows.Forms.Screen.PrimaryScreen?.Bounds})");
        }

        // ---- Subtree dump --------------------------------------------------------

        private static void DumpTree(IUIAutomation automation, IUIAutomationElement element, int depth, int maxDepth, int maxChildrenPerLevel)
        {
            if (element == null || depth > maxDepth)
                return;

            Log(new string(' ', depth * 2) + Describe(element));

            if (depth == maxDepth)
                return;

            IUIAutomationTreeWalker walker = automation.ControlViewWalker;
            IUIAutomationElement? child = null;
            try { child = walker.GetFirstChildElement(element); } catch { }

            int count = 0;
            while (child != null && count < maxChildrenPerLevel)
            {
                IUIAutomationElement? next = null;
                try { next = walker.GetNextSiblingElement(child); } catch { }
                DumpTree(automation, child, depth + 1, maxDepth, maxChildrenPerLevel);
                if (Marshal.IsComObject(child)) { try { Marshal.ReleaseComObject(child); } catch { } }
                child = next;
                count++;
            }
            if (count >= maxChildrenPerLevel)
                Log(new string(' ', (depth + 1) * 2) + $"... (truncated at {maxChildrenPerLevel} children)");
        }

        private static string Describe(IUIAutomationElement e)
        {
            string name = "?", cls = "?", autoId = "?", rid = "?", bounds = "?";
            int ct = 0;
            try { name = e.CurrentName ?? ""; } catch { }
            try { cls = e.CurrentClassName ?? ""; } catch { }
            try { autoId = e.CurrentAutomationId ?? ""; } catch { }
            try { ct = e.CurrentControlType; } catch { }
            try { rid = RidToString(e.GetRuntimeId()); } catch { }
            try { bounds = RectToString(e.CurrentBoundingRectangle); } catch { }
            return $"type={ControlTypeName(ct)} class='{cls}' name='{Trunc(name, 40)}' autoId='{autoId}' rid=[{rid}] bounds={bounds}";
        }

        // ---- COM event handler implementations ----------------------------------

        private sealed class StructHandler : IUIAutomationStructureChangedEventHandler
        {
            private int _count;
            public void HandleStructureChangedEvent(IUIAutomationElement sender, StructureChangeType changeType, Array runtimeId)
            {
                int n = Interlocked.Increment(ref _count);
                string who = DescribeCached(sender);
                // Per #48 the payload runtimeId is only populated on ChildRemoved.
                string payloadRid = runtimeId is { Length: > 0 } ? RidToString(runtimeId as int[]) : "(none)";
                Log($"[STRUCT #{n}] {changeType}  payloadRid=[{payloadRid}]  sender: {who}");
            }
        }

        private sealed class PropHandler : IUIAutomationPropertyChangedEventHandler
        {
            private int _count;
            private long _lastFireMs;
            private int _burst;

            public void HandlePropertyChangedEvent(IUIAutomationElement sender, int propertyId, object newValue)
            {
                if (propertyId != UIA_PropertyIds.UIA_BoundingRectanglePropertyId)
                    return;

                int n = Interlocked.Increment(ref _count);
                long now = Clock.ElapsedMilliseconds;
                long gap = now - Interlocked.Exchange(ref _lastFireMs, now);

                // Burst accounting: fires within 300ms of each other are one reflow burst.
                if (gap <= 300) Interlocked.Increment(ref _burst);
                else Interlocked.Exchange(ref _burst, 1);

                string rect = newValue is double[] d && d.Length == 4 ? RectToString(d) : "(not double[4])";
                string who = DescribeCached(sender);
                Log($"[BRECT  #{n}] gap={gap,5}ms burst={_burst,3}  newBounds={rect}  sender: {who}");
            }
        }

        private static string DescribeCached(IUIAutomationElement e)
        {
            string name = "?", cls = "?", rid = "?";
            int ct = 0;
            try { name = e.CachedName ?? ""; } catch { }
            try { cls = e.CachedClassName ?? ""; } catch { }
            try { ct = e.CachedControlType; } catch { }
            try { rid = RidToString(e.GetRuntimeId()); } catch { }
            return $"type={ControlTypeName(ct)} class='{cls}' name='{Trunc(name, 40)}' rid=[{rid}]";
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

        private static string RidToString(Array? rid)
        {
            if (rid == null) return "";
            var parts = new List<string>(rid.Length);
            foreach (var v in rid) parts.Add(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "");
            return string.Join(".", parts);
        }

        // UIA element bounds come back as tagRECT (left/top/right/bottom, screen px).
        private static string RectToString(tagRECT r) =>
            $"(x={r.left},y={r.top},w={r.right - r.left},h={r.bottom - r.top})";

        // The BoundingRectangle PropertyChanged VARIANT payload is a double[4] = x,y,w,h.
        private static string RectToString(double[] r) =>
            r.Length == 4
                ? $"(x={r[0].ToString("0", CultureInfo.InvariantCulture)},y={r[1].ToString("0", CultureInfo.InvariantCulture)},w={r[2].ToString("0", CultureInfo.InvariantCulture)},h={r[3].ToString("0", CultureInfo.InvariantCulture)})"
                : "(bad rect)";

        private static string Trunc(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

        private static string ControlTypeName(int ct) => ct switch
        {
            0 => "-",
            50000 => "Button",
            50008 => "List",
            50007 => "ListItem",
            50019 => "TabItem",
            50020 => "Text",
            50021 => "ToolBar",
            50026 => "Group",
            50032 => "Window",
            50033 => "Pane",
            _ => ct.ToString(),
        };

        // ---- console wiring (WinExe has no console by default) -------------------

        private static void OpenConsole()
        {
            PInvoke.AllocConsole();
            var stdout = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdout);
            var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            Console.SetIn(stdin);
            try { Console.Title = "Taskbar Overlay Event Probe (#83)"; } catch { }
        }

        private static void CloseConsole()
        {
            try { PInvoke.FreeConsole(); } catch { }
        }
    }
}
