// PROTOTYPE — throwaway. Wayfinder ticket #89 (populate the built-in window rules), parent map #62.
// Headless UIA dump: for each top-level window of a target app, prints what a window rule needs —
// root FrameworkId, the control-view tree of containers (controlType / className / automationId /
// name), scroll candidates (ScrollPattern + view sizes + owning HWND visibility), a control-type
// histogram with actionable-pattern counts (missing click targets), and the web Document URL read.
//
//   RuleProbe <process-name | title-substring> [--all] [--depth N] [--warm]
//     --all    print every control-view node, not just containers / scroll candidates
//     --depth  max tree depth (default 40)
//     --warm   query once and wait 2s before the real scan (Chromium/Electron accessibility warm-up)
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UIA = UIAutomationClient;

internal static class Program
{
    const int P_Rect = 30001, P_CT = 30003, P_LCT = 30004, P_Name = 30005, P_Aid = 30011, P_Cls = 30012,
              P_Hwnd = 30020, P_Offscreen = 30022, P_Fw = 30024,
              P_HasInvoke = 30031, P_HasExpand = 30028, P_HasSelItem = 30036, P_HasToggle = 30041,
              P_HasScroll = 30034, P_HasValue = 30043, P_HasRange = 30033, P_HasLegacy = 30090,
              P_HViewSize = 30054, P_VViewSize = 30056, P_HScrollable = 30057, P_VScrollable = 30058,
              P_LegacyDefAction = 30100, P_AriaRole = 30101, P_Landmark = 30157;
    const int CT_Document = 50030, CT_ScrollBar = 50014;
    const int PAT_Value = 10002;

    static readonly int[] Props = { P_Rect, P_CT, P_LCT, P_Name, P_Aid, P_Cls, P_Hwnd, P_Offscreen, P_Fw,
        P_HasInvoke, P_HasExpand, P_HasSelItem, P_HasToggle, P_HasScroll, P_HasValue, P_HasRange, P_HasLegacy,
        P_HViewSize, P_VViewSize, P_HScrollable, P_VScrollable, P_LegacyDefAction, P_AriaRole, P_Landmark };

    // containers worth printing in the default (non --all) tree view
    static readonly HashSet<int> Containers = new() { 50033 /*Pane*/, 50026 /*Group*/, 50008 /*List*/, 50023 /*Tree*/,
        50030 /*Document*/, 50028 /*DataGrid*/, 50036 /*Table*/, 50018 /*Tab*/, 50025 /*Custom*/, 50014 /*ScrollBar*/,
        50004 /*Edit*/, 50032 /*Window*/, 50021 /*ToolBar*/, 50010 /*MenuBar*/, 50019 /*TabItem*/ };

