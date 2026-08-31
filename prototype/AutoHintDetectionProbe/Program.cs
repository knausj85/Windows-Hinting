// PROTOTYPE — throwaway. Wayfinder ticket #45: WinEvent + UIA detection & surface
// classification for transient shell surfaces. Do NOT merge; do NOT productionize.
//
// What it does: installs WinEvent hooks (foreground / menu / object-show) and managed
// UIA MenuOpened/MenuClosed handlers, and logs every event with the data the Talon
// `update_state` heuristic keys off (active-window class, focused-element control type +
// name, parent control type + name), plus a proposed surface classification and the
// delivery latency. You drive it by hand: run it, then open each v1-catalog surface and
// each app in the MenuOpened matrix, and read the log.
//
// Output: console + auto-hint-detection-probe.log in the working directory.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using UIA = UIAutomationClient; // native COM UIA client (aliased to avoid TreeScope/name clashes)

namespace AutoHintDetectionProbe;

internal static class Program
{
    // ---- log plumbing ------------------------------------------------------
    private static readonly object LogLock = new();
    private static StreamWriter _file = null!;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static void Log(string line)
    {
        string stamped = $"[+{Clock.ElapsedMilliseconds,7}ms] {line}";
        lock (LogLock)
        {
            Console.WriteLine(stamped);
            _file.WriteLine(stamped);
            _file.Flush();
        }
    }

