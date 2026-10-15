// PROTOTYPE — throwaway. Do NOT merge; do NOT productionize.
//
// PagingProbe: turns docs/research/uia-paging-without-scrollpattern.md's untested suggestions into
// measurements. For a target window it finds candidate scrollable elements, then for each
// candidate x mechanism x command (LineDown, PageDown, PageUp, ToTop, ToBottom) it records the
// position before/after through every oracle it can read (GetScrollInfo, native top index, UIA
// scroll percent, TextPattern first visible line, ScrollItem first visible item, ScrollBar
// RangeValue, PrintWindow pixel hash) plus side effects (cursor, foreground, focus, selection).
// The target is kept in the BACKGROUND: the probe puts its own small "decoy" window in front.
//
// Usage:
//   PagingProbe --target <processName|titleSubstring> [--hwnd 0xHEX] [--label L]
//               [--name substr] [--point x,y] [--cand 1,3] [--mech SP,WMV,...] [--max 8]
//               [--list] [--no-decoy] [--out dir]
//
// Mechanisms (ids):
//   SP      UIA ScrollPattern on the element or nearest ancestor (raw view)
//   WMV     SendMessage(WM_VSCROLL, SB_*) to the element's HWND (or the HWND with the live SB_VERT)
//   WMVCTL  WM_VSCROLL to the parent of a ScrollBar-class child window, lParam = that scrollbar
//   EMS     EM_SCROLL (Edit/RichEdit only; line/page)
//   TXT     TextPattern: ScrollIntoView of the line just outside the visible range
//   GRIDGET GridPattern.GetItem(row,0) alone (WPF claims this scrolls)
//   GRIDSI  GridPattern.GetItem(row,0) then ScrollItem.ScrollIntoView on the cell/row
//   SI      ScrollItem.ScrollIntoView on an item one viewport past the edge
//   NAT     native item messages (TVM_SELECTITEM FIRSTVISIBLE / LB_SETTOPINDEX / LVM_ENSUREVISIBLE)
//   RV      RangeValue.SetValue on the vertical ScrollBar child
//   WHEEL   PostMessage(WM_MOUSEWHEEL) to the HWND (no cursor move; LineDown only)
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using UIA = UIAutomationClient;

namespace PagingProbe;

internal enum Cmd { LineDown, PageDown, PageUp, ToTop, ToBottom }

internal sealed class Cand
{
    public int Id;
    public UIA.IUIAutomationElement El = null!;
    public string Desc = "";
    public RECT Rect;
    public RECT View;                       // viewport used for item visibility
    public UIA.IUIAutomationElement? SpEl;
    public UIA.IUIAutomationScrollPattern? Sp;
    public string SpWhere = "";
    public UIA.IUIAutomationTextPattern? Tp;
    public int TxtTotalLines = -1;
    public UIA.IUIAutomationGridPattern? Grid;
    public UIA.IUIAutomationElement? RvEl;
    public UIA.IUIAutomationRangeValuePattern? Rv;
    public UIA.IUIAutomationSelectionPattern? Sel;
    public IntPtr Hwnd;                     // nearest HWND of the element
    public string HwndClass = "";
    public IntPtr SbHwnd;                   // HWND whose SB_VERT is live (scrollable)
    public IntPtr SbCtl;                    // vertical ScrollBar-class child window
    public string NativeKind = "";          // TV / LB / LV / EDIT
    public bool HasItems;
}

internal sealed class Snap
{
    public int SbPos = int.MinValue, SbPage, SbMin, SbMax;
    public int NatTop = -1, NatPer = -1, NatCount = -1;
    public double SpPct = -1, SpView = -1;
    public int TxtLine = -1, TxtVis = -1; public string TxtFirst = "";
    public int ItemFirst = -1, ItemLast = -1, ItemVis = -1, ItemCount = -1;
    public double RvVal = double.NaN, RvLarge = double.NaN, RvMin, RvMax;
    public ulong Pix;
    public POINT Cur; public IntPtr Fg, FocusHwnd; public string UiaFocus = "", SelSig = "";
    public bool HasKbFocus;

    public string Key() => $"{SbPos}|{NatTop}|{SpPct:F3}|{TxtLine}|{ItemFirst}|{RvVal:F3}|{Pix}";
}

internal sealed class Result
{
    public string Target = "", Cand = "", Mech = "", Cmd = "", Verdict = "", Oracle = "", Side = "", Note = "", Before = "", After = "";
    public double Pages = double.NaN;
}

internal static partial class Program
{
    // ---- UIA ids ----
    private const int P_Selection = 10001, P_Grid = 10006, P_GridItem = 10007, P_RangeValue = 10003, P_Scroll = 10004, P_Text = 10014, P_ScrollItem = 10017;
    private const int Prop_Bounding = 30001, Prop_ControlType = 30003, Prop_Name = 30005, Prop_HasKbFocus = 30008, Prop_ClassName = 30012, Prop_NativeHwnd = 30020, Prop_IsOffscreen = 30022, Prop_Orientation = 30023, Prop_AutomationId = 30011;
    private const int Prop_IsGrid = 30030, Prop_IsScroll = 30034, Prop_IsScrollItem = 30035, Prop_IsText = 30040;
    private const int Prop_VPct = 30055, Prop_VView = 30056, Prop_VScrollable = 30058, Prop_GridItemRow = 30064;
    private const int CT_Edit = 50004, CT_ListItem = 50007, CT_List = 50008, CT_ScrollBar = 50014, CT_Tree = 50023, CT_TreeItem = 50024, CT_DataGrid = 50028, CT_DataItem = 50029, CT_Document = 50030, CT_Pane = 50033, CT_Table = 50036, CT_Group = 50026, CT_Custom = 50025;

    private static UIA.IUIAutomation A = null!;
    private static IntPtr _top, _decoy, _front;
    private static string? _focusName;

    // Park keyboard focus inside the target on a named element (not the candidate), so a mechanism that
    // moves in-app focus to the scrolled control shows up as K.
    private static void RefocusInTarget()
    {
        try
        {
            var el = A.ElementFromHandle(_top).FindFirst(UIA.TreeScope.TreeScope_Descendants, A.CreatePropertyCondition(Prop_Name, _focusName));
            if (el == null) { Console.WriteLine($"   !! focus element '{_focusName}' not found"); return; }
            el.SetFocus();
        }
        catch (Exception ex) { Console.WriteLine("   !! SetFocus failed " + ex.Message); }
        EnsureDecoyFront();
    }
    private static readonly List<Result> Results = new();
    private static readonly Cmd[] AllCmds = { Cmd.LineDown, Cmd.PageDown, Cmd.PageUp, Cmd.ToTop, Cmd.ToBottom };
    private static readonly string[] AllMechs = { "SP", "WMV", "WMVCTL", "EMS", "TXT", "TXTT", "GRIDGET", "GRIDSI", "SI", "NAT", "RV", "WHEEL", "WHEELX", "WHEELS" };

    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var o = Opts.Parse(args);
        A = new UIA.CUIAutomation();

        _top = o.Hwnd != IntPtr.Zero ? o.Hwnd : FindTop(o.Target);
        if (_top == IntPtr.Zero) { Console.WriteLine($"!! no visible top-level window matches '{o.Target}'"); return 2; }
        string label = o.Label ?? o.Target ?? $"0x{_top:X}";
        Console.WriteLine($"Target: 0x{_top.ToInt64():X} '{Title(_top)}' [{ClassOf(_top)}] proc={ProcName(_top)}");

        // Make the target visible (z-order top) first: Chromium drops its web-content tree for occluded
        // windows. The decoy (small, top-left) is then put in front so the target is visible but NOT foreground.
        _front = _top; EnsureDecoyFront(); Thread.Sleep(1500); _front = IntPtr.Zero;
        var cands = Discover(o);
        Console.WriteLine($"Candidates ({cands.Count}):");
        foreach (var c in cands) Console.WriteLine($"  #{c.Id} {c.Desc}");
        if (o.List || cands.Count == 0) return 0;

        if (o.TargetFg) { _front = _top; EnsureDecoyFront(); }
        else if (!o.NoDecoy) { StartDecoy(); _front = _decoy; }
        _focusName = o.FocusName;
        if (_focusName != null) RefocusInTarget();
        Console.WriteLine($"Foreground at start: 0x{GetForegroundWindow():X} '{Title(GetForegroundWindow())}' (target fg? {GetForegroundWindow() == _top})");

