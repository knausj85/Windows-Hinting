// PROTOTYPE — throwaway. Wayfinder ticket #59 (parent map #54): scroll-exposure probe.
// Do NOT merge; do NOT productionize.
//
// THE QUESTION (map #54): how reliably do real apps expose scrollability to UI
// Automation? Working hypothesis: ScrollPattern exposure is inconsistent and often
// false-negative (visibly-scrollable content reports non-scrollable / exposes no
// ScrollPattern at all). This probe censuses, per focused window, EVERY UIA scroll
// signal present on every candidate element, and then OBJECTIVELY tests whether a
// UIA-driven scroll actually moves the content — independent of what the flags claim.
//
// WHAT IT DOES: press the global hotkey (Ctrl+Alt+S) while any app is focused. The
// probe walks that window's UIA subtree, server-side-filtered to elements that bear a
// scroll signal (ScrollPattern OR ScrollItemPattern OR RangeValuePattern OR a ScrollBar
// control type), and for each dumps:
//   ControlType, Name, AutomationId, FrameworkId, ProviderDescription, BoundingRect,
//   ScrollPattern.{Vertically/HorizontallyScrollable, V/H ViewSize, V/H ScrollPercent},
//   ScrollItemPattern presence, RangeValuePattern {Value, Min, Max}.
// Then, for every element that exposes a live ScrollPattern with a non-sentinel percent,
// it ATTEMPTS a small UIA scroll (Scroll(SmallIncrement); SetScrollPercent fallback),
// records whether VerticalScrollPercent actually changed ("did a UIA-scroll move it"),
// and RESTORES the original position. This ground-truth column is the point: it does
// not trust the *Scrollable flags.
//
// The operator drives ground truth for reality: focus a region KNOWN to scroll in each
// target app, hit the hotkey, then annotate the "REALITY" line the probe prints (which
// region really scrolls) against what UIA claimed + whether the UIA-scroll worked.
//
// TRIGGERS: on-demand global hotkey (Ctrl+Alt+S) is the workhorse — it does not steal
// focus, so GetForegroundWindow() still points at the app. `auto` toggles capture on
// every foreground change (the ticket's "focused-window change" trigger); off by default
// because for a targeted census the operator picks regions deliberately. Menu-open /
// title-change auto-triggers from the ticket are intentionally NOT implemented — they
// don't bear on scroll-region exposure; noted in the README as a scoped refinement.
//
// Output: console + scroll-exposure-probe.log in the working directory.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using UIA = UIAutomationClient; // native COM UIA client

namespace ScrollExposureProbe;

internal static class Program
{
    // ---- log plumbing ------------------------------------------------------
    private static readonly object LogLock = new();
    private static StreamWriter _file = null!;

    private static void Log(string line)
    {
        lock (LogLock)
        {
            Console.WriteLine(line);
            _file.WriteLine(line);
            _file.Flush();
        }
    }

    // ---- UIA constants (UIAutomationClient.h) ------------------------------
    private const int UIA_RangeValuePatternId = 10003;
    private const int UIA_ScrollPatternId = 10004;
    private const int UIA_ScrollItemPatternId = 10017;

    private const int UIA_ControlTypePropertyId = 30003;
    private const int UIA_IsRangeValuePatternAvailablePropertyId = 30033;
    private const int UIA_IsScrollPatternAvailablePropertyId = 30034;
    private const int UIA_IsScrollItemPatternAvailablePropertyId = 30035;
    private const int UIA_ProviderDescriptionPropertyId = 30107;

    // ScrollPattern value properties (read for display; ground truth uses the typed
    // IUIAutomationScrollPattern.CurrentVerticalScrollPercent double instead).
    private const int UIA_ScrollHorizontalScrollPercentPropertyId = 30053;
    private const int UIA_ScrollHorizontalViewSizePropertyId = 30054;
    private const int UIA_ScrollVerticalScrollPercentPropertyId = 30055;
    private const int UIA_ScrollVerticalViewSizePropertyId = 30056;
    private const int UIA_ScrollHorizontallyScrollablePropertyId = 30057;
    private const int UIA_ScrollVerticallyScrollablePropertyId = 30058;

    private const int UIA_ScrollBarControlTypeId = 50014;

    // ScrollPattern "this axis can't scroll" sentinel for the percent properties.
    private const double UiaScrollPatternNoScroll = -1.0;

    private static UIA.IUIAutomation _com = null!;
    private static int _captureSeq;
    private static volatile bool _autoCapture;
    private static IntPtr _ownConsoleHwnd;