    // Classes worth logging for the very noisy EVENT_OBJECT_SHOW/HIDE/CREATE stream.
    // This IS the "cheap relevance filter" the map flagged as unspecified — anything
    // outside this set is dropped so the object-event log stays readable.
    private static readonly HashSet<string> ObjectEventClassAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "#32768",                            // classic Win32 context menu (shell context menus)
        "Xaml_WindowedPopupClass",           // Win11 XAML popups / PopupHost (taskbar context menus)
        "Windows.UI.Core.CoreWindow",        // Start / Search / Notification Center / jump lists
        "ControlCenterWindow",               // Control Center (quick settings)
        "XamlExplorerHostIslandWindow",      // Task View / Snap Assist
        "TopLevelWindowForOverflowXamlIsland", // system-tray overflow (Win11)
        "Shell_TrayWnd",                     // taskbar
        "#32770",                            // dialog
    };

    [STAThread]
    private static void Main()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* redirected stdout */ }
        string logPath = Path.Combine(Environment.CurrentDirectory, "auto-hint-detection-probe.log");
        _file = new StreamWriter(logPath, append: false) { AutoFlush = true };

        PrintBanner(logPath);

        // ---- WinEvent hooks ------------------------------------------------
        // Keep the delegate rooted so it isn't collected while the hook is live.
        _winEventProc = OnWinEvent;

        // System range: foreground + menu lifecycle (0x0003..0x0007).
        IntPtr hSystem = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_MENUPOPUPEND,
            IntPtr.Zero, _winEventProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        // Object range: create/show/hide (0x8000..0x8003) — filtered by class allow-list.
        IntPtr hObject = SetWinEventHook(
            EVENT_OBJECT_CREATE, EVENT_OBJECT_HIDE,
            IntPtr.Zero, _winEventProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        Log($"WinEvent hooks installed: system={(hSystem != IntPtr.Zero)} object={(hObject != IntPtr.Zero)}");

        // ---- managed UIA MenuOpened / MenuClosed ---------------------------
        // Subtree at the desktop root — deliberately broad, because the whole
        // question is whether these fire at all across apps. If registration or
        // delivery is flaky/slow, THAT is the finding.
        bool uiaOk = false;
        try
        {
            Automation.AddAutomationEventHandler(
                AutomationElement.MenuOpenedEvent, AutomationElement.RootElement,
                TreeScope.Subtree, OnMenuOpened);
            Automation.AddAutomationEventHandler(
                AutomationElement.MenuClosedEvent, AutomationElement.RootElement,
                TreeScope.Subtree, OnMenuClosed);
            uiaOk = true;
        }
        catch (Exception ex)
        {
            Log($"!! UIA MenuOpened/MenuClosed registration FAILED: {ex.GetType().Name}: {ex.Message}");
        }
        Log($"UIA (managed) MenuOpened/MenuClosed handlers registered: {uiaOk}");

        // ---- native COM UIA events (round 2) --------------------------------
        // The managed client can't express these; the COM client can. Testing
        // whether they give a usable signal where MenuOpened failed (VS Code /
        // GitHub Desktop), and a cleaner open/close for the shell CoreWindows.
        bool comOk = false;
        try
        {
            _com = new UIA.CUIAutomation();
            var root = _com.GetRootElement();
            _comHandler = new ComEventHandler();
            foreach (int evId in new[]
            {
                UIA_Window_WindowOpenedEventId,
                UIA_Window_WindowClosedEventId,
                UIA_MenuModeStartEventId,
                UIA_MenuModeEndEventId,
            })
            {
                _com.AddAutomationEventHandler(
                    evId, root, UIA.TreeScope.TreeScope_Subtree, null, _comHandler);
            }
            comOk = true;
        }
        catch (Exception ex)
        {
            Log($"!! COM UIA event registration FAILED: {ex.GetType().Name}: {ex.Message}");
        }
        Log($"UIA (COM) WindowOpened/Closed + MenuModeStart/End handlers registered: {comOk}");
        Log("──────────────────────────────────────────────────────────────────────");
        Log("READY. Open a surface, or type a label + Enter to drop a MARK divider.");
        Log("──────────────────────────────────────────────────────────────────────");

        // stdin marker thread: lets you annotate the log ("start menu" <Enter>)
        // right before you open each surface, so events are easy to attribute.
        var marker = new Thread(MarkerLoop) { IsBackground = true };
        marker.Start();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = false;
            try { if (hSystem != IntPtr.Zero) UnhookWinEvent(hSystem); } catch { }
            try { if (hObject != IntPtr.Zero) UnhookWinEvent(hObject); } catch { }
            try { Automation.RemoveAllEventHandlers(); } catch { }
            try { _com?.RemoveAllEventHandlers(); } catch { }
            Log("Shutting down.");
        };

        // Message pump — required for OUTOFCONTEXT WinEvent callbacks.
        System.Windows.Forms.Application.Run(new System.Windows.Forms.ApplicationContext());
    }

    private static void MarkerLoop()
    {
        while (true)
        {
            string? line = Console.ReadLine();
            if (line is null) return; // stdin closed
            Log($"════════════ MARK: {line} ════════════");
        }
    }

    // ---- WinEvent callback -------------------------------------------------
    private static WinEventDelegate _winEventProc = null!;

    private static void OnWinEvent(
        IntPtr hHook, uint ev, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        try
        {
            string name = EventName(ev);

            // Object create/destroy/show/hide is a firehose — drop anything not in the
            // allow-list and anything that isn't the window object itself
            // (idObject 0 = OBJID_WINDOW). Covers the whole 0x8000..0x8003 range so
            // DESTROY (0x8001) is filtered too, not just the three we name explicitly.
            bool isObjectRange = ev is >= EVENT_OBJECT_CREATE and <= EVENT_OBJECT_HIDE;
            string cls = GetClass(hwnd);
            if (isObjectRange)
            {
                if (idObject != 0) return;
                if (!ObjectEventClassAllowList.Contains(cls)) return;
            }

            long latency = unchecked((uint)Environment.TickCount - dwmsEventTime);
            string title = GetText(hwnd);
            string proc = ProcName(hwnd);

            var sb = new StringBuilder();
            sb.Append($"WINEVENT {name,-22} lat={latency,4}ms hwnd=0x{hwnd.ToInt64():X8} ");
            sb.Append($"class='{cls}' title='{Trunc(title, 40)}' proc={proc}");

            // Focused-element snapshot: the exact fields the Talon update_state rules read.
            var (fCtl, fName, pCtl, pName) = SnapshotFocus();
            sb.Append($"\n            focus: ctl={fCtl} name='{Trunc(fName, 30)}' | parent: ctl={pCtl} name='{Trunc(pName, 30)}'");
            sb.Append($"\n            → classify: {Classify(cls, title, fCtl, fName, pCtl, pName)}");

            Log(sb.ToString());
        }
        catch (Exception ex)
        {
            Log($"!! OnWinEvent threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- native COM UIA events (round 2) ----------------------------------
    private static UIA.IUIAutomation? _com;
    private static ComEventHandler? _comHandler;

    // UIA event IDs (UIAutomationClient.h). Managed System.Windows.Automation
    // exposes none of these four.
    private const int UIA_Window_WindowOpenedEventId = 20016;
    private const int UIA_Window_WindowClosedEventId = 20017;
    private const int UIA_MenuModeStartEventId = 20018;
    private const int UIA_MenuModeEndEventId = 20019;

    private sealed class ComEventHandler : UIA.IUIAutomationEventHandler
    {
        public void HandleAutomationEvent(UIA.IUIAutomationElement sender, int eventId)
        {
            string which = eventId switch
            {
                UIA_Window_WindowOpenedEventId => "Win.WindowOpened",
                UIA_Window_WindowClosedEventId => "Win.WindowClosed",
                UIA_MenuModeStartEventId => "MenuModeStart",
                UIA_MenuModeEndEventId => "MenuModeEnd",
                _ => $"evt{eventId}",
            };
            // sender is frequently null for MenuMode* and WindowClosed, and any
            // property read on a torn-down element can throw — guard everything.
            string ctl = "<null>", nm = "", cls = "", proc = "";
            if (sender is not null)
            {
                ctl = "?"; nm = "?"; cls = "?"; proc = "?";
                try { ctl = ControlTypeName(sender.CurrentControlType); } catch { }
                try { nm = sender.CurrentName ?? ""; } catch { }
                try { cls = sender.CurrentClassName ?? ""; } catch { }
                try { proc = SafeProcName(sender.CurrentProcessId); } catch { }
            }
            Log($"UIA-COM  {which,-22} ctl={ctl} name='{Trunc(nm, 30)}' class='{cls}' proc={proc}");
        }
    }

    private static readonly Dictionary<int, string> ControlTypeIds = new()
    {
        [50000] = "Button", [50002] = "CheckBox", [50003] = "ComboBox", [50004] = "Edit",
        [50007] = "ListItem", [50008] = "List", [50009] = "Menu", [50010] = "MenuBar",
        [50011] = "MenuItem", [50018] = "Tab", [50019] = "TabItem", [50020] = "Text",
        [50021] = "ToolBar", [50022] = "ToolTip", [50023] = "Tree", [50024] = "TreeItem",
        [50025] = "Custom", [50026] = "Group", [50030] = "Document", [50031] = "SplitButton",
        [50032] = "Window", [50033] = "Pane", [50037] = "TitleBar", [50038] = "Separator",
    };
    private static string ControlTypeName(int id) =>
        ControlTypeIds.TryGetValue(id, out var n) ? n : id.ToString();

    // ---- managed UIA menu callbacks ---------------------------------------
    private static void OnMenuOpened(object? sender, AutomationEventArgs e) => LogMenu("MenuOpened", sender);
    private static void OnMenuClosed(object? sender, AutomationEventArgs e) => LogMenu("MenuClosed", sender);

    private static void LogMenu(string which, object? sender)
    {
        try
        {
            var el = sender as AutomationElement;
            string ctl = "?", nm = "?", cls = "?", proc = "?";
            if (el is not null)
            {
                try { ctl = el.Current.ControlType.ProgrammaticName; } catch { }
                try { nm = el.Current.Name; } catch { }
                try { cls = el.Current.ClassName; } catch { }
                try { proc = SafeProcName(el.Current.ProcessId); } catch { }
            }
            Log($"UIA      {which,-22} ctl={ctl} name='{Trunc(nm, 30)}' class='{cls}' proc={proc}");
        }
        catch (Exception ex)
        {
            Log($"!! {which} handler threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- focused-element snapshot -----------------------------------------
    private static (string fCtl, string fName, string pCtl, string pName) SnapshotFocus()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null) return ("<none>", "", "", "");
            string fCtl = SafeCtl(focused);
            string fName = SafeName(focused);
            string pCtl = "", pName = "";
            try
            {
                var parent = TreeWalker.ControlViewWalker.GetParent(focused);
                if (parent is not null) { pCtl = SafeCtl(parent); pName = SafeName(parent); }
            }
            catch { }
            return (fCtl, fName, pCtl, pName);
        }
        catch (Exception ex)
        {
            return ($"<err:{ex.GetType().Name}>", "", "", "");
        }
    }

    // ---- classification (Talon update_state, ported as the starting seed) --
    // Returns the proposed surface label so you can eyeball whether the seed rules
    // still hold on this machine. Refine these predicates from what the log shows.
    private static string Classify(
        string cls, string title, string fCtl, string fName, string pCtl, string pName)
    {
        // Focused-element-first rules (menus), independent of the host window class.
        if (fCtl == "MenuItem" && pCtl is "Menu" or "ToolBar" or "Window" or "MenuBar")
            return "MENU (strategy: element-override=parent)";
        if (fCtl == "Menu")
            return "MENU (strategy: focused-element)";
        if (fCtl == "Group" && pCtl == "MenuItem")
            return "MENU (strategy: focused-element→first-parent-window)";
        if (pCtl == "ToolBar")
            return "MENU (strategy: focused-element-parent)";
        if (fCtl == "Window" && fName == "Popup")
            return "MENU (strategy: element-override=focused)";

        // Window-class rules.
        switch (cls)
        {
            case "#32768":
                return "GENERIC_CONTEXT_MENU (classic Win32 menu; strategy: element-override=window)";
            case "#32770":
                return "OPEN_DIALOG (strategy: active-window)";
            case "Xaml_WindowedPopupClass":
                return "GENERIC_CONTEXT_MENU / XAML popup (strategy: element-override=window)";
            case "Windows.UI.Core.CoreWindow":
                if (fName == "Notification Center") return "NOTIFICATION_CENTER (strategy: active-window)";
                if (title.Contains("Jump List")) return "JUMP_LIST_CONTEXT_MENU (strategy: active-window)";
                if (title == "Search") return "START_MENU / SEARCH (strategy: active-window[-parent])";
                if (fName == "Search box") return "START_MENU (strategy: active-window-parent)";
                return "CoreWindow (AMBIGUOUS — needs Start/Search/NotifCenter disambiguation)";
            case "ControlCenterWindow":
                return "CONTROL_CENTER";
            case "XamlExplorerHostIslandWindow":
                if (title == "Task View") return "TASK_VIEW (strategy: active-window)";
                if (title == "Snap Assist") return "SNAP_ASSIST (strategy: active-window)";
                return "XamlExplorerHostIsland (Task View / Snap Assist — disambiguate by title)";
            case "TopLevelWindowForOverflowXamlIsland":
                return "SYSTEM_TRAY (overflow; strategy: active-window)";
            case "Shell_TrayWnd":
                if (fName == "Search box") return "START_MENU (weird traywnd case)";
                if (pName == "Running applications") return "TASK_VIEW";
                return "taskbar (Shell_TrayWnd — usually NONE unless search/taskview)";
            default:
                if (title == "System tray overflow window.") return "SYSTEM_TRAY (strategy: active-window)";
                return "NONE / unrecognized (candidate for a new catalog rule)";
        }
    }

    // ---- small helpers -----------------------------------------------------
    private static string SafeCtl(AutomationElement el)
    {
        try { return el.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""); }
        catch { return "?"; }
    }
    private static string SafeName(AutomationElement el)
    {
        try { return el.Current.Name ?? ""; }
        catch { return ""; }
    }
    private static string SafeProcName(int pid)
    {
        try { return $"{Process.GetProcessById(pid).ProcessName}({pid})"; }
        catch { return $"pid{pid}"; }
    }
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
        var sb = new StringBuilder(256);
        return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }
    private static string ProcName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "?";
        GetWindowThreadProcessId(hwnd, out uint pid);
        return SafeProcName((int)pid);
    }

    private static string EventName(uint ev) => ev switch
    {
        EVENT_SYSTEM_FOREGROUND => "SYSTEM_FOREGROUND",
        EVENT_SYSTEM_MENUSTART => "SYSTEM_MENUSTART",
        EVENT_SYSTEM_MENUEND => "SYSTEM_MENUEND",
        EVENT_SYSTEM_MENUPOPUPSTART => "SYSTEM_MENUPOPUPSTART",
        EVENT_SYSTEM_MENUPOPUPEND => "SYSTEM_MENUPOPUPEND",
        EVENT_OBJECT_CREATE => "OBJECT_CREATE",
        EVENT_OBJECT_DESTROY => "OBJECT_DESTROY",
        EVENT_OBJECT_SHOW => "OBJECT_SHOW",
        EVENT_OBJECT_HIDE => "OBJECT_HIDE",
        _ => $"0x{ev:X4}",
    };

    private static void PrintBanner(string logPath)
    {
        Log("AutoHintDetectionProbe — wayfinder #45 detection spike (THROWAWAY)");
        Log($"Logging to: {logPath}");
        Log("");
        Log("Manual test checklist — open each, watch which events fire + the classify line:");
        Log("  v1 catalog:  Start menu · Search · Notification Center · Control Center ·");
        Log("               System-tray overflow · taskbar jump list (right-click a pinned app) ·");
        Log("               Task View · Snap Assist (Win+Z / drag-snap) ·");
        Log("               shell context menu (right-click desktop) · a Win11 XAML flyout");
        Log("  MenuOpened matrix — open a menu bar / context menu in EACH and note if UIA fires:");
        Log("               VS Code · GitHub Desktop · Visual Studio · File Explorer");
        Log("  Round 2 (COM): also watch UIA-COM Win.WindowOpened/Closed + MenuModeStart/End");
        Log("               — focus the problem apps (VS Code, GitHub Desktop).");
        Log("  Tip: type a label + Enter (e.g. 'START MENU') right before opening it to mark the log.");
        Log("  Ctrl+C to quit.");
        Log("");
    }

    // ---- P/Invoke ----------------------------------------------------------
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MENUSTART = 0x0004;
    private const uint EVENT_SYSTEM_MENUEND = 0x0005;
    private const uint EVENT_SYSTEM_MENUPOPUPSTART = 0x0006;
    private const uint EVENT_SYSTEM_MENUPOPUPEND = 0x0007;
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_HIDE = 0x8003;

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