        foreach (var c in cands)
        {
            if (o.Cands.Count > 0 && !o.Cands.Contains(c.Id)) continue;
            Console.WriteLine($"\n=== #{c.Id} {c.Desc}");
            var s0 = SnapOf(c);
            Console.WriteLine($"    oracles: {OracleList(c, s0)}");
            foreach (var m in AllMechs)
            {
                if (o.Mechs.Count > 0 && !o.Mechs.Contains(m)) continue;
                string? why = Unavailable(c, m);
                if (why != null) continue;
                foreach (var cmd in AllCmds)
                {
                    if (m == "EMS" && cmd is Cmd.ToTop or Cmd.ToBottom) continue;
                    if (m is "WHEEL" or "WHEELX" or "WHEELS" && cmd != Cmd.LineDown) continue;
                    var r = RunOne(label, c, m, cmd);
                    Results.Add(r);
                    Console.WriteLine($"    {m,-7} {cmd,-8} {r.Verdict,-9} {Fmt(r.Pages),7} {r.Oracle,-5} side={r.Side,-4} {r.Note}  [{r.Before} -> {r.After}]");
                }
            }
            // leave it at the top
            Prepare(c, Cmd.LineDown);
        }

        string outDir = o.Out ?? Path.Combine(AppContext.BaseDirectory, "results");
        Directory.CreateDirectory(outDir);
        string safe = string.Concat(label.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        WriteCsv(Path.Combine(outDir, safe + ".csv"));
        WriteMd(Path.Combine(outDir, safe + ".md"), label, cands);
        Console.WriteLine($"\nWrote {outDir}\\{safe}.csv/.md");
        if (_decoy != IntPtr.Zero) PostMessage(_decoy, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero);
        return 0;
    }

