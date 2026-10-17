// =============================================================================
// PROTOTYPE — pattern-honesty probe   (issue #124 / #123 follow-up, map #44)
// =============================================================================
// THROWAWAY. `--probe-occlusion --verify` mode. Answers "are the patterns this
// app exposes REAL?" — the first building block of an accessibility profile.
//
//   1. STATIC AUDIT (always): every control element in the scope, with its
//      pattern availability, checked for claims that contradict geometry or
//      each other (IsOffscreen vs ElementFromPoint, LeafNode ExpandCollapse,
//      aria-haspopup without ExpandCollapse, interactive types with no pattern).
//   2. ACTION AUDIT (--act): performs a pattern action, records everything it
//      raises (UIA property/structure/automation events, focus, WinEvents),
//      reads the state back, measures the visible effect, undoes it, and scores
//      the result against the EXPECTATION table below.
//
// RUN:
//   --verify [--scope NAME] [--act SPEC|@x,y|all-expand|all-toggle|all-select|all-scroll]
//            [--pattern expand|toggle|select|invoke|scroll] [--max N] [--wait MS]
//            [--no-undo] [--sc-root]
//   Writes <log>.verify.jsonl: one PROFILE record per audited action.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace WindowsHinting
{
    internal static partial class OcclusionProbe
    {
        // ---- the expectation table: what a REAL implementation of each action does ----
        private sealed record Expect(string Action, string Readback, string PropertyEvent, string Structure,
                                     string OtherEvents, string Effect, string Undo);

        private static readonly Expect[] Expectations =
        {
            new("ExpandCollapse.Expand", "ExpandCollapseState=Expanded", "ExpandCollapseState→Expanded on the element",
                "ChildAdded / ChildrenBulkAdded / ChildrenInvalidated under the element or the popup",
                "MenuOpened + MENUPOPUPSTART (menus); WindowOpened (dialogs)",
                "new actionable descendants, or a new popup container / HWND", "Collapse() → Collapsed, popup gone"),
            new("Toggle.Toggle", "ToggleState changes", "ToggleState change on the element", "none expected",
                "WinEvent OBJECT_STATECHANGE", "none expected (state only)", "Toggle() back to the original state"),
            new("SelectionItem.Select", "IsSelected=true", "IsSelected→true", "content swap for tabs (ChildrenInvalidated)",
                "ElementSelected; WinEvent OBJECT_SELECTION", "selected content changes (tabs) or none (lists)",
                "Select() the previously selected item"),
            new("Invoke.Invoke", "n/a", "n/a", "depends on the command", "Invoke_Invoked",
                "something observable: focus, structure, window or popup", "Esc if a popup opened"),
            new("Scroll.Scroll", "Vertical/HorizontalScrollPercent changes", "ScrollPercent change on the scroller",
                "ChildAdded/Removed when virtualized", "WinEvent SCROLLINGSTART / VALUECHANGE",
                "children move", "SetScrollPercent(previous)"),
        };

        private static readonly Dictionary<int, string> CtNames = new()
        {
            [50000] = "Button", [50001] = "Calendar", [50002] = "CheckBox", [50003] = "ComboBox", [50004] = "Edit",
            [50005] = "Hyperlink", [50006] = "Image", [50007] = "ListItem", [50008] = "List", [50009] = "Menu",
            [50010] = "MenuBar", [50011] = "MenuItem", [50012] = "ProgressBar", [50013] = "RadioButton",
            [50014] = "ScrollBar", [50015] = "Slider", [50016] = "Spinner", [50017] = "StatusBar", [50018] = "Tab",
            [50019] = "TabItem", [50020] = "Text", [50021] = "ToolBar", [50022] = "ToolTip", [50023] = "Tree",
            [50024] = "TreeItem", [50025] = "Custom", [50026] = "Group", [50027] = "Thumb", [50028] = "DataGrid",
            [50029] = "DataItem", [50030] = "Document", [50031] = "SplitButton", [50032] = "Window", [50033] = "Pane",
            [50034] = "Header", [50035] = "HeaderItem", [50036] = "Table", [50037] = "TitleBar", [50038] = "Separator",
            [50039] = "SemanticZoom", [50040] = "AppBar",
        };
        private static string CtName(object? ct) => ct is int i && CtNames.TryGetValue(i, out var n) ? n : ct?.ToString() ?? "?";

        private static readonly HashSet<int> InteractiveCts = new() { 50000, 50002, 50003, 50005, 50007, 50011, 50013, 50019, 50024, 50031, 50029 };

        // ---- event recorder ---------------------------------------------------------------
        private sealed record VEv(double Ms, string Kind, string Detail, string Rid);
        private static readonly List<VEv> VEvents = new();
        private static void VMark(string kind, string detail, string rid = "")
        {
            if (!T.IsRunning) return;
            lock (VEvents) VEvents.Add(new VEv(T.Elapsed.TotalMilliseconds, kind, detail, rid));
        }

        private static readonly Dictionary<uint, string> VWinEv = new()
        {
            [0x0003] = "FOREGROUND", [0x0004] = "MENUSTART", [0x0005] = "MENUEND", [0x0006] = "MENUPOPUPSTART",
            [0x0007] = "MENUPOPUPEND", [0x0012] = "SCROLLINGSTART", [0x0013] = "SCROLLINGEND",
            [0x8000] = "CREATE", [0x8001] = "DESTROY", [0x8002] = "SHOW", [0x8003] = "HIDE", [0x8004] = "REORDER",
            [0x8005] = "FOCUS", [0x8006] = "SELECTION", [0x800A] = "STATECHANGE", [0x800E] = "VALUECHANGE",
        };
        private static WinEventProc? _vProc;

        private sealed record Snap(string State, int Sub, int Win, HashSet<string> Ctrs, HashSet<IntPtr> Hwnds,
                                   string FocusRid, string FocusName);

        private sealed class Row
        {
            public string Element = "", Ct = "", Action = "", Before = "", After = "", Verdict = "", Undo = "";
            public int PropSelf, PropOther, Sc, DSub, DWin, NewHwnds, NewCtrs, PopupItems;
            public string ScTypes = "", Other = "", Focus = "", Error = "";
            public bool ScSelf;
            public double SettleMs, FirstPropMs = -1, FirstScMs = -1;
        }

        // ===================================================================================
        private static void RunVerify(string[] args, IntPtr target, string logPath)
        {
            N.GetWindowThreadProcessId(target, out uint pid);
            string? scope = Arg(args, "--scope");
            string? act = Arg(args, "--act");

            Log("");
            Log("=== EXPECTATIONS (what a REAL pattern implementation does) ===");
            foreach (var e in Expectations)
                Log($"  {e.Action,-22} readback: {e.Readback} | prop: {e.PropertyEvent} | structure: {e.Structure} | " +
                    $"other: {e.OtherEvents} | effect: {e.Effect} | undo: {e.Undo}");

            if (Array.Exists(args, a => a == "--nav")) { RunNav(args, target, pid, logPath); return; }
            RunMta(() => StaticAudit(target, pid, scope));
            if (act == null) { Log("=== END (static audit only; pass --act to exercise patterns) ==="); return; }

            int max = int.TryParse(Arg(args, "--max"), out var m) ? m : 12;
            int waitCap = int.TryParse(Arg(args, "--wait"), out var w) ? w : 1500;
            bool undo = !Array.Exists(args, a => a == "--no-undo");
            string? forced = Arg(args, "--pattern");
            Subscribe(target, pid, Array.Exists(args, a => a == "--sc-root"));
            Thread.Sleep(400);

            // ---- candidates ----------------------------------------------------------------
            var cands = new List<(IUIAutomationElement el, string pattern)>();
            RunMta(() =>
            {
                var a = (IUIAutomation)new CUIAutomation();
                var win = a.ElementFromHandle((HWND)target);
                var root = scope != null ? FindByName(a, win, scope) ?? win : win;
                if (act.StartsWith("all-"))
                {
                    string p = act[4..];
                    var (pid2, _) = PatternIds(p);
                    var cr = a.CreateCacheRequest();
                    cr.AddProperty(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId);
                    cr.AddProperty(UIA_PROPERTY_ID.UIA_IsEnabledPropertyId);
                    cr.AddProperty(UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId);
                    cr.AddProperty(UIA_PROPERTY_ID.UIA_SelectionItemIsSelectedPropertyId);
                    cr.AddProperty(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
                    var all = root.FindAllBuildCache(TreeScope.TreeScope_Descendants,
                        a.CreateAndCondition(a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsControlElementPropertyId, true),
                                             a.CreatePropertyCondition(pid2, true)), cr);
                    for (int i = 0; i < all.Length && cands.Count < max; i++)
                    {
                        var el = all.GetElement(i);
                        if (el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsEnabledPropertyId) is false) continue;
                        if (ToRect(el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId)).Width <= 0) continue;
                        if (p == "expand" && el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId) is not 0) continue;
                        if (p == "select" && el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_SelectionItemIsSelectedPropertyId) is true) continue;
                        cands.Add((el, p));
                    }
                    Log($"candidates for {act}: {all.Length} with the pattern, auditing {cands.Count} (cap --max {max})");
                }
                else
                {
                    IUIAutomationElement? el = TryParseAt(act, out var at) ? a.ElementFromPoint(at.Location) : FindByName(a, root, act);
                    if (el == null) { Log($"!! --act '{act}' not found"); return; }
                    cands.Add((el, forced ?? DefaultPattern(el)));
                }
            });

            var rows = new List<Row>();
            var jsonl = Path.ChangeExtension(logPath, ".verify.jsonl");
            using var jw = new StreamWriter(jsonl, false);
            foreach (var (el, pattern) in cands)
            {
                if (!IsWindowAlive(target)) { Log("!! target window is gone — stopping"); break; }
                var row = VerifyOne(target, pid, el, pattern, waitCap, undo);
                rows.Add(row);
                jw.WriteLine(JsonSerializer.Serialize(new
                {
                    kind = "pattern-action", process = ProcOfPid(pid), windowClass = ClassOf(target),
                    element = row.Element, controlType = row.Ct, action = row.Action, before = row.Before, after = row.After,
                    propertyEvent = row.PropSelf > 0, propertyEventOther = row.PropOther, firstPropertyMs = row.FirstPropMs,
                    structureEvents = row.Sc, structureTypes = row.ScTypes, structureOnSelf = row.ScSelf, firstStructureMs = row.FirstScMs,
                    otherEvents = row.Other, deltaSubtree = row.DSub, deltaWindow = row.DWin, newHwnds = row.NewHwnds,
                    newContainers = row.NewCtrs, popupItems = row.PopupItems, focus = row.Focus, settleMs = row.SettleMs,
                    undo = row.Undo, verdict = row.Verdict, error = row.Error,
                }));
            }

            // ---- report ----------------------------------------------------------------------
            Log("");
            Log("=== ACTION AUDIT (ms after the action; Δsub = actionable descendants, Δwin = on-screen actionables in window) ===");
            Log(" # | VERDICT      | action            | element (ct)                      | readback before→after       | prop self/other (first ms) | structure n (types) [first..settle ms] | other events                          | Δsub Δwin +hwnd +ctr items | focus                | undo");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                Log($"{i,2} | {r.Verdict,-12} | {r.Action,-17} | {Trunc($"{r.Element} ({r.Ct})", 33),-33} | {Trunc($"{r.Before}→{r.After}", 27),-27} | " +
                    $"{r.PropSelf}/{r.PropOther} ({(r.FirstPropMs < 0 ? "-" : r.FirstPropMs.ToString("F0"))})".PadRight(26) + " | " +
                    $"{r.Sc} ({Trunc(r.ScTypes, 22)}){(r.ScSelf ? " self" : "")} [{(r.FirstScMs < 0 ? "-" : r.FirstScMs.ToString("F0"))}..{r.SettleMs:F0}]".PadRight(38) + " | " +
                    $"{Trunc(r.Other, 37),-37} | {r.DSub,4} {r.DWin,4} {r.NewHwnds,5} {r.NewCtrs,4} {r.PopupItems,5} | {Trunc(r.Focus, 20),-20} | {r.Undo}" +
                    (r.Error != "" ? $"  ERR {r.Error}" : ""));
            }
            Log("");
            Log("=== SUMMARY ===");
            foreach (var g in rows.GroupBy(r => r.Action.Split(' ')[0]))
                Log($"  {g.Key,-22} " + string.Join("  ", g.GroupBy(r => r.Verdict.Split(' ')[0]).Select(v => $"{v.Key}={v.Count()}")) +
                    $"   with structure events: {g.Count(r => r.Sc > 0)}/{g.Count()}   undo clean: {g.Count(r => r.Undo.StartsWith("ok"))}/{g.Count()}");
            Log($"profile records: {jsonl}");
            Log("=== END ===");
        }

        // ===================================================================================
        private static void StaticAudit(IntPtr target, uint pid, string? scopeSpec)
        {
            var a = (IUIAutomation)new CUIAutomation();
            var win = a.ElementFromHandle((HWND)target);
            var root = scopeSpec != null ? FindByName(a, win, scopeSpec) ?? win : win;
            var props = new[]
            {
                UIA_PROPERTY_ID.UIA_NamePropertyId, UIA_PROPERTY_ID.UIA_ControlTypePropertyId, UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId,
                UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId, UIA_PROPERTY_ID.UIA_IsEnabledPropertyId, UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId,
                UIA_PROPERTY_ID.UIA_AriaPropertiesPropertyId, UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId,
                UIA_PROPERTY_ID.UIA_IsInvokePatternAvailablePropertyId, UIA_PROPERTY_ID.UIA_IsExpandCollapsePatternAvailablePropertyId,
                UIA_PROPERTY_ID.UIA_IsTogglePatternAvailablePropertyId, UIA_PROPERTY_ID.UIA_IsSelectionItemPatternAvailablePropertyId,
                UIA_PROPERTY_ID.UIA_IsScrollPatternAvailablePropertyId, UIA_PROPERTY_ID.UIA_IsValuePatternAvailablePropertyId,
                UIA_PROPERTY_ID.UIA_IsLegacyIAccessiblePatternAvailablePropertyId, UIA_PROPERTY_ID.UIA_LegacyIAccessibleDefaultActionPropertyId,
                UIA_PROPERTY_ID.UIA_FrameworkIdPropertyId,
            };
            var cr = a.CreateCacheRequest();
            foreach (var p in props) cr.AddProperty(p);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var all = root.FindAllBuildCache(TreeScope.TreeScope_Descendants,
                a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsControlElementPropertyId, true), cr);
            N.GetWindowRect(target, out var wr);
            var wrect = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
            Log("");
            Log($"=== STATIC AUDIT  scope={(root == win ? "window" : scopeSpec)}  control elements={all.Length}  ({sw.ElapsedMilliseconds} ms) ===");

            string[] pats = { "Inv", "EC", "Tog", "Sel", "Scr", "Val", "LegDA" };
            var matrix = new SortedDictionary<string, int[]>(); // ct -> [count, per pattern...]
            var frameworks = new Dictionary<string, int>();
            var checks = new Dictionary<string, List<string>>();
            void Flag(string k, string ex) { if (!checks.TryGetValue(k, out var l)) checks[k] = l = new(); l.Add(ex); }
            int efpBudget = 80;

            for (int i = 0; i < all.Length; i++)
            {
                var e = all.GetElement(i);
                object G(UIA_PROPERTY_ID p) => e.GetCachedPropertyValue(p);
                string name = G(UIA_PROPERTY_ID.UIA_NamePropertyId) as string ?? "";
                var ctRaw = G(UIA_PROPERTY_ID.UIA_ControlTypePropertyId);
                string ct = CtName(ctRaw);
                var rect = ToRect(G(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId));
                bool off = G(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId) is true;
                bool inv = G(UIA_PROPERTY_ID.UIA_IsInvokePatternAvailablePropertyId) is true;
                bool ec = G(UIA_PROPERTY_ID.UIA_IsExpandCollapsePatternAvailablePropertyId) is true;
                bool tog = G(UIA_PROPERTY_ID.UIA_IsTogglePatternAvailablePropertyId) is true;
                bool sel = G(UIA_PROPERTY_ID.UIA_IsSelectionItemPatternAvailablePropertyId) is true;
                bool scr = G(UIA_PROPERTY_ID.UIA_IsScrollPatternAvailablePropertyId) is true;
                bool val = G(UIA_PROPERTY_ID.UIA_IsValuePatternAvailablePropertyId) is true;
                string legDa = G(UIA_PROPERTY_ID.UIA_LegacyIAccessibleDefaultActionPropertyId) as string ?? "";
                string aria = G(UIA_PROPERTY_ID.UIA_AriaPropertiesPropertyId) as string ?? "";
                var ecState = G(UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId);
                string fw = G(UIA_PROPERTY_ID.UIA_FrameworkIdPropertyId) as string ?? "";
                frameworks[fw] = frameworks.GetValueOrDefault(fw) + 1;

                if (!matrix.TryGetValue(ct, out var row)) matrix[ct] = row = new int[pats.Length + 1];
                row[0]++;
                bool[] has = { inv, ec, tog, sel, scr, val, legDa != "" };
                for (int k = 0; k < has.Length; k++) if (has[k]) row[k + 1]++;

                string tag = $"'{Trunc(name, 30)}' ({ct}) {rect}";
                bool actionable = inv || ec || tog || sel;
                var c = Centre(rect);
                bool geomVisible = rect.Width > 0 && rect.Height > 0 && wrect.Contains(c);

                // IsOffscreen claims vs. hit-testing (the VS Code menu-bar case).
                if (off && actionable && geomVisible && efpBudget-- > 0)
                {
                    try
                    {
                        var hit = a.ElementFromPoint(c);
                        if (hit != null && IsSelfOrDescendant(a, e, hit)) Flag("OFFSCREEN_LIE (IsOffscreen=true but ElementFromPoint hits it)", tag);
                    }
                    catch { }
                }
                if (!off && actionable && !geomVisible && rect.Width > 0) Flag("ONSCREEN_OUTSIDE_WINDOW (IsOffscreen=false, centre outside window)", tag);
                if (!off && actionable && rect.Width <= 0) Flag("ONSCREEN_NO_RECT (IsOffscreen=false, empty rect)", tag);
                if (ec && ecState is 3) Flag("EC_LEAF (ExpandCollapse exposed but LeafNode — not an opener)", tag);
                if (ec && ecState is 1) Flag("EC_ALREADY_EXPANDED (info)", tag);
                if (ec && inv) Flag("EC_AND_INVOKE (both patterns — which does activation use?)", tag);
                if (aria.Contains("haspopup=", StringComparison.OrdinalIgnoreCase) && !aria.Contains("haspopup=false", StringComparison.OrdinalIgnoreCase) && !ec)
                    Flag("HASPOPUP_NO_EC (aria-haspopup without ExpandCollapse)", tag);
                if (ctRaw is int ci && InteractiveCts.Contains(ci) && !actionable && !off)
                    Flag($"NO_ACTION_PATTERN (interactive type, no Invoke/EC/Toggle/Select{(legDa != "" ? $"; legacy DA='{legDa}'" : "")})", tag);
                if (actionable && !off && string.IsNullOrWhiteSpace(name)) Flag("NAMELESS_ACTIONABLE", tag);
                if (actionable && G(UIA_PROPERTY_ID.UIA_IsEnabledPropertyId) is false && !off) Flag("DISABLED_ACTIONABLE (info)", tag);
            }

            Log($"frameworks: {string.Join(", ", frameworks.OrderByDescending(kv => kv.Value).Select(kv => $"{(kv.Key == "" ? "(none)" : kv.Key)}={kv.Value}"))}");
            Log("pattern matrix (count of control elements exposing each pattern):");
            Log($"  {"ControlType",-14} {"n",5} " + string.Join(" ", pats.Select(p => $"{p,6}")));
            foreach (var kv in matrix.OrderByDescending(kv => kv.Value[0]))
                Log($"  {kv.Key,-14} {kv.Value[0],5} " + string.Join(" ", kv.Value.Skip(1).Select(v => $"{(v == 0 ? "." : v.ToString()),6}")));
            Log("consistency checks:");
            if (checks.Count == 0) Log("  (none flagged)");
            foreach (var kv in checks.OrderByDescending(kv => kv.Value.Count))
            {
                Log($"  {kv.Value.Count,4}  {kv.Key}");
                foreach (var ex in kv.Value.Take(5)) Log($"          e.g. {ex}");
            }
            if (efpBudget < 0) Log($"  (ElementFromPoint budget exhausted; OFFSCREEN_LIE may be undercounted)");
        }

        private static bool IsSelfOrDescendant(IUIAutomation a, IUIAutomationElement anc, IUIAutomationElement el)
        {
            var w = a.RawViewWalker;
            for (int d = 0; el != null && d < 12; d++, el = w.GetParentElement(el))
                if (a.CompareElements(anc, el)) return true;
            return false;
        }

        // ===================================================================================
        private static void Subscribe(IntPtr target, uint pid, bool scRoot) // scRoot: prop/structure/Invoked on the desktop root (popups in other HWNDs)
        {
            var hooked = new ManualResetEventSlim(false);
            var t = new Thread(() =>
            {
                _vProc = (hook, ev, hwnd, idObj, idChild, th, time) =>
                {
                    if (VWinEv.TryGetValue(ev, out var n)) VMark("win", n + (ev == 0x8002 || ev == 0x8000 ? $"[{ClassOf(hwnd)}]" : ""));
                };
                SetWinEventHook(0x0001, 0x80FF, IntPtr.Zero, _vProc, pid, 0, 0x0002);
                hooked.Set();
                while (true) { Application.DoEvents(); Thread.Sleep(2); }
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            hooked.Wait();

            RunMta(() =>
            {
                var ua = (IUIAutomation)new CUIAutomation();
                var cr = ua.CreateCacheRequest();
                cr.AddProperty(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId);
                cr.AddProperty(UIA_PROPERTY_ID.UIA_ProcessIdPropertyId);
                cr.AddProperty(UIA_PROPERTY_ID.UIA_ControlTypePropertyId);
                cr.AddProperty(UIA_PROPERTY_ID.UIA_NamePropertyId);
                string R(IUIAutomationElement e) { try { return Rid(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId)); } catch { return ""; } }
                bool Ours(IUIAutomationElement e) { try { return e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ProcessIdPropertyId) is int p && p == pid; } catch { return false; } }

                var tEl = ua.ElementFromHandle((HWND)target);
                var root = ua.GetRootElement();
                var evh = new MenuHandler((e, id) => { if (Ours(e)) VMark("uia", id.ToString().Replace("UIA_", "").Replace("EventId", ""), R(e)); });
                foreach (var id in new[] { UIA_EVENT_ID.UIA_MenuOpenedEventId, UIA_EVENT_ID.UIA_MenuClosedEventId, UIA_EVENT_ID.UIA_Window_WindowOpenedEventId })
                    ua.AddAutomationEventHandler(id, root, TreeScope.TreeScope_Subtree, cr, evh);
                foreach (var id in new[] { UIA_EVENT_ID.UIA_Invoke_InvokedEventId, UIA_EVENT_ID.UIA_SelectionItem_ElementSelectedEventId,
                                           UIA_EVENT_ID.UIA_LayoutInvalidatedEventId, UIA_EVENT_ID.UIA_AsyncContentLoadedEventId })
                    ua.AddAutomationEventHandler(id, scRoot ? root : tEl, TreeScope.TreeScope_Subtree, cr, evh);
                ua.AddFocusChangedEventHandler(cr, new FocusHandler(e =>
                {
                    if (Ours(e)) VMark("focus", Trunc(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId) as string, 30), R(e));
                }));
                Windows.Win32.UI_Accessibility_IUIAutomation_Extensions.AddPropertyChangedEventHandlerNativeArray(ua, scRoot ? root : tEl, TreeScope.TreeScope_Subtree, cr,
                    new PropHandler((e, id, v) => { if (Ours(e)) VMark("prop", $"{PropShort(id)}={v}", R(e)); }),
                    new ReadOnlySpan<UIA_PROPERTY_ID>(new[]
                    {
                        UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId, UIA_PROPERTY_ID.UIA_ToggleToggleStatePropertyId,
                        UIA_PROPERTY_ID.UIA_SelectionItemIsSelectedPropertyId, UIA_PROPERTY_ID.UIA_ScrollVerticalScrollPercentPropertyId,
                        UIA_PROPERTY_ID.UIA_ScrollHorizontalScrollPercentPropertyId,
                    }));
                ua.AddStructureChangedEventHandler(scRoot ? root : tEl, TreeScope.TreeScope_Subtree, cr, new ScHandler((e, type) =>
                {
                    if (Ours(e)) VMark("sc", type.ToString().Replace("StructureChangeType_", ""), R(e));
                }));
                Log($"verify: subscribed WinEvents(pid) + UIA prop/structure({(scRoot ? "desktop root, pid-filtered" : "target")})/Invoked/ElementSelected/LayoutInvalidated/Menu*/WindowOpened/Focus");
            });
        }

        private static string PropShort(UIA_PROPERTY_ID id) => id switch
        {
            UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId => "ExpandCollapseState",
            UIA_PROPERTY_ID.UIA_ToggleToggleStatePropertyId => "ToggleState",
            UIA_PROPERTY_ID.UIA_SelectionItemIsSelectedPropertyId => "IsSelected",
            UIA_PROPERTY_ID.UIA_ScrollVerticalScrollPercentPropertyId => "VScroll%",
            UIA_PROPERTY_ID.UIA_ScrollHorizontalScrollPercentPropertyId => "HScroll%",
            _ => id.ToString(),
        };

        private static (UIA_PROPERTY_ID avail, UIA_PATTERN_ID pattern) PatternIds(string p) => p switch
        {
            "expand" => (UIA_PROPERTY_ID.UIA_IsExpandCollapsePatternAvailablePropertyId, UIA_PATTERN_ID.UIA_ExpandCollapsePatternId),
            "toggle" => (UIA_PROPERTY_ID.UIA_IsTogglePatternAvailablePropertyId, UIA_PATTERN_ID.UIA_TogglePatternId),
            "select" => (UIA_PROPERTY_ID.UIA_IsSelectionItemPatternAvailablePropertyId, UIA_PATTERN_ID.UIA_SelectionItemPatternId),
            "scroll" => (UIA_PROPERTY_ID.UIA_IsScrollPatternAvailablePropertyId, UIA_PATTERN_ID.UIA_ScrollPatternId),
            _ => (UIA_PROPERTY_ID.UIA_IsInvokePatternAvailablePropertyId, UIA_PATTERN_ID.UIA_InvokePatternId),
        };

        private static string DefaultPattern(IUIAutomationElement el)
        {
            if (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ExpandCollapsePatternId) is IUIAutomationExpandCollapsePattern ec
                && ec.CurrentExpandCollapseState != ExpandCollapseState.ExpandCollapseState_LeafNode) return "expand";
            if (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_TogglePatternId) != null) return "toggle";
            if (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_SelectionItemPatternId) != null) return "select";
            if (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ScrollPatternId) != null) return "scroll";
            return "invoke";
        }

        private static string ReadState(IUIAutomationElement el, string pattern)
        {
            try
            {
                switch (pattern)
                {
                    case "expand":
                        return (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ExpandCollapsePatternId) as IUIAutomationExpandCollapsePattern)?
                            .CurrentExpandCollapseState.ToString().Replace("ExpandCollapseState_", "") ?? "none";
                    case "toggle":
                        return (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_TogglePatternId) as IUIAutomationTogglePattern)?
                            .CurrentToggleState.ToString().Replace("ToggleState_", "") ?? "none";
                    case "select":
                        return el.GetCurrentPattern(UIA_PATTERN_ID.UIA_SelectionItemPatternId) is IUIAutomationSelectionItemPattern s
                            ? (s.CurrentIsSelected ? "Selected" : "Unselected") : "none";
                    case "scroll":
                        return el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ScrollPatternId) is IUIAutomationScrollPattern sp
                            ? $"v{sp.CurrentVerticalScrollPercent:F0}/h{sp.CurrentHorizontalScrollPercent:F0}" : "none";
                    default:
                        return "n/a";
                }
            }
            catch (Exception ex) { return "err:" + ex.GetType().Name; }
        }

        private static Snap TakeSnap(IntPtr target, uint pid, IUIAutomationElement el, string pattern)
        {
            var a = (IUIAutomation)new CUIAutomation();
            var actionable = a.CreateAndCondition(a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsControlElementPropertyId, true),
                a.CreateOrCondition(
                    a.CreateOrCondition(a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsInvokePatternAvailablePropertyId, true),
                                        a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsExpandCollapsePatternAvailablePropertyId, true)),
                    a.CreateOrCondition(a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsTogglePatternAvailablePropertyId, true),
                                        a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsSelectionItemPatternAvailablePropertyId, true))));
            var onscreen = a.CreateAndCondition(actionable, a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId, false));
            int sub = -1, winN = -1;
            try { sub = el.FindAll(TreeScope.TreeScope_Descendants, actionable).Length; } catch { }
            var win = a.ElementFromHandle((HWND)target);
            try { winN = win.FindAll(TreeScope.TreeScope_Descendants, onscreen).Length; } catch { }

            var hwnds = SamePidPopups(target, pid);
            var ctrs = new HashSet<string>();
            var ctrCond = a.CreateOrCondition(
                a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_MenuControlTypeId),
                a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsDialogPropertyId, true));
            var cr = a.CreateCacheRequest();
            cr.AddProperty(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId);
            void AddCtrs(IUIAutomationElement r)
            {
                try
                {
                    var f = r.FindAllBuildCache(TreeScope.TreeScope_Subtree, ctrCond, cr);
                    for (int i = 0; i < f.Length; i++) { var id = Rid(f.GetElement(i).GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId)); if (id != "") ctrs.Add(id); }
                }
                catch { }
            }
            AddCtrs(win);
            foreach (var h in hwnds) try { AddCtrs(a.ElementFromHandle((HWND)h)); } catch { }

            string fr = "", fn = "";
            try
            {
                var f = a.GetFocusedElement();
                fr = Rid(f.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId));
                fn = f.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId) as string ?? "";
            }
            catch { }
            return new Snap(ReadState(el, pattern), sub, winN, ctrs, hwnds, fr, fn);
        }

        private static HashSet<IntPtr> SamePidPopups(IntPtr target, uint pid)
        {
            var set = new HashSet<IntPtr>();
            N.EnumWindows((h, _) =>
            {
                if (h == target || !N.IsWindowVisible(h)) return true;
                N.GetWindowThreadProcessId(h, out uint p);
                if (p != pid) return true;
                N.DwmGetWindowAttribute(h, 14, out int cloaked, 4);
                N.GetWindowRect(h, out var r);
                if (cloaked == 0 && r.Right > r.Left && r.Bottom > r.Top) set.Add(h);
                return true;
            }, IntPtr.Zero);
            return set;
        }

        private static int CountActionableIn(IEnumerable<IntPtr> hwnds)
        {
            int n = 0;
            var a = (IUIAutomation)new CUIAutomation();
            var cond = a.CreateAndCondition(a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsControlElementPropertyId, true),
                a.CreateOrCondition(a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsInvokePatternAvailablePropertyId, true),
                                    a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsExpandCollapsePatternAvailablePropertyId, true)));
            foreach (var h in hwnds) try { n += a.ElementFromHandle((HWND)h).FindAll(TreeScope.TreeScope_Descendants, cond).Length; } catch { }
            return n;
        }

        private static bool IsWindowAlive(IntPtr h) => N.IsWindowVisible(h);

        // ===================================================================================
        private static Row VerifyOne(IntPtr target, uint pid, IUIAutomationElement el, string pattern, int waitCap, bool undo)
        {
            var row = new Row();
            string rid = "";
            RunMta(() =>
            {
                try
                {
                    row.Element = el.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId) as string ?? "";
                    row.Ct = CtName(el.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_ControlTypePropertyId));
                    rid = Rid(el.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId));
                }
                catch (Exception ex) { row.Error = "describe: " + ex.Message; }
            });
            row.Action = pattern switch
            {
                "expand" => "ExpandCollapse.Expand", "toggle" => "Toggle.Toggle", "select" => "SelectionItem.Select",
                "scroll" => "Scroll.Scroll", _ => "Invoke.Invoke",
            };
            Log($"--- {row.Action} on '{row.Element}' ({row.Ct}) rid={rid}");

            Snap? before = null, after = null;
            double prevH = -1, prevV = -1;
            IUIAutomationElement? prevSelected = null;
            RunMta(() =>
            {
                before = TakeSnap(target, pid, el, pattern);
                if (pattern == "scroll" && el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ScrollPatternId) is IUIAutomationScrollPattern sp)
                { prevH = sp.CurrentHorizontalScrollPercent; prevV = sp.CurrentVerticalScrollPercent; }
                if (pattern == "select" && el.GetCurrentPattern(UIA_PATTERN_ID.UIA_SelectionItemPatternId) is IUIAutomationSelectionItemPattern si)
                {
                    try
                    {
                        if (si.CurrentSelectionContainer?.GetCurrentPattern(UIA_PATTERN_ID.UIA_SelectionPatternId) is IUIAutomationSelectionPattern sel)
                        { var cur = sel.GetCurrentSelection(); if (cur.Length > 0) prevSelected = cur.GetElement(0); }
                    }
                    catch { }
                }
            });
            row.Before = before!.State;

            lock (VEvents) VEvents.Clear();
            T.Restart();
            RunMta(() =>
            {
                try
                {
                    switch (pattern)
                    {
                        case "expand": ((IUIAutomationExpandCollapsePattern)el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ExpandCollapsePatternId)).Expand(); break;
                        case "toggle": ((IUIAutomationTogglePattern)el.GetCurrentPattern(UIA_PATTERN_ID.UIA_TogglePatternId)).Toggle(); break;
                        case "select": ((IUIAutomationSelectionItemPattern)el.GetCurrentPattern(UIA_PATTERN_ID.UIA_SelectionItemPatternId)).Select(); break;
                        case "scroll":
                            var sp = (IUIAutomationScrollPattern)el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ScrollPatternId);
                            bool v = sp.CurrentVerticallyScrollable;
                            sp.Scroll(v ? ScrollAmount.ScrollAmount_NoAmount : ScrollAmount.ScrollAmount_LargeIncrement,
                                      v ? ScrollAmount.ScrollAmount_LargeIncrement : ScrollAmount.ScrollAmount_NoAmount);
                            break;
                        default: ((IUIAutomationInvokePattern)el.GetCurrentPattern(UIA_PATTERN_ID.UIA_InvokePatternId)).Invoke(); break;
                    }
                }
                catch (Exception ex) { row.Error = ex.GetType().Name + ": " + ex.Message.Split('\n')[0]; }
            });
            double callMs = T.Elapsed.TotalMilliseconds;

            // settle: at least 300 ms, then until 250 ms with no events, capped.
            while (T.ElapsedMilliseconds < waitCap)
            {
                Thread.Sleep(25);
                double last;
                lock (VEvents) last = VEvents.Count == 0 ? callMs : VEvents[^1].Ms;
                if (T.ElapsedMilliseconds >= 300 && T.Elapsed.TotalMilliseconds - last >= 250) break;
            }
            List<VEv> evs;
            lock (VEvents) evs = VEvents.ToList();
            row.SettleMs = evs.Count == 0 ? callMs : evs[^1].Ms;

            RunMta(() => after = TakeSnap(target, pid, el, pattern));
            T.Stop();
            row.After = after!.State;
            var newH = after.Hwnds.Except(before.Hwnds).ToList();
            row.NewHwnds = newH.Count;
            row.NewCtrs = after.Ctrs.Except(before.Ctrs).Count();
            row.DSub = after.Sub - before.Sub;
            row.DWin = after.Win - before.Win;
            RunMta(() => row.PopupItems = CountActionableIn(newH));
            row.Focus = after.FocusRid != before.FocusRid ? $"→ '{after.FocusName}'" : "-";

            var props = evs.Where(e => e.Kind == "prop").ToList();
            row.PropSelf = props.Count(e => e.Rid == rid && rid != "");
            row.PropOther = props.Count - row.PropSelf;
            row.FirstPropMs = props.FirstOrDefault(e => e.Rid == rid)?.Ms ?? -1;
            var scs = evs.Where(e => e.Kind == "sc").ToList();
            row.Sc = scs.Count;
            row.ScSelf = scs.Any(e => e.Rid == rid && rid != "");
            row.ScTypes = string.Join(",", scs.GroupBy(e => e.Detail).Select(g => $"{g.Key}×{g.Count()}"));
            row.FirstScMs = scs.FirstOrDefault()?.Ms ?? -1;
            row.Other = string.Join(" ", evs.Where(e => e.Kind is "uia" or "win" or "focus")
                .GroupBy(e => e.Kind == "focus" ? "Focus" : e.Detail)
                .Select(g => $"{g.Key}@{g.First().Ms:F0}{(g.Count() > 1 ? "×" + g.Count() : "")}"));
            foreach (var e in evs) Log($"   {e.Ms,7:F0}  {e.Kind,-5} {e.Detail}{(e.Rid == rid && rid != "" ? "  [self]" : "")}");

            // ---- score against the expectation table ----------------------------------------
            bool effect = row.DSub > 0 || row.DWin != 0 || row.NewHwnds > 0 || row.NewCtrs > 0;
            bool changed = row.Before != row.After;
            bool propEv = row.PropSelf > 0;
            bool popupish = row.NewHwnds > 0 || row.NewCtrs > 0;
            if (row.Error != "") row.Verdict = "FAIL";
            else switch (pattern)
            {
                case "expand":
                    bool expanded = row.After == "Expanded";
                    row.Verdict = row.After == "LeafNode" ? "EMPTY"     // honest: nothing to expand (e.g. empty tree node)
                        : expanded && effect && propEv ? "REAL"
                        : expanded && effect ? "QUIET"            // works, but no state event to drive it
                        : expanded ? "HOLLOW"                    // claims expanded, nothing appeared
                        : effect ? "STALE-STATE"                 // something appeared, state didn't say so
                        : "NO-OP";
                    break;
                case "invoke":
                    bool invoked = evs.Any(e => e.Detail.Contains("Invoke_Invoked"));
                    bool anything = effect || row.Sc > 0 || row.Focus != "-";
                    row.Verdict = invoked && anything ? "REAL" : anything ? "QUIET" : invoked ? "HOLLOW" : "NO-OP";
                    break;
                case "select":
                    bool selEv = propEv || evs.Any(e => e.Detail.Contains("ElementSelected"));
                    row.Verdict = row.Before == "Selected" ? "ALREADY" : row.After == "Selected" ? (selEv ? "REAL" : "QUIET") : "NO-OP";
                    break;
                default: // toggle, scroll: state-only expectations
                    row.Verdict = changed ? (propEv ? "REAL" : "QUIET") : "NO-OP";
                    break;
            }
            if (row.Verdict is "REAL" or "QUIET" && effect && row.Sc == 0) row.Verdict += " no-SC";

            // ---- undo ------------------------------------------------------------------------
            if (!undo) { row.Undo = "skipped"; return row; }
            Thread.Sleep(150);
            string how = "";
            RunMta(() =>
            {
                try
                {
                    switch (pattern)
                    {
                        case "expand":
                            if (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ExpandCollapsePatternId) is IUIAutomationExpandCollapsePattern ec
                                && ec.CurrentExpandCollapseState != ExpandCollapseState.ExpandCollapseState_Collapsed)
                            { ec.Collapse(); how = "Collapse"; }
                            break;
                        case "toggle":
                            var tp = (IUIAutomationTogglePattern)el.GetCurrentPattern(UIA_PATTERN_ID.UIA_TogglePatternId);
                            for (int i = 0; i < 2 && ReadState(el, "toggle") != row.Before; i++) tp.Toggle();
                            how = "Toggle";
                            break;
                        case "select":
                            if (prevSelected != null && row.Before != "Selected")
                            { (prevSelected.GetCurrentPattern(UIA_PATTERN_ID.UIA_SelectionItemPatternId) as IUIAutomationSelectionItemPattern)?.Select(); how = "reselect"; }
                            break;
                        case "scroll":
                            if (prevV >= 0 || prevH >= 0)
                            { ((IUIAutomationScrollPattern)el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ScrollPatternId)).SetScrollPercent(prevH, prevV); how = "SetScrollPercent"; }
                            break;
                    }
                }
                catch (Exception ex) { how += $" err({ex.GetType().Name})"; }
            });
            Thread.Sleep(400);
            Snap? post = null;
            RunMta(() => post = TakeSnap(target, pid, el, pattern));
            bool lingering = post!.Ctrs.Except(before.Ctrs).Any() /* HWND diffs are noisy (tooltips) */;
            if (lingering)
            {
                // Only press Esc when a popup is demonstrably still open (Esc closes some apps' windows).
                N.keybd_event(0x1B, 0, 0, UIntPtr.Zero);
                N.keybd_event(0x1B, 0, 2, UIntPtr.Zero);
                Thread.Sleep(400);
                RunMta(() => post = TakeSnap(target, pid, el, pattern));
                lingering = post!.Ctrs.Except(before.Ctrs).Any() /* HWND diffs are noisy (tooltips) */;
                how += (how == "" ? "" : "+") + "Esc";
            }
            bool stateBack = post.State == row.Before || pattern == "invoke" || post.State == "LeafNode";
            row.Undo = (stateBack && !lingering ? "ok" : "DIRTY") + $" ({(how == "" ? "nothing" : how)}; state {post.State}{(lingering ? "; popup still open" : "")})";
            return row;
        }
    }
}