    static UIA.CUIAutomation8 _uia = null!;
    static bool _all; static int _maxDepth = 40;

    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("usage: RuleProbe <process|title-substring> [--all] [--depth N] [--warm]"); return 2; }
        string target = args[0];
        _all = args.Contains("--all");
        bool warm = args.Contains("--warm");
        int di = Array.IndexOf(args, "--depth"); if (di >= 0 && di + 1 < args.Length) _maxDepth = int.Parse(args[di + 1]);
        _uia = new UIA.CUIAutomation8();

        var wins = FindWindows(target);
        if (wins.Count == 0) { Console.WriteLine($"no visible top-level window matches '{target}'"); return 1; }
        if (warm)
        {
            foreach (var h in wins) try { _uia.ElementFromHandle(h).FindAll(UIA.TreeScope.TreeScope_Subtree, _uia.CreateTrueCondition()); } catch { }
            Thread.Sleep(2000);
        }
        foreach (var h in wins) DumpWindow(h);
        return 0;
    }

    static List<IntPtr> FindWindows(string target)
    {
        var list = new List<IntPtr>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            string title = Title(h); if (title.Length == 0) return true;
            GetWindowThreadProcessId(h, out uint pid);
            string proc = ""; try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { }
            if (proc.Equals(target, StringComparison.OrdinalIgnoreCase) || title.Contains(target, StringComparison.OrdinalIgnoreCase)) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    sealed class Hist { public int N, Invoke, Toggle, SelItem, Expand, Legacy, Offscreen; }

    static void DumpWindow(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        string proc = ""; try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { }
        Console.WriteLine($"\n==== 0x{hwnd.ToInt64():X} class='{ClassOf(hwnd)}' proc={proc} title='{Title(hwnd)}'");

        var req = _uia.CreateCacheRequest();
        foreach (int p in Props) req.AddProperty(p);
        req.TreeScope = UIA.TreeScope.TreeScope_Subtree;
        req.TreeFilter = _uia.ControlViewCondition;

        var sw = Stopwatch.StartNew();
        UIA.IUIAutomationElement root;
        try { root = _uia.ElementFromHandleBuildCache(hwnd, req); }
        catch (Exception ex) { Console.WriteLine($"  BuildCache failed: {ex.Message}"); return; }
        Console.WriteLine($"  root FrameworkId='{S(root, P_Fw)}' cached tree in {sw.ElapsedMilliseconds} ms");

        var hist = new SortedDictionary<string, Hist>();
        var scroll = new List<string>();
        int nodes = 0;
        Console.WriteLine("  -- tree (control view" + (_all ? ", all nodes" : ", containers + scroll candidates") + ")");
        Walk(root, 0, hwnd, hist, scroll, ref nodes);

        Console.WriteLine($"  -- {nodes} nodes. control-type histogram: count | invoke toggle selItem expand legacyDefAction | offscreen");
        foreach (var (ct, h) in hist)
            Console.WriteLine($"     {ct,-14} {h.N,5} | {h.Invoke,4} {h.Toggle,4} {h.SelItem,4} {h.Expand,4} {h.Legacy,4} | {h.Offscreen,4}");

        Console.WriteLine($"  -- scroll candidates (ScrollPattern or ScrollBar): {scroll.Count}");
        foreach (var s in scroll) Console.WriteLine("     " + s);

        DumpUrl(root);
    }

    static void Walk(UIA.IUIAutomationElement el, int depth, IntPtr ownerHwnd, SortedDictionary<string, Hist> hist, List<string> scroll, ref int nodes)
    {
        nodes++;
        int ct = I(el, P_CT);
        int nh = I(el, P_Hwnd); if (nh != 0) ownerHwnd = (IntPtr)nh;
        string ctn = CtName(ct);
        if (!hist.TryGetValue(ctn, out var h)) hist[ctn] = h = new Hist();
        h.N++;
        if (B(el, P_HasInvoke)) h.Invoke++;
        if (B(el, P_HasToggle)) h.Toggle++;
        if (B(el, P_HasSelItem)) h.SelItem++;
        if (B(el, P_HasExpand)) h.Expand++;
        if (S(el, P_LegacyDefAction).Length > 0) h.Legacy++;
        bool off = B(el, P_Offscreen); if (off) h.Offscreen++;

        bool hasScroll = B(el, P_HasScroll);
        string line = Describe(el, ct);
        string scrollInfo = "";
        if (hasScroll)
        {
            scrollInfo = $" SP(v={D(el, P_VViewSize):F0}{(B(el, P_VScrollable) ? "*" : "")} h={D(el, P_HViewSize):F0}{(B(el, P_HScrollable) ? "*" : "")})";
        }
        if (hasScroll || ct == CT_ScrollBar)
        {
            string vis = ownerHwnd == IntPtr.Zero ? "?" : (IsWindowVisible(ownerHwnd) ? "vis" : "HIDDEN");
            scroll.Add($"{line}{scrollInfo} hwnd=0x{ownerHwnd.ToInt64():X}('{ClassOf(ownerHwnd)}',{vis}){(off ? " OFFSCREEN" : "")}");
        }
        if (_all || Containers.Contains(ct) || hasScroll)
            Console.WriteLine($"  {new string(' ', Math.Min(depth, 60))}{line}{scrollInfo}{(off ? " off" : "")}");

        if (depth >= _maxDepth) return;
        UIA.IUIAutomationElementArray kids;
        try { kids = el.GetCachedChildren(); } catch { return; }
        if (kids is null) return;
        for (int i = 0; i < kids.Length; i++) Walk(kids.GetElement(i), depth + 1, ownerHwnd, hist, scroll, ref nodes);
    }

    static string Describe(UIA.IUIAutomationElement el, int ct)
    {
        var sb = new StringBuilder(CtName(ct));
        string cls = S(el, P_Cls), aid = S(el, P_Aid), name = S(el, P_Name), lct = S(el, P_LCT), fw = S(el, P_Fw);
        if (cls.Length > 0) sb.Append($" cls='{Trunc(cls, 70)}'");
        if (aid.Length > 0) sb.Append($" aid='{Trunc(aid, 50)}'");
        if (name.Length > 0) sb.Append($" name='{Trunc(name, 50)}'");
        if (lct.Length > 0 && !lct.Equals(CtName(ct), StringComparison.OrdinalIgnoreCase)) sb.Append($" lct='{lct}'");
        string role = S(el, P_AriaRole); int lm = I(el, P_Landmark);
        if (role.Length > 0) sb.Append($" role={role}");
        if (lm != 0) sb.Append($" landmark={lm}");
        if (fw.Length > 0) sb.Append($" fw={fw}");
        var r = Rect(el); sb.Append($" [{r.l},{r.t} {r.w}x{r.h}]");
        return sb.ToString();
    }

    static void DumpUrl(UIA.IUIAutomationElement root)
    {
        var sw = Stopwatch.StartNew();
        UIA.IUIAutomationElement? doc = null;
        try { doc = root.FindFirst(UIA.TreeScope.TreeScope_Descendants, _uia.CreatePropertyCondition(P_CT, CT_Document)); } catch { }
        if (doc is null) { Console.WriteLine($"  -- first Document: none ({sw.ElapsedMilliseconds} ms)"); return; }
        string val = "";
        try { if (doc.GetCurrentPattern(PAT_Value) is UIA.IUIAutomationValuePattern vp) val = vp.CurrentValue ?? ""; } catch { }
        string name = ""; try { name = doc.CurrentName ?? ""; } catch { }
        string cls = ""; try { cls = doc.CurrentClassName ?? ""; } catch { }
        Console.WriteLine($"  -- first Document: cls='{cls}' name='{Trunc(name, 60)}' value='{Trunc(val, 120)}' ({sw.ElapsedMilliseconds} ms)");
    }

    // ---- cached-property helpers ----
    static object? V(UIA.IUIAutomationElement el, int p) { try { return el.GetCachedPropertyValue(p); } catch { return null; } }
    static string S(UIA.IUIAutomationElement el, int p) => V(el, p) as string ?? "";
    static int I(UIA.IUIAutomationElement el, int p) => V(el, p) is int i ? i : 0;
    static bool B(UIA.IUIAutomationElement el, int p) => V(el, p) is bool b && b;
    static double D(UIA.IUIAutomationElement el, int p) => V(el, p) is double d ? d : -1;
    static (int l, int t, int w, int h) Rect(UIA.IUIAutomationElement el)
        => V(el, P_Rect) is double[] a && a.Length == 4 ? ((int)a[0], (int)a[1], (int)a[2], (int)a[3]) : (0, 0, 0, 0);
    static string Trunc(string s, int n) { s = s.Replace("\r", " ").Replace("\n", " "); return s.Length <= n ? s : s[..n] + "…"; }

    static readonly string[] CtNames = { "Button", "Calendar", "CheckBox", "ComboBox", "Edit", "Hyperlink", "Image", "ListItem",
        "List", "Menu", "MenuBar", "MenuItem", "ProgressBar", "RadioButton", "ScrollBar", "Slider", "Spinner", "StatusBar", "Tab",
        "TabItem", "Text", "ToolBar", "ToolTip", "Tree", "TreeItem", "Custom", "Group", "Thumb", "DataGrid", "DataItem", "Document",
        "SplitButton", "Window", "Pane", "Header", "HeaderItem", "Table", "TitleBar", "Separator", "SemanticZoom", "AppBar" };
    static string CtName(int ct) => ct >= 50000 && ct - 50000 < CtNames.Length ? CtNames[ct - 50000] : ct.ToString();

    // ---- Win32 ----
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    static string Title(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }
    static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
}