    // ======================================================================================
    // Discovery
    // ======================================================================================
    private static List<Cand> Discover(Opts o)
    {
        // Chromium only builds its web-content tree once it sees a UIA client listening for events
        // (UiaClientsAreListening) and/or a client touches the render-widget HWND.
        try { A.AddFocusChangedEventHandler(null, new NullFocusHandler()); } catch { }
        bool woke = ClassOf(_top) == "Chrome_WidgetWin_1";
        if (woke) try { A.ElementFromHandle(_top).FindAll(UIA.TreeScope.TreeScope_Descendants, A.CreateTrueCondition()); } catch { }
        foreach (var h in ChildWindows(_top))
            if (ClassOf(h).StartsWith("Chrome_RenderWidgetHostHWND"))
                try { A.ElementFromHandle(h).FindFirst(UIA.TreeScope.TreeScope_Descendants, A.CreateTrueCondition()); woke = true; } catch { }
        if (woke) Thread.Sleep(2000);
        var root = A.ElementFromHandle(_top);
        var found = new List<UIA.IUIAutomationElement>();
        var seen = new HashSet<string>();
        void Add(UIA.IUIAutomationElement? e) { if (e == null) return; string id = Rid(e); if (seen.Add(id)) found.Add(e); }

        if (o.Point is { } pt)
        {
            var e = A.ElementFromPoint(new UIA.tagPOINT { x = pt.X, y = pt.Y });
            // the element and its ancestors up to the window
            var w = A.RawViewWalker;
            for (int i = 0; e != null && i < 12; i++) { Add(e); if (A.CompareElements(e, root) != 0) break; e = w.GetParentElement(e); }
        }
        else
        {
            var cond = A.CreateOrCondition(
                A.CreateOrCondition(A.CreatePropertyCondition(Prop_IsScroll, true), A.CreatePropertyCondition(Prop_IsText, true)),
                A.CreateOrCondition(
                    A.CreateOrCondition(A.CreatePropertyCondition(Prop_ControlType, CT_List), A.CreatePropertyCondition(Prop_ControlType, CT_Tree)),
                    A.CreateOrCondition(A.CreateOrCondition(A.CreatePropertyCondition(Prop_ControlType, CT_DataGrid), A.CreatePropertyCondition(Prop_ControlType, CT_Table)),
                                        A.CreatePropertyCondition(Prop_ControlType, CT_Document))));
            var all = root.FindAll(UIA.TreeScope.TreeScope_Subtree, cond);
            for (int i = 0; i < all.Length; i++) Add(all.GetElement(i));
            // HWNDs with a live vertical scrollbar that UIA might present as a bare pane
            foreach (var h in ChildWindows(_top))
                if (IsWindowVisible(h) && (SbLive(h, out _) || NativeKindOf(ClassOf(h)) != "" || HasOwnVScrollBarCtl(h)))
                    try { Add(A.ElementFromHandle(h)); } catch { }
        }

        var list = new List<Cand>();
        foreach (var e in found)
        {
            RECT r; try { r = R(e.CurrentBoundingRectangle); } catch { continue; }
            if (Environment.GetEnvironmentVariable("PP_DEBUG") == "1") Console.WriteLine($"  [dbg] {CtName(e.CurrentControlType)} {e.CurrentClassName} {r.W}x{r.H} off={e.CurrentIsOffscreen}");
            if (r.W < 60 || r.H < 60) continue;
            try { if (e.CurrentIsOffscreen != 0) continue; } catch { }
            var c = Build(e);
            if (c == null) continue;
            if (o.Name != null && !c.Desc.Contains(o.Name, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(c);
        }
        // biggest first, then cap
        list = list.OrderByDescending(c => (long)c.Rect.W * c.Rect.H).Take(o.Max).ToList();
        for (int i = 0; i < list.Count; i++) list[i].Id = i + 1;
        return list;
    }

    private static Cand? Build(UIA.IUIAutomationElement e)
    {
        var c = new Cand { El = e, Rect = R(e.CurrentBoundingRectangle) };
        int ct = e.CurrentControlType;
        // ScrollPattern on self or nearest raw ancestor that is vertically scrollable
        var w = A.RawViewWalker; var cur = e;
        for (int lvl = 0; cur != null && lvl < 8; lvl++)
        {
            bool isSp = false; try { isSp = (bool)cur.GetCurrentPropertyValue(Prop_IsScroll); } catch { }
            if (isSp && GetBool(cur, Prop_VScrollable) && GetDbl(cur, Prop_VView) is > 0 and < 99.9)
            {
                c.SpEl = cur; c.Sp = (UIA.IUIAutomationScrollPattern)cur.GetCurrentPattern(P_Scroll);
                c.SpWhere = lvl == 0 ? "self" : $"anc{lvl}:{CtName(cur.CurrentControlType)}";
                break;
            }
            if (lvl > 0 && Rid(cur) == Rid(A.ElementFromHandle(_top))) break;
            try { cur = w.GetParentElement(cur); } catch { break; }
        }
        // a vertically-scrollable SP on a *descendant* (WPF ScrollViewer inside the list)
        if (c.Sp == null)
        {
            try
            {
                var d = e.FindFirst(UIA.TreeScope.TreeScope_Descendants, A.CreateAndCondition(A.CreatePropertyCondition(Prop_IsScroll, true), A.CreatePropertyCondition(Prop_VScrollable, true)));
                if (d != null && GetDbl(d, Prop_VView) is > 0 and < 99.9)
                { c.SpEl = d; c.Sp = (UIA.IUIAutomationScrollPattern)d.GetCurrentPattern(P_Scroll); c.SpWhere = $"desc:{CtName(d.CurrentControlType)}"; }
            }
            catch { }
        }
        try { if ((bool)e.GetCurrentPropertyValue(Prop_IsText)) c.Tp = (UIA.IUIAutomationTextPattern)e.GetCurrentPattern(P_Text); } catch { }
        try { if ((bool)e.GetCurrentPropertyValue(Prop_IsGrid)) c.Grid = (UIA.IUIAutomationGridPattern)e.GetCurrentPattern(P_Grid); } catch { }
        try { c.Sel = e.GetCurrentPattern(P_Selection) as UIA.IUIAutomationSelectionPattern; } catch { }
        // vertical ScrollBar element (RangeValue) inside the element (or inside its SP element)
        try
        {
            var sbCond = A.CreateAndCondition(A.CreatePropertyCondition(Prop_ControlType, CT_ScrollBar), A.CreatePropertyCondition(Prop_Orientation, 2));
            var sb = e.FindFirst(UIA.TreeScope.TreeScope_Descendants, sbCond) ?? c.SpEl?.FindFirst(UIA.TreeScope.TreeScope_Descendants, sbCond);
            if (sb != null && sb.GetCurrentPattern(P_RangeValue) is UIA.IUIAutomationRangeValuePattern rv) { c.RvEl = sb; c.Rv = rv; }
        }
        catch { }
        // HWND: own or nearest ancestor with one
        cur = e;
        for (int lvl = 0; cur != null && lvl < 60 && c.Hwnd == IntPtr.Zero; lvl++)
        {
            try { c.Hwnd = cur.CurrentNativeWindowHandle; } catch { }
            if (c.Hwnd == IntPtr.Zero) try { cur = w.GetParentElement(cur); } catch { break; }
        }
        if (c.Hwnd != IntPtr.Zero)
        {
            c.HwndClass = ClassOf(c.Hwnd);
            c.NativeKind = NativeKindOf(c.HwndClass);
            if (SbLive(c.Hwnd, out _)) c.SbHwnd = c.Hwnd;
            else
                foreach (var ch in ChildWindows(c.Hwnd))   // a content child window inside the element
                {
                    if (!IsWindowVisible(ch) || !SbLive(ch, out _)) continue;
                    GetWindowRect(ch, out var cr);
                    if (Overlap(cr, c.Rect) > 0.5) { c.SbHwnd = ch; break; }
                }
            c.SbCtl = FindScrollBarCtl(c.Hwnd, c.Rect);
        }
        c.View = c.SpEl != null ? Intersect(c.Rect, R(c.SpEl.CurrentBoundingRectangle)) : c.Rect;
        if (c.View.W <= 0 || c.View.H <= 0) c.View = c.Rect;
        try { c.HasItems = Items(c).Count > 0; } catch { }
        if (c.Tp != null) try { c.TxtTotalLines = CountLines(c.Tp.DocumentRange.GetText(4_000_000)) + 1; } catch { }

        bool scrollish = c.Sp != null || c.SbHwnd != IntPtr.Zero || c.SbCtl != IntPtr.Zero || c.Rv != null
                         || (c.Tp != null && c.TxtTotalLines > 40)
                         || (ct is CT_List or CT_Tree or CT_DataGrid or CT_Table && c.HasItems && c.Rect.H > 150);
        if (!scrollish) return null;
        string name = ""; try { name = e.CurrentName ?? ""; } catch { }
        if (name.Length > 28) name = name[..28] + "…";
        string aid = ""; try { aid = e.CurrentAutomationId ?? ""; } catch { }
        if (aid.Length > 20) aid = aid[..20];
        c.Desc = $"{CtName(ct)} '{name}' aid={aid} cls={Trunc(e.CurrentClassName, 24)} {c.Rect.W}x{c.Rect.H}@{c.Rect.L},{c.Rect.T}"
                 + $" | SP={(c.Sp != null ? c.SpWhere : "-")} TP={(c.Tp != null ? "y" : "-")} Grid={(c.Grid != null ? "y" : "-")} RV={(c.Rv != null ? "y" : "-")} Items={(c.HasItems ? "y" : "-")}"
                 + $" hwnd=0x{c.Hwnd:X}[{Trunc(c.HwndClass, 32)}] SB={(c.SbHwnd != IntPtr.Zero ? $"0x{c.SbHwnd:X}" : "-")} SBCTL={(c.SbCtl != IntPtr.Zero ? $"0x{c.SbCtl:X}" : "-")} NAT={(c.NativeKind == "" ? "-" : c.NativeKind)}";
        return c;
    }

    private static string NativeKindOf(string cls)
    {
        if (cls.Contains("SysTreeView32", StringComparison.OrdinalIgnoreCase)) return "TV";
        if (cls.Contains("SysListView32", StringComparison.OrdinalIgnoreCase)) return "LV";
        if (cls.Equals("ListBox", StringComparison.OrdinalIgnoreCase) || cls.Contains(".LISTBOX.", StringComparison.OrdinalIgnoreCase) || cls.Equals("ComboLBox", StringComparison.OrdinalIgnoreCase)) return "LB";
        if (cls.Equals("Edit", StringComparison.OrdinalIgnoreCase) || cls.Contains(".EDIT.", StringComparison.OrdinalIgnoreCase) || cls.Contains("RichEdit", StringComparison.OrdinalIgnoreCase)) return "EDIT";
        return "";
    }

    // A content window that owns a vertical ScrollBar-class child (VCL/Delphi, DataGridView, DirectUI).
    private static bool HasOwnVScrollBarCtl(IntPtr h)
    {
        if (ClassOf(h).Contains("ScrollBar", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var ch in ChildWindows(h))
            if (GetParent(ch) == h && IsWindowVisible(ch) && ClassOf(ch).Contains("ScrollBar", StringComparison.OrdinalIgnoreCase) && (GetWindowLong(ch, -16) & 1) != 0)
            {
                GetWindowRect(ch, out var r);
                if (r.H > 100) return true;
            }
        return false;
    }

    private static IntPtr FindScrollBarCtl(IntPtr hwnd, RECT target)
    {
        var pool = new List<IntPtr>(ChildWindows(hwnd));
        var parent = GetParent(hwnd);
        if (parent != IntPtr.Zero) pool.AddRange(ChildWindows(parent));
        foreach (var h in pool)
        {
            if (!IsWindowVisible(h)) continue;
            if (!ClassOf(h).Contains("ScrollBar", StringComparison.OrdinalIgnoreCase)) continue;
            if ((GetWindowLong(h, -16) & 1) == 0) continue; // SBS_VERT
            GetWindowRect(h, out var r);
            bool vOverlap = r.T < target.B && r.B > target.T;
            bool nearRight = Math.Abs(r.R - target.R) < 40 || (r.L >= target.L && r.R <= target.R + 40);
            if (vOverlap && nearRight && r.H > target.H / 3) return h;
        }
        return IntPtr.Zero;
    }

    private static string? Unavailable(Cand c, string m) => m switch
    {
        "SP" => c.Sp == null ? "no ScrollPattern" : null,
        "WMV" => c.Hwnd == IntPtr.Zero ? "no hwnd" : null,
        "WMVCTL" => c.SbCtl == IntPtr.Zero ? "no ScrollBar ctl" : null,
        "EMS" => c.NativeKind == "EDIT" ? null : "not Edit",
        "TXT" or "TXTT" => c.Tp == null ? "no TextPattern" : null,
        "GRIDGET" or "GRIDSI" => c.Grid == null ? "no Grid" : null,
        "SI" => c.HasItems ? null : "no ScrollItem items",
        "NAT" => c.NativeKind is "TV" or "LB" or "LV" ? null : "not TV/LB/LV",
        "RV" => c.Rv == null ? "no ScrollBar RangeValue" : null,
        "WHEEL" or "WHEELX" or "WHEELS" => c.Hwnd == IntPtr.Zero ? "no hwnd" : null,
        _ => "?"
    };

    // ======================================================================================
    // Run one (candidate, mechanism, command)
    // ======================================================================================
    private static Result RunOne(string label, Cand c, string m, Cmd cmd)
    {
        var res = new Result { Target = label, Cand = $"#{c.Id}", Mech = m, Cmd = cmd.ToString() };
        string prep = Prepare(c, cmd, m);
        if (_focusName != null) RefocusInTarget(); else EnsureDecoyFront();
        var before = SnapOf(c, full: true);
        string note;
        try { note = Actuate(c, m, cmd, before); }
        catch (Exception ex) { note = "EXC " + ex.GetType().Name + ": " + Trunc(ex.Message, 60); }
        var after = Settle(c, before);
        res.Note = (prep != "" ? prep + " " : "") + note;
        Evaluate(c, cmd, before, after, res);
        return res;
    }

    private static Snap Settle(Cand c, Snap before)
    {
        Thread.Sleep(180);
        var s = SnapOf(c, full: true);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 1500)
        {
            Thread.Sleep(150);
            var s2 = SnapOf(c, full: true);
            if (s2.Key() == s.Key()) return s2;
            s = s2;
        }
        return s;
    }

    // Bring the candidate to the precondition position for `cmd` using a trusted driver.
    private static string Prepare(Cand c, Cmd cmd, string mechUnderTest = "")
    {
        bool wantBottom = cmd == Cmd.ToTop;
        bool wantMid = cmd == Cmd.PageUp;
        string[] drivers = { "SP", "WMV", "WMVCTL", "NAT", "TXT", "SI" };
        foreach (var d in drivers)
        {
            if (Unavailable(c, d) != null) continue;
            if (d == "WMV" && c.SbHwnd == IntPtr.Zero) continue;
            try
            {
                var s0 = SnapOf(c);
                Actuate(c, d, wantBottom ? Cmd.ToBottom : Cmd.ToTop, s0);
                var s1 = Settle(c, s0);
                bool ok = wantBottom ? AtBottom(c, s1) == true : AtTop(c, s1) == true;
                if (!ok) continue;
                if (wantMid)
                {
                    for (int i = 0; i < 3; i++) { var sx = SnapOf(c); Actuate(c, d, Cmd.PageDown, sx); Settle(c, sx); }
                    var sm = SnapOf(c);
                    if (AtTop(c, sm) == true) return $"(prep-mid FAILED via {d})";
                }
                return "";
            }
            catch { }
        }
        return wantBottom ? "(prep-bottom?)" : wantMid ? "(prep-mid?)" : "(prep-top?)";
    }

    private static string Actuate(Cand c, string m, Cmd cmd, Snap s)
    {
        switch (m)
        {
            case "SP":
                {
                    var sp = c.Sp!;
                    switch (cmd)
                    {
                        case Cmd.LineDown: sp.Scroll(UIA.ScrollAmount.ScrollAmount_NoAmount, UIA.ScrollAmount.ScrollAmount_SmallIncrement); break;
                        case Cmd.PageDown: sp.Scroll(UIA.ScrollAmount.ScrollAmount_NoAmount, UIA.ScrollAmount.ScrollAmount_LargeIncrement); break;
                        case Cmd.PageUp: sp.Scroll(UIA.ScrollAmount.ScrollAmount_NoAmount, UIA.ScrollAmount.ScrollAmount_LargeDecrement); break;
                        case Cmd.ToTop: sp.SetScrollPercent(-1, 0); break;
                        case Cmd.ToBottom: sp.SetScrollPercent(-1, 100); break;
                    }
                    return "";
                }
            case "WMV":
                {
                    var h = c.SbHwnd != IntPtr.Zero ? c.SbHwnd : c.Hwnd;
                    int sb = SbCode(cmd);
                    var r1 = SendMessageTimeout(h, WM_VSCROLL, (IntPtr)sb, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out var ret);
                    SendMessageTimeout(h, WM_VSCROLL, (IntPtr)SB_ENDSCROLL, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out _);
                    return r1 == IntPtr.Zero ? "send-timeout" : (h != c.Hwnd ? $"->0x{h:X}" : "");
                }
            case "WMVCTL":
                {
                    var parent = GetParent(c.SbCtl);
                    SendMessageTimeout(parent, WM_VSCROLL, (IntPtr)SbCode(cmd), c.SbCtl, SMTO_ABORTIFHUNG, 1000, out _);
                    SendMessageTimeout(parent, WM_VSCROLL, (IntPtr)SB_ENDSCROLL, c.SbCtl, SMTO_ABORTIFHUNG, 1000, out _);
                    return $"->parent 0x{parent:X}[{Trunc(ClassOf(parent), 20)}]";
                }
            case "EMS":
                {
                    SendMessageTimeout(c.Hwnd, EM_SCROLL, (IntPtr)SbCode(cmd), IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out var ret);
                    return $"ret=0x{ret:X}";
                }
            case "TXT": return TextScroll(c, cmd, false);
            case "TXTT": return TextScroll(c, cmd, true);
            case "GRIDGET":
            case "GRIDSI": return GridScroll(c, cmd, m == "GRIDSI");
            case "SI": return ItemScroll(c, cmd);
            case "NAT": return NativeScroll(c, cmd);
            case "RV":
                {
                    var rv = c.Rv!;
                    double v = rv.CurrentValue, mn = rv.CurrentMinimum, mx = rv.CurrentMaximum, sm = rv.CurrentSmallChange, lg = rv.CurrentLargeChange;
                    if (sm <= 0) sm = 1; if (lg <= 0) lg = (mx - mn) / 10;
                    double t = cmd switch { Cmd.LineDown => v + sm, Cmd.PageDown => v + lg, Cmd.PageUp => v - lg, Cmd.ToTop => mn, _ => mx };
                    rv.SetValue(Math.Clamp(t, mn, mx));
                    return $"v={v:F1}->{t:F1}";
                }
            case "WHEEL":
            case "WHEELX":
            case "WHEELS":
                {
                    int cx = (c.View.L + c.View.R) / 2, cy = (c.View.T + c.View.B) / 2;
                    if (m == "WHEELX") { cx = 5; cy = 5; } // control: lParam deliberately OFF the target
                    var target = c.SbHwnd != IntPtr.Zero ? c.SbHwnd : c.Hwnd;
                    if (m == "WHEELS") SendMessageTimeout(target, 0x020A, (IntPtr)((-120) << 16), (IntPtr)((cy << 16) | (cx & 0xFFFF)), SMTO_ABORTIFHUNG, 1000, out _);
                    else PostMessage(target, 0x020A, (IntPtr)((-120) << 16), (IntPtr)((cy << 16) | (cx & 0xFFFF)));
                    return "";
                }
        }
        return "?";
    }

    private static int SbCode(Cmd cmd) => cmd switch
    {
        Cmd.LineDown => 1 /*SB_LINEDOWN*/, Cmd.PageDown => 3 /*SB_PAGEDOWN*/, Cmd.PageUp => 2 /*SB_PAGEUP*/, Cmd.ToTop => 6 /*SB_TOP*/, _ => 7 /*SB_BOTTOM*/
    };

    // ---- TextPattern ----------------------------------------------------------------------
    private static string TextScroll(Cand c, Cmd cmd, bool topOnly)
    {
        var tp = c.Tp!;
        const UIA.TextUnit Line = UIA.TextUnit.TextUnit_Line;
        const UIA.TextPatternRangeEndpoint S = UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, E = UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_End;
        if (cmd is Cmd.ToTop or Cmd.ToBottom)
        {
            var d = tp.DocumentRange;
            if (cmd == Cmd.ToTop) { d.MoveEndpointByRange(E, d, S); d.ExpandToEnclosingUnit(Line); d.ScrollIntoView(1); return "doc-start line, alignTop"; }
            d.MoveEndpointByRange(S, d, E); d.MoveEndpointByUnit(S, UIA.TextUnit.TextUnit_Character, -1); d.ExpandToEnclosingUnit(Line); d.ScrollIntoView(topOnly ? 1 : 0);
            return topOnly ? "doc-end line, alignTop" : "doc-end line, alignBottom";
        }
        var vis = tp.GetVisibleRanges();
        if (vis == null || vis.Length == 0) return "no visible ranges";
        var first = vis.GetElement(0).Clone(); var last = vis.GetElement(vis.Length - 1).Clone();
        switch (cmd)
        {
            case Cmd.LineDown:
                {
                    var r = first; r.MoveEndpointByRange(E, r, S); r.ExpandToEnclosingUnit(Line);
                    int moved = r.Move(Line, 1); r.ExpandToEnclosingUnit(Line); r.ScrollIntoView(1);
                    return $"2nd line->top (move={moved}) '{Trunc(r.GetText(30), 20)}'";
                }
            case Cmd.PageDown:
                {
                    var r = last; r.MoveEndpointByRange(S, r, E); r.MoveEndpointByUnit(S, UIA.TextUnit.TextUnit_Character, -1); r.ExpandToEnclosingUnit(Line);
                    int moved = r.Move(Line, 1); r.ExpandToEnclosingUnit(Line); r.ScrollIntoView(1);
                    return $"line after last->top (move={moved}) '{Trunc(r.GetText(30), 20)}'";
                }
            case Cmd.PageUp:
                {
                    var r = first; r.MoveEndpointByRange(E, r, S); r.ExpandToEnclosingUnit(Line);
                    if (topOnly)
                    {
                        var sbv = new StringBuilder(); for (int i = 0; i < vis.Length; i++) sbv.Append(vis.GetElement(i).GetText(200_000));
                        int visLines = CountLines(sbv.ToString().TrimEnd(new[] { (char)13, (char)10 })) + 1; visLines = Math.Max(visLines, vis.Length);
                        int mv = r.Move(Line, -(visLines - 1)); r.ExpandToEnclosingUnit(Line); r.ScrollIntoView(1);
                        return $"line first-{visLines - 1}->top (move={mv}) '{Trunc(r.GetText(30), 20)}'";
                    }
                    int moved = r.Move(Line, -1); r.ExpandToEnclosingUnit(Line); r.ScrollIntoView(0);
                    return $"line before first->bottom (move={moved}) '{Trunc(r.GetText(30), 20)}'";
                }
        }
        return "";
    }

    // ---- Grid -----------------------------------------------------------------------------
    private static string GridScroll(Cand c, Cmd cmd, bool thenScrollItem)
    {
        var g = c.Grid!;
        int rows = g.CurrentRowCount;
        var (firstRow, lastRow, vis) = VisibleRows(c);
        int target = cmd switch
        {
            Cmd.ToTop => 0,
            Cmd.ToBottom => rows - 1,
            Cmd.LineDown => lastRow + 1,
            Cmd.PageDown => lastRow + Math.Max(1, vis),
            _ => firstRow - Math.Max(1, vis),
        };
        if (cmd is Cmd.LineDown or Cmd.PageDown or Cmd.PageUp && lastRow < 0) return "no visible rows";
        target = Math.Clamp(target, 0, rows - 1);
        var cell = g.GetItem(target, 0);
        string note = $"rows={rows} vis={firstRow}..{lastRow} target={target}";
        if (!thenScrollItem) return note;
        if (cell == null) return note + " cell=null";
        if (cell.GetCurrentPattern(P_ScrollItem) is UIA.IUIAutomationScrollItemPattern si) { si.ScrollIntoView(); return note + " SI(cell)"; }
        var parent = A.RawViewWalker.GetParentElement(cell);
        if (parent?.GetCurrentPattern(P_ScrollItem) is UIA.IUIAutomationScrollItemPattern psi) { psi.ScrollIntoView(); return note + " SI(row)"; }
        return note + " no ScrollItem on cell/row";
    }

    // Visible grid rows = rows of realized GridItem cells fully inside the viewport.
    private static (int first, int last, int vis) VisibleRows(Cand c)
    {
        var cr = A.CreateCacheRequest();
        cr.AddProperty(Prop_Bounding); cr.AddProperty(Prop_GridItemRow);
        var arr = c.El.FindAllBuildCache(UIA.TreeScope.TreeScope_Descendants, A.CreatePropertyCondition(30029 /*IsGridItemPatternAvailable*/, true), cr);
        int first = int.MaxValue, last = -1; var rows = new HashSet<int>();
        for (int i = 0; arr != null && i < arr.Length; i++)
        {
            var e = arr.GetElement(i);
            if (!Contained(R(e.CachedBoundingRectangle), c.View)) continue;
            if (e.GetCachedPropertyValue(Prop_GridItemRow) is not int row) continue;
            rows.Add(row); first = Math.Min(first, row); last = Math.Max(last, row);
        }
        return (first == int.MaxValue ? -1 : first, last, rows.Count);
    }

    // ---- ScrollItem on boundary items -------------------------------------------------------
    private static string ItemScroll(Cand c, Cmd cmd)
    {
        var items = Items(c);
        if (items.Count == 0) return "no items";
        var (f, l, v) = VisibleIdx(c, items);
        if (f < 0 && cmd is not (Cmd.ToTop or Cmd.ToBottom)) return "no visible items";
        int want = cmd switch
        {
            Cmd.ToTop => 0,
            Cmd.ToBottom => items.Count - 1,
            Cmd.LineDown => l + 1,
            Cmd.PageDown => l + Math.Max(1, v),
            _ => f - Math.Max(1, v),
        };
        int t = Math.Clamp(want, 0, items.Count - 1);
        var el = items[t].el;
        ((UIA.IUIAutomationScrollItemPattern)el.GetCurrentPattern(P_ScrollItem)).ScrollIntoView();
        return $"items={items.Count} vis={f}..{l} target={t}{(t != want ? "(clamped: not realized?)" : "")}";
    }

    private static List<(UIA.IUIAutomationElement el, RECT r)> Items(Cand c)
    {
        var cr = A.CreateCacheRequest();
        cr.AddProperty(Prop_Bounding); cr.AddProperty(Prop_IsOffscreen);
        var cond = A.CreateAndCondition(A.CreatePropertyCondition(Prop_IsScrollItem, true),
            A.CreateOrCondition(A.CreateOrCondition(A.CreatePropertyCondition(Prop_ControlType, CT_ListItem), A.CreatePropertyCondition(Prop_ControlType, CT_TreeItem)),
                                A.CreatePropertyCondition(Prop_ControlType, CT_DataItem)));
        var arr = c.El.FindAllBuildCache(UIA.TreeScope.TreeScope_Descendants, cond, cr);
        var list = new List<(UIA.IUIAutomationElement, RECT)>();
        if (arr == null) return list;
        for (int i = 0; i < arr.Length; i++) { var e = arr.GetElement(i); list.Add((e, R(e.CachedBoundingRectangle))); }
        return list;
    }

    private static (int first, int last, int vis) VisibleIdx(Cand c, List<(UIA.IUIAutomationElement el, RECT r)> items)
    {
        int f = -1, l = -1, n = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var r = items[i].r;
            if (r.H <= 0 || !Contained(r, c.View)) continue;
            if (f < 0) f = i; l = i; n++;
        }
        return (f, l, n);
    }

    // ---- Native item messages ---------------------------------------------------------------
    private static string NativeScroll(Cand c, Cmd cmd)
    {
        var h = c.Hwnd;
        switch (c.NativeKind)
        {
            case "TV":
                {
                    const int TVM_GETNEXTITEM = 0x110A, TVM_SELECTITEM = 0x110B, TVM_GETVISIBLECOUNT = 0x1110;
                    const int TVGN_ROOT = 0, TVGN_FIRSTVISIBLE = 5, TVGN_NEXTVISIBLE = 6, TVGN_PREVIOUSVISIBLE = 7, TVGN_LASTVISIBLE = 0xA;
                    int count = (int)Send(h, TVM_GETVISIBLECOUNT, 0, 0);
                    IntPtr item = Send(h, TVM_GETNEXTITEM, TVGN_FIRSTVISIBLE, 0);
                    IntPtr target = item;
                    switch (cmd)
                    {
                        case Cmd.ToTop: target = Send(h, TVM_GETNEXTITEM, TVGN_ROOT, 0); break;
                        case Cmd.ToBottom: target = Send(h, TVM_GETNEXTITEM, TVGN_LASTVISIBLE, 0); break;
                        default:
                            int steps = cmd == Cmd.LineDown ? 1 : count; int code = cmd == Cmd.PageUp ? TVGN_PREVIOUSVISIBLE : TVGN_NEXTVISIBLE;
                            for (int i = 0; i < steps; i++) { var nx = Send(h, TVM_GETNEXTITEM, code, target); if (nx == IntPtr.Zero) break; target = nx; }
                            break;
                    }
                    var ok = Send(h, TVM_SELECTITEM, TVGN_FIRSTVISIBLE, target);
                    return $"TVM_SELECTITEM(FIRSTVISIBLE) visCount={count} ok={ok}";
                }
            case "LB":
                {
                    const int LB_GETTOPINDEX = 0x018E, LB_SETTOPINDEX = 0x0197, LB_GETCOUNT = 0x018B;
                    int top = (int)Send(h, LB_GETTOPINDEX, 0, 0), n = (int)Send(h, LB_GETCOUNT, 0, 0), per = LbPerPage(h);
                    int t = cmd switch { Cmd.LineDown => top + 1, Cmd.PageDown => top + per, Cmd.PageUp => top - per, Cmd.ToTop => 0, _ => n - 1 };
                    t = Math.Clamp(t, 0, Math.Max(0, n - 1));
                    var ret = Send(h, LB_SETTOPINDEX, t, 0);
                    return $"LB_SETTOPINDEX({t}) per={per} ret={ret}";
                }
            case "LV":
                {
                    const int LVM_GETTOPINDEX = 0x1027, LVM_GETCOUNTPERPAGE = 0x1028, LVM_GETITEMCOUNT = 0x1004, LVM_ENSUREVISIBLE = 0x1013;
                    int top = (int)Send(h, LVM_GETTOPINDEX, 0, 0), per = (int)Send(h, LVM_GETCOUNTPERPAGE, 0, 0), n = (int)Send(h, LVM_GETITEMCOUNT, 0, 0);
                    int t = cmd switch { Cmd.LineDown => top + per, Cmd.PageDown => top + 2 * per - 1, Cmd.PageUp => top - per, Cmd.ToTop => 0, _ => n - 1 };
                    t = Math.Clamp(t, 0, Math.Max(0, n - 1));
                    var ret = Send(h, LVM_ENSUREVISIBLE, t, 0);
                    return $"LVM_ENSUREVISIBLE({t}) top={top} per={per} ret={ret}";
                }
        }
        return "?";
    }

    private static int LbPerPage(IntPtr h)
    {
        if (SbLive(h, out var si)) return (int)si.nPage;
        int ih = (int)Send(h, 0x01A1 /*LB_GETITEMHEIGHT*/, 0, 0);
        GetClientRect(h, out var cr);
        return ih > 0 ? Math.Max(1, cr.H / ih) : 1;
    }

    // ======================================================================================
    // Oracles
    // ======================================================================================
    private static Snap SnapOf(Cand c, bool full = false)
    {
        var s = new Snap();
        var sbh = c.SbHwnd != IntPtr.Zero ? c.SbHwnd : IntPtr.Zero;
        if (sbh != IntPtr.Zero && GetSi(sbh, 1 /*SB_VERT*/, out var si)) { s.SbPos = si.nPos; s.SbPage = (int)si.nPage; s.SbMin = si.nMin; s.SbMax = si.nMax; }
        else if (c.SbCtl != IntPtr.Zero && GetSi(c.SbCtl, 2 /*SB_CTL*/, out var si2)) { s.SbPos = si2.nPos; s.SbPage = (int)si2.nPage; s.SbMin = si2.nMin; s.SbMax = si2.nMax; }
        if (c.NativeKind == "LB") { s.NatTop = (int)Send(c.Hwnd, 0x018E, 0, 0); s.NatPer = LbPerPage(c.Hwnd); s.NatCount = (int)Send(c.Hwnd, 0x018B, 0, 0); }
        else if (c.NativeKind == "LV") { s.NatTop = (int)Send(c.Hwnd, 0x1027, 0, 0); s.NatPer = (int)Send(c.Hwnd, 0x1028, 0, 0); s.NatCount = (int)Send(c.Hwnd, 0x1004, 0, 0); }
        if (c.SpEl != null) { s.SpPct = GetDbl(c.SpEl, Prop_VPct); s.SpView = GetDbl(c.SpEl, Prop_VView); }
        if (c.Tp != null)
        {
            try
            {
                var vis = c.Tp.GetVisibleRanges();
                if (vis != null && vis.Length > 0)
                {
                    var first = vis.GetElement(0);
                    var d = c.Tp.DocumentRange;
                    d.MoveEndpointByRange(UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, first, UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
                    s.TxtLine = CountLines(d.GetText(4_000_000));
                    int vl = 0; var sb = new StringBuilder();
                    for (int i = 0; i < vis.Length; i++) sb.Append(vis.GetElement(i).GetText(200_000));
                    string vt = sb.ToString(); vl = CountLines(vt.TrimEnd('\r', '\n')) + 1;
                    s.TxtVis = Math.Max(vl, vis.Length); s.TxtFirst = Trunc(first.GetText(40).Split('\r', '\n')[0], 24);
                }
            }
            catch { }
        }
        if (c.HasItems)
        {
            try
            {
                var items = Items(c); var (f, l, v) = VisibleIdx(c, items);
                s.ItemFirst = f; s.ItemLast = l; s.ItemVis = v; s.ItemCount = items.Count;
            }
            catch { }
        }
        if (c.Rv != null) try { s.RvVal = c.Rv.CurrentValue; s.RvLarge = c.Rv.CurrentLargeChange; s.RvMin = c.Rv.CurrentMinimum; s.RvMax = c.Rv.CurrentMaximum; } catch { }
        s.Pix = PixHash(_top, c.View);
        if (full)
        {
            GetCursorPos(out s.Cur);
            s.Fg = GetForegroundWindow();
            uint tid = GetWindowThreadProcessId(c.Hwnd != IntPtr.Zero ? c.Hwnd : _top, out _);
            var gi = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(tid, ref gi)) s.FocusHwnd = gi.hwndFocus;
            try { var fe = A.GetFocusedElement(); s.UiaFocus = Rid(fe) + "~" + Trunc(fe.CurrentName, 16); } catch { }
            try { s.HasKbFocus = c.El.CurrentHasKeyboardFocus != 0; } catch { }
            s.SelSig = SelSig(c);
        }
        return s;
    }

    private static string SelSig(Cand c)
    {
        var sb = new StringBuilder();
        try
        {
            if (c.Sel != null)
            {
                var a = c.Sel.GetCurrentSelection();
                for (int i = 0; a != null && i < a.Length && i < 5; i++) sb.Append(a.GetElement(i).CurrentName).Append(';');
            }
        }
        catch { }
        try
        {
            if (c.Tp != null)
            {
                var sel = c.Tp.GetSelection();
                if (sel != null && sel.Length > 0)
                {
                    var r = sel.GetElement(0); var d = c.Tp.DocumentRange;
                    d.MoveEndpointByRange(UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, r, UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
                    sb.Append($"caret@{d.GetText(4_000_000).Length}+{r.GetText(1000).Length}");
                }
            }
        }
        catch { }
        return sb.ToString();
    }

    private static bool? AtTop(Cand c, Snap s)
    {
        if (s.SbPos != int.MinValue && s.SbMax > s.SbMin) return s.SbPos <= s.SbMin;
        if (s.NatTop >= 0) return s.NatTop == 0;
        if (s.SpPct >= 0) return s.SpPct <= 0.01;
        if (s.TxtLine >= 0) return s.TxtLine == 0;
        if (s.ItemFirst >= 0) return s.ItemFirst == 0;
        if (!double.IsNaN(s.RvVal)) return s.RvVal <= s.RvMin;
        return null;
    }

    private static bool? AtBottom(Cand c, Snap s)
    {
        if (s.SbPos != int.MinValue && s.SbMax > s.SbMin) return s.SbPos >= s.SbMax - Math.Max(1, s.SbPage) + 1 - 1;
        if (s.NatTop >= 0) return s.NatTop + s.NatPer >= s.NatCount;
        if (s.SpPct >= 0) return s.SpPct >= 99.9;
        if (s.TxtLine >= 0 && c.TxtTotalLines > 0) return s.TxtLine + s.TxtVis >= c.TxtTotalLines - 1;
        if (s.ItemLast >= 0) return s.ItemLast == s.ItemCount - 1;
        if (!double.IsNaN(s.RvVal)) return s.RvVal >= s.RvMax;
        return null;
    }

    // Primary oracle -> displacement in viewports ("pages").
    private static (string name, double pages, string b, string a)? Primary(Cand c, Snap b, Snap a)
    {
        if (b.SbPos != int.MinValue && a.SbPos != int.MinValue && b.SbPage > 0 && b.SbMax > b.SbMin)
            return ("SB", (a.SbPos - b.SbPos) / (double)b.SbPage, $"pos{b.SbPos}/pg{b.SbPage}/max{b.SbMax}", $"pos{a.SbPos}");
        if (b.NatTop >= 0 && a.NatTop >= 0 && b.NatPer > 0)
            return ("NAT", (a.NatTop - b.NatTop) / (double)b.NatPer, $"top{b.NatTop}/per{b.NatPer}", $"top{a.NatTop}");
        if (b.SpPct >= 0 && a.SpPct >= 0 && b.SpView > 0 && b.SpView < 100)
            return ("SP%", (a.SpPct - b.SpPct) / 100.0 * (100 - b.SpView) / b.SpView, $"{b.SpPct:F2}%/v{b.SpView:F1}", $"{a.SpPct:F2}%");
        if (b.TxtLine >= 0 && a.TxtLine >= 0 && b.TxtVis > 0)
            return ("TXT", (a.TxtLine - b.TxtLine) / (double)b.TxtVis, $"ln{b.TxtLine}/vis{b.TxtVis}", $"ln{a.TxtLine}");
        if (b.ItemFirst >= 0 && a.ItemFirst >= 0 && b.ItemVis > 0)
            return ("ITEM", (a.ItemFirst - b.ItemFirst) / (double)b.ItemVis, $"it{b.ItemFirst}/vis{b.ItemVis}", $"it{a.ItemFirst}");
        if (!double.IsNaN(b.RvVal) && !double.IsNaN(a.RvVal) && b.RvLarge > 0)
            return ("RV", (a.RvVal - b.RvVal) / b.RvLarge, $"v{b.RvVal:F1}/lg{b.RvLarge:F1}", $"v{a.RvVal:F1}");
        return null;
    }

    private static void Evaluate(Cand c, Cmd cmd, Snap b, Snap a, Result res)
    {
        var p = Primary(c, b, a);
        bool pixMoved = b.Pix != a.Pix;
        bool moved = (p != null && Math.Abs(p.Value.pages) > 1e-6) || pixMoved;
        var side = new StringBuilder();
        if (b.Cur.X != a.Cur.X || b.Cur.Y != a.Cur.Y) side.Append('C');
        if (b.Fg != a.Fg) side.Append('F');
        if (b.FocusHwnd != a.FocusHwnd || b.UiaFocus != a.UiaFocus || b.HasKbFocus != a.HasKbFocus) side.Append('K');
        if (b.SelSig != a.SelSig) side.Append('S');
        res.Side = side.Length == 0 ? "-" : side.ToString();
        if (p != null) { res.Oracle = p.Value.name; res.Pages = p.Value.pages; res.Before = p.Value.b; res.After = p.Value.a; }
        else { res.Oracle = "PIX"; res.Before = $"{b.Pix:X4}"[..4]; res.After = $"{a.Pix:X4}"[..4]; }
        if (res.Side.Contains('K')) res.Note += $" focus:{(b.FocusHwnd != a.FocusHwnd ? $"hwnd 0x{b.FocusHwnd:X}->0x{a.FocusHwnd:X} " : "")}{(b.HasKbFocus != a.HasKbFocus ? $"elKb {b.HasKbFocus}->{a.HasKbFocus} " : "")}{(b.UiaFocus != a.UiaFocus ? $"uia '{b.UiaFocus.Split('~').Last()}'->'{a.UiaFocus.Split('~').Last()}'" : "")}";
        if (res.Side.Contains('S')) res.Note += $" sel:'{Trunc(b.SelSig, 30)}'->'{Trunc(a.SelSig, 30)}'";
        if (res.Side.Contains('F')) res.Note += $" fg->0x{a.Fg:X}'{Trunc(Title(a.Fg), 20)}'";

        double pg = res.Pages;
        bool numeric = p != null;
        if (!moved) { res.Verdict = "no-move"; return; }
        if (numeric && Math.Abs(pg) < 1e-6 && pixMoved) { res.Verdict = "pix-only"; return; }
        if (!numeric) { res.Verdict = "moved?"; return; }
        switch (cmd)
        {
            case Cmd.LineDown: res.Verdict = pg > 0 && pg <= 0.34 ? "line" : pg > 0 ? "big" : "wrong-dir"; break;
            case Cmd.PageDown: res.Verdict = pg >= 0.7 && pg <= 1.1 ? "page" : pg > 1.1 ? "over" : pg > 0 ? "part" : "wrong-dir"; break;
            case Cmd.PageUp: res.Verdict = pg <= -0.7 && pg >= -1.1 ? "page" : pg < -1.1 ? "over" : pg < 0 ? "part" : "wrong-dir"; break;
            case Cmd.ToTop: res.Verdict = AtTop(c, a) == true ? "exact" : pg < 0 ? "part" : "wrong-dir"; break;
            case Cmd.ToBottom: res.Verdict = AtBottom(c, a) == true ? "exact" : pg > 0 ? "part" : "wrong-dir"; break;
        }
    }

    private static string OracleList(Cand c, Snap s)
    {
        var l = new List<string>();
        if (s.SbPos != int.MinValue) l.Add($"SB(pos {s.SbPos} page {s.SbPage} range {s.SbMin}..{s.SbMax})");
        if (s.NatTop >= 0) l.Add($"NAT(top {s.NatTop} per {s.NatPer} n {s.NatCount})");
        if (s.SpPct >= 0) l.Add($"SP%({s.SpPct:F1} view {s.SpView:F1})");
        if (s.TxtLine >= 0) l.Add($"TXT(line {s.TxtLine} vis {s.TxtVis} total {c.TxtTotalLines} '{s.TxtFirst}')");
        if (s.ItemFirst >= 0 || s.ItemCount > 0) l.Add($"ITEM(first {s.ItemFirst} vis {s.ItemVis} realized {s.ItemCount})");
        if (!double.IsNaN(s.RvVal)) l.Add($"RV({s.RvVal:F1} {s.RvMin}..{s.RvMax} lg {s.RvLarge})");
        l.Add("PIX");
        return string.Join(", ", l);
    }

    private static readonly char[] NL = { (char)13, (char)10 };
    private static int CountLines(string t)
    {
        int n = 0;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] == '\n') n++;
            else if (t[i] == '\r' && (i + 1 >= t.Length || t[i + 1] != '\n')) n++;
        }
        return n;
    }

    // ---- PrintWindow pixel hash ---------------------------------------------------------------
    private static ulong PixHash(IntPtr top, RECT r)
    {
        try
        {
            GetWindowRect(top, out var wr);
            if (wr.W <= 0 || wr.H <= 0) return 0;
            using var bmp = new Bitmap(wr.W, wr.H, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) { var hdc = g.GetHdc(); PrintWindow(top, hdc, 2); g.ReleaseHdc(hdc); }
            var rr = Intersect(r, wr);
            // exclude a 24px strip on the right (scrollbar thumb) so only content counts
            var crop = new Rectangle(rr.L - wr.L, rr.T - wr.T, Math.Max(1, rr.W - 24), Math.Max(1, rr.H));
            crop.Intersect(new Rectangle(0, 0, wr.W, wr.H));
            if (crop.Width <= 0 || crop.Height <= 0) return 0;
            var bd = bmp.LockBits(crop, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            ulong hsh = 1469598103934665603UL;
            unsafe
            {
                for (int y = 0; y < crop.Height; y++)
                {
                    uint* row = (uint*)((byte*)bd.Scan0 + y * bd.Stride);
                    for (int x = 0; x < crop.Width; x++) { hsh ^= row[x]; hsh *= 1099511628211UL; }
                }
            }
            bmp.UnlockBits(bd);
            return hsh;
        }
        catch { return 0; }
    }

    // ======================================================================================
    // Decoy foreground window
    // ======================================================================================
    private static void StartDecoy()
    {
        var ready = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            var f = new System.Windows.Forms.Form { Text = "PagingProbe decoy (foreground)", Width = 360, Height = 120, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Left = 0, Top = 0, TopMost = false };
            f.Controls.Add(new System.Windows.Forms.TextBox { Dock = System.Windows.Forms.DockStyle.Fill, Multiline = true, Text = "PagingProbe decoy: keeps the target in the background." });
            f.Shown += (_, _) => { _decoy = f.Handle; _front = f.Handle; ready.Set(); };
            System.Windows.Forms.Application.Run(f);
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        ready.Wait(5000);
        EnsureDecoyFront();
    }

    private static void EnsureDecoyFront()
    {
        var want = _front;
        if (want == IntPtr.Zero || GetForegroundWindow() == want) return;
        var fg = GetForegroundWindow();
        uint fgT = GetWindowThreadProcessId(fg, out _), me = GetCurrentThreadId(), dT = GetWindowThreadProcessId(want, out _);
        AttachThreadInput(me, fgT, true); AttachThreadInput(dT, fgT, true);
        SetForegroundWindow(want); BringWindowToTop(want);
        AttachThreadInput(me, fgT, false); AttachThreadInput(dT, fgT, false);
        Thread.Sleep(150);
        if (GetForegroundWindow() != want)
        {
            // fallback: a synthetic ALT tap grants foreground rights (no cursor movement)
            keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(want); Thread.Sleep(150);
        }
        if (GetForegroundWindow() != want) Console.WriteLine($"   !! could not foreground decoy (fg=0x{GetForegroundWindow():X} '{Title(GetForegroundWindow())}')");
    }

    // ======================================================================================
    // Output
    // ======================================================================================
    private static string Fmt(double d) => double.IsNaN(d) ? "" : d.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "pg";

    private static void WriteCsv(string path)
    {
        var sb = new StringBuilder("target,cand,mech,cmd,verdict,pages,oracle,before,after,side,note\n");
        foreach (var r in Results)
            sb.AppendLine(string.Join(",", new[] { r.Target, r.Cand, r.Mech, r.Cmd, r.Verdict, double.IsNaN(r.Pages) ? "" : r.Pages.ToString("F3", CultureInfo.InvariantCulture), r.Oracle, r.Before, r.After, r.Side, r.Note.Trim() }.Select(Csv)));
        File.WriteAllText(path, sb.ToString());
    }

    private static string Csv(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private static void WriteMd(string path, string label, List<Cand> cands)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {label}  — `{Title(_top)}` [{ClassOf(_top)}], {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        foreach (var c in cands) sb.AppendLine($"- **#{c.Id}** {c.Desc}");
        sb.AppendLine();
        sb.AppendLine("| cand | mech | LineDown | PageDown | PageUp | ToTop | ToBottom | notes |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var g in Results.GroupBy(r => (r.Cand, r.Mech)))
        {
            string Cell(Cmd cmd)
            {
                var r = g.FirstOrDefault(x => x.Cmd == cmd.ToString());
                if (r == null) return "";
                return $"{r.Verdict} {Fmt(r.Pages)} {r.Oracle}{(r.Side != "-" ? " **" + r.Side + "**" : "")}".Trim();
            }
            var notes = string.Join("; ", g.Select(r => r.Note.Trim()).Where(n => n.Length > 0).Distinct().Take(2));
            sb.AppendLine($"| {g.Key.Cand} | {g.Key.Mech} | {Cell(Cmd.LineDown)} | {Cell(Cmd.PageDown)} | {Cell(Cmd.PageUp)} | {Cell(Cmd.ToTop)} | {Cell(Cmd.ToBottom)} | {Trunc(notes.Replace("|", "/"), 140)} |");
        }
        File.WriteAllText(path, sb.ToString());
    }

    // ======================================================================================
    // Helpers
    // ======================================================================================
    private static IntPtr FindTop(string? target)
    {
        if (target == null) return IntPtr.Zero;
        IntPtr best = IntPtr.Zero; long bestArea = 0;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            string t = Title(h); if (t.Length == 0) return true;
            if (t.StartsWith("PagingProbe decoy")) return true;
            bool procMatch = ProcName(h).Equals(target, StringComparison.OrdinalIgnoreCase);
            bool match = procMatch || t.Contains(target, StringComparison.OrdinalIgnoreCase);
            if (!match) return true;
            GetWindowRect(h, out var r); long a = (long)r.W * r.H + (procMatch ? 1L << 40 : 0);
            if (a > bestArea) { bestArea = a; best = h; }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    private static List<IntPtr> ChildWindows(IntPtr parent)
    {
        var l = new List<IntPtr>();
        EnumChildWindows(parent, (h, _) => { l.Add(h); return true; }, IntPtr.Zero);
        return l;
    }

    private static bool SbLive(IntPtr h, out SCROLLINFO si)
    {
        si = default;
        if ((GetWindowLong(h, -16) & 0x00200000 /*WS_VSCROLL*/) == 0) return false;
        if (!GetSi(h, 1, out si)) return false;
        return si.nPage > 0 && si.nMax - si.nMin + 1 > si.nPage;
    }

    private static bool GetSi(IntPtr h, int bar, out SCROLLINFO si)
    {
        si = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = 0x17 /*SIF_ALL*/ };
        return GetScrollInfo(h, bar, ref si);
    }

    private static IntPtr Send(IntPtr h, int msg, long w, long l) { SendMessageTimeout(h, (uint)msg, (IntPtr)w, (IntPtr)l, SMTO_ABORTIFHUNG, 1000, out var r); return r; }
    private static IntPtr Send(IntPtr h, int msg, long w, IntPtr l) { SendMessageTimeout(h, (uint)msg, (IntPtr)w, l, SMTO_ABORTIFHUNG, 1000, out var r); return r; }

    private static string Rid(UIA.IUIAutomationElement e)
    {
        try { return string.Join(".", (int[])e.GetRuntimeId()); } catch { return Guid.NewGuid().ToString(); }
    }

    private static bool GetBool(UIA.IUIAutomationElement e, int id) { try { return e.GetCurrentPropertyValue(id) is bool b && b; } catch { return false; } }
    private static double GetDbl(UIA.IUIAutomationElement e, int id) { try { return e.GetCurrentPropertyValue(id) is double d ? d : -1; } catch { return -1; } }
    private static RECT R(UIA.tagRECT r) => new() { L = r.left, T = r.top, R = r.right, B = r.bottom };
    private static RECT Intersect(RECT a, RECT b) => new() { L = Math.Max(a.L, b.L), T = Math.Max(a.T, b.T), R = Math.Min(a.R, b.R), B = Math.Min(a.B, b.B) };
    private static bool Contained(RECT r, RECT v) => r.T >= v.T - 2 && r.B <= v.B + 2 && r.H > 0;
    private static double Overlap(RECT a, RECT b) { var i = Intersect(a, b); return i.W <= 0 || i.H <= 0 ? 0 : (double)i.W * i.H / Math.Max(1.0, (double)a.W * a.H); }
    private static string Trunc(string? s, int n) => s == null ? "" : s.Length <= n ? s : s[..n] + "…";

    private static string CtName(int ct) => ct switch
    {
        CT_Edit => "Edit", CT_List => "List", CT_ListItem => "ListItem", CT_Tree => "Tree", CT_DataGrid => "DataGrid", CT_Document => "Document",
        CT_Pane => "Pane", CT_Table => "Table", CT_Group => "Group", CT_Custom => "Custom", CT_ScrollBar => "ScrollBar", 50032 => "Window", 50018 => "Tab", 50003 => "ComboBox",
        _ => ct.ToString()
    };

    private static string Title(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
    private static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    private static string ProcName(IntPtr h)
    {
        GetWindowThreadProcessId(h, out uint pid);
        try { return Process.GetProcessById((int)pid).ProcessName; } catch { return ""; }
    }

    // ---- P/Invoke ----
    private const uint WM_VSCROLL = 0x0115, EM_SCROLL = 0x00B5, SMTO_ABORTIFHUNG = 0x2;
    private const int SB_ENDSCROLL = 8;

    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] private static extern bool GetScrollInfo(IntPtr h, int bar, ref SCROLLINFO si);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO gi);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
}

[StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
[StructLayout(LayoutKind.Sequential)]
internal struct RECT { public int L, T, R, B; public int W => R - L; public int H => B - T; }
[StructLayout(LayoutKind.Sequential)] internal struct SCROLLINFO { public uint cbSize, fMask; public int nMin, nMax; public uint nPage; public int nPos, nTrackPos; }
[StructLayout(LayoutKind.Sequential)]
internal struct GUITHREADINFO { public int cbSize; public uint flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }

[ComVisible(true)]
internal sealed class NullFocusHandler : UIA.IUIAutomationFocusChangedEventHandler
{
    public void HandleFocusChangedEvent(UIA.IUIAutomationElement sender) { }
}

internal sealed class Opts
{
    public string? Target, Label, Name, Out, FocusName;
    public IntPtr Hwnd;
    public (int X, int Y)? Point;
    public HashSet<int> Cands = new();
    public HashSet<string> Mechs = new(StringComparer.OrdinalIgnoreCase);
    public int Max = 8;
    public bool List, NoDecoy, TargetFg;

    public static Opts Parse(string[] a)
    {
        var o = new Opts();
        for (int i = 0; i < a.Length; i++)
        {
            string Next() => a[++i];
            switch (a[i])
            {
                case "--target": o.Target = Next(); break;
                case "--hwnd": o.Hwnd = new IntPtr(long.Parse(Next().Replace("0x", ""), NumberStyles.HexNumber)); break;
                case "--label": o.Label = Next(); break;
                case "--name": o.Name = Next(); break;
                case "--out": o.Out = Next(); break;
                case "--point": { var p = Next().Split(','); o.Point = (int.Parse(p[0]), int.Parse(p[1])); break; }
                case "--cand": foreach (var s in Next().Split(',')) o.Cands.Add(int.Parse(s)); break;
                case "--mech": foreach (var s in Next().Split(',')) o.Mechs.Add(s); break;
                case "--max": o.Max = int.Parse(Next()); break;
                case "--list": o.List = true; break;
                case "--no-decoy": o.NoDecoy = true; break;
                case "--target-fg": o.TargetFg = true; break;
                case "--focus-name": o.FocusName = Next(); break;
                default: o.Target ??= a[i]; break;
            }
        }
        return o;
    }
}