    [STAThread]
    private static void Main()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* redirected stdout */ }
        string logPath = Path.Combine(Environment.CurrentDirectory, "scroll-exposure-probe.log");
        _file = new StreamWriter(logPath, append: false) { AutoFlush = true };
        _ownConsoleHwnd = GetForegroundWindow(); // console is foreground at launch

        try { _com = new UIA.CUIAutomation(); }
        catch (Exception ex) { Log($"!! FATAL: could not create CUIAutomation: {ex.GetType().Name}: {ex.Message}"); return; }

        PrintBanner(logPath);

        // ---- foreground WinEvent hook (auto-capture mode) ------------------
        _winEventProc = OnWinEvent;
        IntPtr hFg = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        Log($"Foreground WinEvent hook installed: {hFg != IntPtr.Zero} (auto-capture starts OFF; type 'auto' to toggle)");

        // ---- global hotkey (message-only window sink) ----------------------
        var sink = new HotkeySink();
        bool hk = RegisterHotKey(sink.Handle, HotkeyId, MOD_CONTROL | MOD_ALT, VK_S);
        Log($"Global hotkey Ctrl+Alt+S registered: {hk}"
            + (hk ? "" : "  — registration FAILED; use the 3-second 'cap' fallback instead."));
        Log("──────────────────────────────────────────────────────────────────────");
        Log("READY. Focus a scrollable region in a target app, then press Ctrl+Alt+S.");
        Log("Commands (type + Enter):  cap = capture foreground 3s from now  |  auto = toggle");
        Log("foreground auto-capture  |  <any text> = drop a MARK divider  |  Ctrl+C = quit.");
        Log("──────────────────────────────────────────────────────────────────────");

        var marker = new Thread(MarkerLoop) { IsBackground = true };
        marker.Start();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = false;
            try { if (hk) UnregisterHotKey(sink.Handle, HotkeyId); } catch { }
            try { if (hFg != IntPtr.Zero) UnhookWinEvent(hFg); } catch { }
            Log("Shutting down.");
        };

        Application.Run(new ApplicationContext());
    }

    // ---- hotkey sink -------------------------------------------------------
    private const int HotkeyId = 0xB01D;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002;
    private const uint VK_S = 0x53;
    private const int WM_HOTKEY = 0x0312;

    private sealed class HotkeySink : NativeWindow
    {
        public HotkeySink() => CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                IntPtr fg = GetForegroundWindow();
                CaptureWindow(fg, "hotkey Ctrl+Alt+S");
            }
            base.WndProc(ref m);
        }
    }
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    // ---- stdin command loop ------------------------------------------------
    private static void MarkerLoop()
    {
        while (true)
        {
            string? line = Console.ReadLine();
            if (line is null) return;
            string cmd = line.Trim().ToLowerInvariant();
            switch (cmd)
            {
                case "auto":
                    _autoCapture = !_autoCapture;
                    Log($"════════ auto-capture on foreground change: {(_autoCapture ? "ON" : "OFF")} ════════");
                    break;
                case "cap":
                    Log("cap: capturing the foreground window in 3s — click your target app NOW…");
                    var t = new Thread(() =>
                    {
                        Thread.Sleep(3000);
                        CaptureWindow(GetForegroundWindow(), "cap (3s delayed)");
                    }) { IsBackground = true };
                    t.SetApartmentState(ApartmentState.STA);
                    t.Start();
                    break;
                default:
                    Log($"════════════ MARK: {line} ════════════");
                    break;
            }
        }
    }

    // ---- foreground auto-capture -------------------------------------------
    private static WinEventDelegate _winEventProc = null!;
    private static IntPtr _lastAutoHwnd;

    private static void OnWinEvent(
        IntPtr hHook, uint ev, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (!_autoCapture || ev != EVENT_SYSTEM_FOREGROUND) return;
        if (hwnd == IntPtr.Zero || hwnd == _ownConsoleHwnd) return;
        if (idObject != 0) return; // window object only
        if (hwnd == _lastAutoHwnd) return;
        _lastAutoHwnd = hwnd;
        try { CaptureWindow(hwnd, "auto: foreground change"); }
        catch (Exception ex) { Log($"!! auto-capture threw: {ex.GetType().Name}: {ex.Message}"); }
    }

    // ---- the census --------------------------------------------------------
    private static void CaptureWindow(IntPtr hwnd, string trigger)
    {
        var sw = Stopwatch.StartNew();
        int seq = ++_captureSeq;
        try
        {
            if (hwnd == IntPtr.Zero) { Log($"!! CAPTURE #{seq}: no foreground window"); return; }
            string proc = ProcName(hwnd), title = GetText(hwnd), cls = GetClass(hwnd);

            UIA.IUIAutomationElement winEl;
            try { winEl = _com.ElementFromHandle(hwnd); }
            catch (Exception ex) { Log($"!! CAPTURE #{seq}: ElementFromHandle failed: {ex.GetType().Name}: {ex.Message}"); return; }

            string winFw = SafeProp(() => winEl.CurrentFrameworkId);

            // Server-side OR condition: any element bearing a scroll signal.
            UIA.IUIAutomationCondition cond = _com.CreateOrCondition(
                _com.CreatePropertyCondition(UIA_IsScrollPatternAvailablePropertyId, true),
                _com.CreateOrCondition(
                    _com.CreatePropertyCondition(UIA_IsScrollItemPatternAvailablePropertyId, true),
                    _com.CreateOrCondition(
                        _com.CreatePropertyCondition(UIA_IsRangeValuePatternAvailablePropertyId, true),
                        _com.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_ScrollBarControlTypeId))));

            UIA.IUIAutomationElementArray found;
            try { found = winEl.FindAll(UIA.TreeScope.TreeScope_Subtree, cond); }
            catch (Exception ex) { Log($"!! CAPTURE #{seq}: FindAll failed: {ex.GetType().Name}: {ex.Message}"); return; }

            int n = found?.Length ?? 0;

            // Single pass: dump each candidate into a buffer and tally by signal, so we
            // can print a signal summary up top. Weak ScrollItem-only nodes (XAML/Blink
            // put ScrollItemPattern on nearly everything) get one compact line so a
            // Chromium page's hundreds of them don't bury the real containers.
            var tally = new Tally();
            var full = new List<string>();
            var weak = new List<string>();
            for (int i = 0; i < n; i++)
            {
                UIA.IUIAutomationElement el;
                try { el = found!.GetElement(i); } catch { continue; }
                DumpCandidate(seq, i, el, tally, full, weak);
            }

            Log("");
            Log($"════════ CAPTURE #{seq}  [{trigger}] ════════");
            Log($"  proc={proc}  hwnd=0x{hwnd.ToInt64():X8}  windowClass='{cls}'  windowFramework={winFw}");
            Log($"  title='{Trunc(title, 60)}'");
            Log($"  scroll-signal candidates in subtree: {n}   (FindAll {sw.ElapsedMilliseconds}ms)");
            Log($"  signals: ScrollPattern={tally.ScrollPattern} (of those, *Scrollable=True: {tally.ScrollPatternScrollable}, " +
                $"UIA-scroll MOVED: {tally.GroundTruthMoved}) · ScrollBar-controltype={tally.ScrollBar} · " +
                $"RangeValue={tally.RangeValue} · weak ScrollItem-only={weak.Count}");
            if (n == 0)
                Log("  → NO scroll signals anywhere in this window's UIA subtree. " +
                    "If the operator can visibly scroll it, this is a FALSE-NEGATIVE (canvas/virtual/terminal).");

            foreach (var line in full) Log(line);
            if (weak.Count > 0)
            {
                Log($"  ── weak (ScrollItem-only, no container signal) × {weak.Count}: ──");
                foreach (var line in weak) Log(line);
            }

            Log($"  ↳ REALITY (operator — which of the above ACTUALLY scroll, and what did you scroll?): ____________");
            Log($"════════ /CAPTURE #{seq}  ({sw.ElapsedMilliseconds}ms total) ════════");
        }
        catch (Exception ex)
        {
            Log($"!! CAPTURE #{seq} threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class Tally
    {
        public int ScrollPattern, ScrollPatternScrollable, GroundTruthMoved, ScrollBar, RangeValue;
    }

    private static void DumpCandidate(
        int seq, int idx, UIA.IUIAutomationElement el, Tally tally, List<string> full, List<string> weak)
    {
        int ctlId = 0;
        try { ctlId = el.CurrentControlType; } catch { }
        string ctl = ControlTypeName(ctlId);
        string name = Trunc(SafeProp(() => el.CurrentName), 34);
        string autoId = Trunc(SafeProp(() => el.CurrentAutomationId), 24);
        string fw = SafeProp(() => el.CurrentFrameworkId);
        string rect = RectStr(el);

        UIA.IUIAutomationScrollPattern? sp = null;
        try { sp = el.GetCurrentPattern(UIA_ScrollPatternId) as UIA.IUIAutomationScrollPattern; } catch { }
        bool scrollItem = false;
        try { scrollItem = el.GetCurrentPattern(UIA_ScrollItemPatternId) is not null; } catch { }
        UIA.IUIAutomationRangeValuePattern? rv = null;
        try { rv = el.GetCurrentPattern(UIA_RangeValuePatternId) as UIA.IUIAutomationRangeValuePattern; } catch { }
        bool isScrollBar = ctlId == UIA_ScrollBarControlTypeId;

        // Weak = ScrollItemPattern only, no container-level signal. Compact one-liner.
        if (scrollItem && sp is null && rv is null && !isScrollBar)
        {
            weak.Add($"     · [{seq}.{idx}] ctl={ctl} name='{name}' autoId='{autoId}' rect={rect}");
            return;
        }

        string provider = Trunc(PropStr(el, UIA_ProviderDescriptionPropertyId), 60);
        full.Add($"  [{seq}.{idx}] ctl={ctl} name='{name}' autoId='{autoId}' fw={fw} rect={rect}");
        if (provider.Length > 0) full.Add($"         provider='{provider}'");

        // ---- ScrollPattern (the authoritative signal) ----
        if (sp is not null)
        {
            tally.ScrollPattern++;
            string vScroll = PropStr(el, UIA_ScrollVerticallyScrollablePropertyId);
            string hScroll = PropStr(el, UIA_ScrollHorizontallyScrollablePropertyId);
            string vView = PropStr(el, UIA_ScrollVerticalViewSizePropertyId);
            string hView = PropStr(el, UIA_ScrollHorizontalViewSizePropertyId);
            string vPct = PropStr(el, UIA_ScrollVerticalScrollPercentPropertyId);
            string hPct = PropStr(el, UIA_ScrollHorizontalScrollPercentPropertyId);
            if (vScroll == "True" || hScroll == "True") tally.ScrollPatternScrollable++;
            full.Add($"         ScrollPattern: vScrollable={vScroll} hScrollable={hScroll} | vViewSize={vView} hViewSize={hView} | vPct={vPct} hPct={hPct}");
            string gt = GroundTruthScroll(sp);
            if (gt.StartsWith("MOVED")) tally.GroundTruthMoved++;
            full.Add($"         GROUND-TRUTH: {gt}");
        }
        else
        {
            full.Add("         ScrollPattern: ABSENT");
        }

        // ---- ScrollItemPattern (presence only; ≠ container scrollable) ----
        if (scrollItem)
            full.Add("         ScrollItemPattern: present (element can be scrolled into view; NOT proof its container is scrollable)");

        // ---- RangeValuePattern (weak/fallback: a ScrollBar exposes this) ----
        if (rv is not null)
        {
            tally.RangeValue++;
            string val = SafeProp(() => rv.CurrentValue.ToString("F1", CultureInfo.InvariantCulture));
            string min = SafeProp(() => rv.CurrentMinimum.ToString("F1", CultureInfo.InvariantCulture));
            string max = SafeProp(() => rv.CurrentMaximum.ToString("F1", CultureInfo.InvariantCulture));
            full.Add($"         RangeValuePattern: value={val} min={min} max={max} (scroll-signal only if max>min AND this is a scrollbar)");
        }

        if (isScrollBar)
        {
            tally.ScrollBar++;
            full.Add("         ControlType=ScrollBar: structural hint that a sibling/ancestor is scrollable (no IScrollProvider of its own)");
        }
    }

    // Objectively test whether a UIA scroll moves the content, then restore.
    private static string GroundTruthScroll(UIA.IUIAutomationScrollPattern sp)
    {
        double before;
        try { before = sp.CurrentVerticalScrollPercent; }
        catch (Exception ex) { return $"could not read VerticalScrollPercent: {ex.GetType().Name}"; }

        if (before <= UiaScrollPatternNoScroll)
            return $"n/a — vPct={before:F0} (NoScroll sentinel: this axis reports non-scrollable)";

        double after = before;
        bool moved = false;
        string how = "";

        // 1) relative small increment (least disruptive)
        try
        {
            sp.Scroll(UIA.ScrollAmount.ScrollAmount_NoAmount, UIA.ScrollAmount.ScrollAmount_SmallIncrement);
            after = sp.CurrentVerticalScrollPercent;
            if (Math.Abs(after - before) > 0.01) { moved = true; how = "Scroll(SmallIncrement)"; }
        }
        catch { /* try absolute next */ }

        // 2) absolute SetScrollPercent toward the far end
        if (!moved)
        {
            double target = before < 50 ? Math.Min(100, before + 25) : Math.Max(0, before - 25);
            try
            {
                sp.SetScrollPercent(UiaScrollPatternNoScroll, target);
                after = sp.CurrentVerticalScrollPercent;
                if (Math.Abs(after - before) > 0.01) { moved = true; how = "SetScrollPercent"; }
            }
            catch (Exception ex) { how = $"both attempts threw ({ex.GetType().Name})"; }
        }

        // restore
        try { sp.SetScrollPercent(UiaScrollPatternNoScroll, before); } catch { }

        return moved
            ? $"MOVED via {how}: vPct {before:F1} → {after:F1}  (restored)"
            : $"NO-MOVE: vPct stayed {before:F1} despite the attempt{(how.Length > 0 ? " (" + how + ")" : "")}";
    }

    // ---- helpers -----------------------------------------------------------
    private static string SafeProp(Func<string?> f)
    {
        try { return f() ?? ""; } catch { return "?"; }
    }

    private static string PropStr(UIA.IUIAutomationElement el, int propId)
    {
        try
        {
            object? v = el.GetCurrentPropertyValue(propId);
            if (v is null) return "?";
            if (v is double d) return d.ToString("F1", CultureInfo.InvariantCulture);
            if (v is bool b) return b ? "True" : "False";
            return v.ToString() ?? "?";
        }
        catch { return "?"; }
    }

    private static string RectStr(UIA.IUIAutomationElement el)
    {
        try
        {
            UIA.tagRECT r = el.CurrentBoundingRectangle;
            return $"({r.left},{r.top} {r.right - r.left}x{r.bottom - r.top})";
        }
        catch { return "?"; }
    }

    private static readonly Dictionary<int, string> ControlTypeIds = new()
    {
        [50000] = "Button", [50001] = "Calendar", [50002] = "CheckBox", [50003] = "ComboBox",
        [50004] = "Edit", [50005] = "Hyperlink", [50006] = "Image", [50007] = "ListItem",
        [50008] = "List", [50009] = "Menu", [50010] = "MenuBar", [50011] = "MenuItem",
        [50012] = "ProgressBar", [50013] = "RadioButton", [50014] = "ScrollBar", [50015] = "Slider",
        [50016] = "Spinner", [50017] = "StatusBar", [50018] = "Tab", [50019] = "TabItem",
        [50020] = "Text", [50021] = "ToolBar", [50022] = "ToolTip", [50023] = "Tree",
        [50024] = "TreeItem", [50025] = "Custom", [50026] = "Group", [50027] = "Thumb",
        [50028] = "DataGrid", [50029] = "DataItem", [50030] = "Document", [50031] = "SplitButton",
        [50032] = "Window", [50033] = "Pane", [50034] = "Header", [50035] = "HeaderItem",
        [50036] = "Table", [50037] = "TitleBar", [50038] = "Separator",
    };
    private static string ControlTypeName(int id) =>
        ControlTypeIds.TryGetValue(id, out var nm) ? nm : (id == 0 ? "?" : id.ToString());

    private static string Trunc(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "…");

    private static string GetClass(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "<null-hwnd>";
        var sb = new StringBuilder(256);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "<no-class>";
    }
    private static string GetText(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "";
        var sb = new StringBuilder(512);
        return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }
    private static string ProcName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "?";
        GetWindowThreadProcessId(hwnd, out uint pid);
        try { return $"{Process.GetProcessById((int)pid).ProcessName}({pid})"; }
        catch { return $"pid{pid}"; }
    }

    private static void PrintBanner(string logPath)
    {
        Log("ScrollExposureProbe — wayfinder #59 scroll-exposure census (THROWAWAY)");
        Log($"Logging to: {logPath}");
        Log("");
        Log("Census protocol — for EACH target app:");
        Log("  1. Focus a region you KNOW scrolls (click into it, or scroll it once by hand).");
        Log("  2. Type a MARK label + Enter (e.g. 'gmail message list') to tag the log.");
        Log("  3. Press Ctrl+Alt+S. Read the candidates + the GROUND-TRUTH line for each.");
        Log("  4. Fill the REALITY line: which region really scrolls, and what you scrolled.");
        Log("Matrix (7): GitHub Desktop · Slack native · Slack PWA (Edge) · VSCode ·");
        Log("            Gmail (Edge tab) · File Explorer · Windows Terminal.");
        Log("Read each candidate as: signal(s) present → what UIA claims → did a UIA-scroll");
        Log("actually move it → does that match reality? A 0-candidate window on visibly-");
        Log("scrollable content is the headline false-negative case.");
        Log("");
    }

    // ---- P/Invoke ----------------------------------------------------------
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
