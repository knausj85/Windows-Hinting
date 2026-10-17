// =============================================================================
// PROTOTYPE — submenu navigation probe   (issue #124, map #72/#77)
// =============================================================================
// THROWAWAY. `--probe-occlusion --verify --nav` mode. Opens a menu (menu-bar
// item, context menu or keys), then walks its submenus and records, for EVERY
// open and close step, which signals fired: ExpandCollapseState on the item,
// MenuOpened/Closed, MENUPOPUPSTART/END, StructureChanged, focus, and whether a
// popup really appeared/disappeared. Answers: can menu-aware chaining follow
// submenu navigation from events alone, per app and per input style?
//
//   --open SPEC|@x,y   UIA Expand (else Invoke) on a menu-bar item
//   --open-rclick SPEC|@x,y   right-click (context menu)
//   --open-click SPEC|@x,y    real click on a menu-bar item
//   --open-keys KEYS   SendKeys (e.g. "%f", "+{F10}")
//   --nav-mode uia|keys|hover   how submenus are opened (default uia)
//   --max N            submenu parents to walk (default 8)
// Subscriptions are desktop-wide (pid-filtered) so popups in their own HWND count.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace WindowsHinting
{
    internal static partial class OcclusionProbe
    {
        private sealed class NavRow
        {
            public string Step = "", Item = "", Kind = ""; // Kind: open | close | switch | root
            public string EcSelf = "", Menu = "", WinMenu = "", Focus = "", ScTypes = "";
            public int Sc, ItemsAdded, ItemsRemoved, DItems, DPopups, MenusAdded, MenusRemoved;
            public double SettleMs;
            public string Verdict = "";
        }

        private sealed record NavState(HashSet<IntPtr> Hwnds, HashSet<string> Ctrs, HashSet<string> Items, string FocusRid, string FocusName, int FocusCt);

        private static void RunNav(string[] args, IntPtr target, uint pid, string logPath)
        {
            string mode = Arg(args, "--nav-mode") ?? "uia";
            int max = int.TryParse(Arg(args, "--max"), out var m) ? m : 8;
            string? open = Arg(args, "--open"), openR = Arg(args, "--open-rclick"), openK = Arg(args, "--open-keys"), openC = Arg(args, "--open-click");
            bool menuBar = open != null || openC != null;
            openR ??= openC;
            Subscribe(target, pid, scRoot: true);
            // Park the cursor so a stale hover position (e.g. a previous hover run) can't open submenus.
            N.GetCursorPos(out var savedCursor);
            SetCursorPos(0, 0);
            Thread.Sleep(400);

            var baseline = NavSnap(target, pid);
            var parentsBefore = new HashSet<string>();
            RunMta(() => { foreach (var (rid, _) in SubmenuParents(target, pid)) parentsBefore.Add(rid); });
            var rows = new List<NavRow>();
            NavState cur = baseline;

            NavRow Step(string label, string kind, string item, string selfRid, Action act, int minMs = 300)
            {
                var prev = cur;
                lock (VEvents) VEvents.Clear();
                T.Restart();
                act();
                double callMs = T.Elapsed.TotalMilliseconds;
                while (T.ElapsedMilliseconds < Math.Max(1500, minMs + 600))
                {
                    Thread.Sleep(25);
                    double last;
                    lock (VEvents) last = VEvents.Count == 0 ? callMs : VEvents[^1].Ms;
                    if (T.ElapsedMilliseconds >= minMs && T.Elapsed.TotalMilliseconds - last >= 250) break;
                }
                List<VEv> evs;
                lock (VEvents) evs = VEvents.ToList();
                T.Stop();
                cur = NavSnap(target, pid);
                var r = new NavRow { Step = label, Kind = kind, Item = item, SettleMs = evs.Count == 0 ? 0 : evs[^1].Ms };
                var ec = evs.Where(e => e.Kind == "prop" && e.Detail.StartsWith("ExpandCollapseState")).ToList();
                var self = ec.Where(e => e.Rid == selfRid && selfRid != "").ToList();
                r.EcSelf = (self.Count > 0 ? $"{self[0].Detail.Split('=')[1]}@{self[0].Ms:F0}" : "-") + (ec.Count > self.Count ? $" +{ec.Count - self.Count}other({string.Join("", ec.Where(e => !self.Contains(e)).Select(e => e.Detail.Split('=')[1]))})" : "");
                string Fired(string key) { var f = evs.FirstOrDefault(e => e.Detail.StartsWith(key)); return f == null ? "" : $"{key.Replace("Menu", "").Replace("MENU", "")}@{f.Ms:F0}"; }
                r.Menu = string.Join(" ", new[] { Fired("MenuOpened"), Fired("MenuClosed") }.Where(s => s != "")) is var mm && mm != "" ? mm : "-";
                r.WinMenu = string.Join(" ", new[] { Fired("MENUPOPUPSTART"), Fired("MENUPOPUPEND"), Fired("MENUSTART"), Fired("MENUEND") }.Where(s => s != "")) is var ww && ww != "" ? ww : "-";
                var scs = evs.Where(e => e.Kind == "sc").ToList();
                r.Sc = scs.Count;
                r.ScTypes = string.Join(",", scs.GroupBy(e => e.Detail).Select(g => $"{g.Key}×{g.Count()}"));
                r.ItemsAdded = cur.Items.Except(prev.Items).Count();
                r.ItemsRemoved = prev.Items.Except(cur.Items).Count();
                r.DItems = cur.Items.Count - prev.Items.Count;
                // Menu containers by RuntimeId (raw HWND counts are noisy: tooltips, access-key popups).
                r.MenusAdded = cur.Ctrs.Except(prev.Ctrs).Count();
                r.MenusRemoved = prev.Ctrs.Except(cur.Ctrs).Count();
                r.DPopups = r.MenusAdded - r.MenusRemoved;
                r.Focus = cur.FocusRid != prev.FocusRid ? $"→'{Trunc(cur.FocusName, 18)}'" : "-";
                bool menuEv = r.Menu != "-" || r.WinMenu != "-";
                bool visible = kind == "close" ? r.ItemsRemoved > 0 || r.MenusRemoved > 0 : r.ItemsAdded > 0 || r.MenusAdded > 0;
                bool ecOk = self.Count > 0;
                r.Verdict = kind is "open" or "close" or "root"
                    ? (visible ? (ecOk && menuEv ? "ALL" : ecOk ? "EC-only" : menuEv ? "MENU-only" : r.Sc > 0 ? "SC-only" : r.Focus != "-" ? "FOCUS-only" : "SILENT") : (menuEv || ecOk ? "PHANTOM-EV" : "NOTHING"))
                    : (visible || r.ItemsAdded + r.ItemsRemoved > 0 ? (menuEv ? "MENU" : r.Sc > 0 ? "SC-only" : "SILENT") : "nochange");
                Log($"--- {label} [{kind}] '{item}' → {r.Verdict}");
                foreach (var e in evs) Log($"   {e.Ms,7:F0}  {e.Kind,-5} {e.Detail}{(e.Rid == selfRid && selfRid != "" ? "  [self]" : "")}");
                Log($"   state: menuItems {prev.Items.Count}→{cur.Items.Count} (+{r.ItemsAdded}/-{r.ItemsRemoved})  menus {prev.Ctrs.Count}→{cur.Ctrs.Count}  hwnds {prev.Hwnds.Count}→{cur.Hwnds.Count}  focus '{cur.FocusName}'");
                rows.Add(r);
                return r;
            }

            // ---- open the root menu ------------------------------------------------------
            string openerRid = "";
            string openLabel = open != null ? $"UIA open '{open}'" : openC != null ? $"click '{openC}'" : openR != null ? $"right-click '{openR}'" : $"keys '{openK}'";
            Step(openLabel, "root", open ?? openR ?? openK ?? "", "", () =>
            {
                if (openK != null) { SendKeys.SendWait(openK); return; }
                Rectangle rr = Rectangle.Empty;
                IUIAutomationElement? el = null;
                RunMta(() =>
                {
                    var a = (IUIAutomation)new CUIAutomation();
                    var spec = open ?? openR!;
                    el = TryParseAt(spec, out var at) ? a.ElementFromPoint(at.Location) : FindByName(a, a.ElementFromHandle((HWND)target), spec);
                    if (el == null) return;
                    rr = TryParseAt(spec, out var at2) ? at2 : ToRect(el.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId));
                    openerRid = Rid(el.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId));
                    if (open != null)
                    {
                        if (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ExpandCollapsePatternId) is IUIAutomationExpandCollapsePattern ec) ec.Expand();
                        else (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_InvokePatternId) as IUIAutomationInvokePattern)?.Invoke();
                    }
                });
                Log($"opener rect={rr} ct={(el == null ? "?" : CtName(el.CurrentControlType))} fg={Describe(N.GetForegroundWindow())}");
                if (openR != null && !rr.IsEmpty)
                {
                    if (openC == null) RClick(Centre(rr));
                    else { N.GetCursorPos(out var old); var c = Centre(rr); SetCursorPos(c.X, c.Y); mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(30); SetCursorPos(old.X, old.Y); }
                }
            });

            // ---- find submenu parents that appeared with the menu -------------------------
            var parents = new List<(string rid, IUIAutomationElement el)>();
            RunMta(() =>
            {
                DebugParents = Array.Exists(args, x => x == "--dbg");
                var all = SubmenuParents(target, pid);
                DebugParents = false;
                foreach (var (rid, e) in all)
                    Log($"   parent? '{e.CurrentName}' rid={rid} {(parentsBefore.Contains(rid) ? "(existed before)" : rid == openerRid ? "(opener)" : "NEW")}");
                parents = all.Where(p => !parentsBefore.Contains(p.rid) && p.rid != openerRid).ToList();
            });
            Log($"submenu parents in the open menu: {parents.Count}");
            if (Array.Exists(args, x => x == "--list-items")) RunMta(() => ListMenuItems(target, pid, baseline.Items));

            // ---- walk -------------------------------------------------------------------------
            if (mode == "keys")
            {
                // Real keyboard navigation: Down through the menu; on a submenu parent, Right then Left.
                var seen = new HashSet<string>();
                int opened = 0;
                for (int i = 0; i < 40 && opened < max; i++)
                {
                    Step($"Down #{i + 1}", "move", "", "", () => { Tap(0x28); });
                    var f = cur;
                    // Wrapped (a repeat after moving), or focus is not tracking the highlight at all.
                    if (!seen.Add(f.FocusRid) && (seen.Count > 1 || i >= 4)) { if (seen.Count == 1) Log("   !! UIA focus does not follow keyboard highlight"); break; }
                    if (parents.All(p => p.rid != f.FocusRid)) continue;
                    opened++;
                    Step("Right (open submenu)", "open", f.FocusName, f.FocusRid, () => Tap(0x27));
                    Step("Left (close submenu)", "close", f.FocusName, f.FocusRid, () => Tap(0x25));
                }
                if (menuBar)
                {
                    Step("Right on a leaf (next top-level menu)", "switch", "", "", () => Tap(0x27));
                    Step("Left (previous top-level menu)", "switch", "", "", () => Tap(0x25));
                }
            }
            else
            {
                foreach (var (rid, el) in parents.Take(max))
                {
                    string name = "";
                    Rectangle rect = Rectangle.Empty;
                    RunMta(() =>
                    {
                        name = el.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId) as string ?? "";
                        rect = ToRect(el.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId));
                    });
                    if (mode == "hover")
                    {
                        // Hover opens; hovering the next parent closes the previous (captured in that step).
                        // SetCursorPos alone doesn't raise pointer-enter in XAML; follow it with real (relative) mouse moves.
                        Step("hover", "open", name, rid, () =>
                        {
                            SetCursorPos(Centre(rect).X - 2, Centre(rect).Y);
                            mouse_event(0x0001, 1, 0, 0, UIntPtr.Zero);
                            Thread.Sleep(20);
                            mouse_event(0x0001, 1, 0, 0, UIntPtr.Zero);
                        }, minMs: 1000);
                    }
                    else
                    {
                        // ExpandCollapse when it is honest; LeafNode-lying Win32 menus get MSAA DoDefaultAction.
                        bool leaf = false;
                        RunMta(() => leaf = (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ExpandCollapsePatternId) as IUIAutomationExpandCollapsePattern)?
                            .CurrentExpandCollapseState is null or ExpandCollapseState.ExpandCollapseState_LeafNode);
                        if (!leaf)
                        {
                            Step("UIA Expand", "open", name, rid, () => RunMta(() =>
                                (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ExpandCollapsePatternId) as IUIAutomationExpandCollapsePattern)?.Expand()));
                            Step("UIA Collapse", "close", name, rid, () => RunMta(() =>
                                (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ExpandCollapsePatternId) as IUIAutomationExpandCollapsePattern)?.Collapse()));
                        }
                        else
                        {
                            Step("MSAA DoDefaultAction (EC=LeafNode)", "open", name, rid, () => RunMta(() =>
                            {
                                try { (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_LegacyIAccessiblePatternId) as IUIAutomationLegacyIAccessiblePattern)?.DoDefaultAction(); }
                                catch (Exception ex) { Log("   DoDefaultAction: " + ex.Message); }
                            }));
                            Step("Left (close submenu)", "close", name, rid, () => Tap(0x25));
                        }
                    }
                }
            }

            // ---- teardown: Esc only while a menu is demonstrably still open --------------------
            for (int i = 0; i < 4; i++)
            {
                var s = NavSnap(target, pid);
                // Esc only while a menu is demonstrably open (Esc with no menu closes some apps' windows, e.g. Beyond Compare).
                bool open2 = s.Ctrs.Except(baseline.Ctrs).Any() || s.Items.Except(baseline.Items).Count() >= 3;
                if (!open2) break;
                Step($"Esc #{i + 1}", "close", "", "", () => { N.keybd_event(0x1B, 0, 0, UIntPtr.Zero); N.keybd_event(0x1B, 0, 2, UIntPtr.Zero); });
            }

            SetCursorPos(savedCursor.X, savedCursor.Y);

            // ---- report ------------------------------------------------------------------------
            Log("");
            Log($"=== SUBMENU NAVIGATION ({mode}) — ms after each step ===");
            Log(" # | VERDICT    | step                         | item                 | EC item(+other)  | UIA Menu*          | WinEvent menu               | SC n (types)                | items ± menus± | focus");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                Log($"{i,2} | {r.Verdict,-10} | {Trunc(r.Step, 28),-28} | {Trunc(r.Item, 20),-20} | {Trunc(r.EcSelf, 16),-16} | {Trunc(r.Menu, 18),-18} | {Trunc(r.WinMenu, 27),-27} | " +
                    $"{Trunc($"{r.Sc} ({r.ScTypes})", 27),-27} | {$"+{r.ItemsAdded}/-{r.ItemsRemoved}",9} {$"+{r.MenusAdded}/-{r.MenusRemoved}",6} | {r.Focus}");
            }
            Log("");
            Log("=== SIGNAL RELIABILITY (steps where the menu visibly opened/closed) ===");
            foreach (var kind in new[] { "open", "close" })
            {
                var real = rows.Where(r => r.Kind == kind && (kind == "open" ? r.ItemsAdded > 0 || r.MenusAdded > 0 : r.ItemsRemoved > 0 || r.MenusRemoved > 0)).ToList();
                if (real.Count == 0) { Log($"  {kind}: no visible {kind}s"); continue; }
                string Pct(Func<NavRow, bool> f) => $"{real.Count(f)}/{real.Count}";
                bool o = kind == "open";
                string ecv = o ? "1" : "0";
                Log($"  {kind,-5} n={real.Count}  EC→{(o ? "Expanded" : "Collapsed")} on item {Pct(r => r.EcSelf.StartsWith(ecv + "@"))}  (on any item {Pct(r => r.EcSelf.StartsWith(ecv + "@") || r.EcSelf.Contains($"other({ecv}") || (r.EcSelf.Contains("other(") && r.EcSelf[(r.EcSelf.IndexOf("other(") + 6)..].Contains(ecv)))})  " +
                    $"{(o ? "MenuOpened" : "MenuClosed")} {Pct(r => r.Menu.Contains(o ? "Opened@" : "Closed@"))}  {(o ? "MENUPOPUPSTART" : "MENUPOPUPEND")} {Pct(r => r.WinMenu.Contains(o ? "POPUPSTART@" : "POPUPEND@"))}  " +
                    $"StructureChanged {Pct(r => r.Sc > 0)}  focus {Pct(r => r.Focus != "-")}");
            }
            var phantom = rows.Count(r => r.Verdict == "PHANTOM-EV");
            if (phantom > 0) Log($"  PHANTOM-EV steps (events, no visible change): {phantom}");
            using (var jw = new StreamWriter(Path.ChangeExtension(logPath, ".nav.jsonl")))
                foreach (var r in rows)
                    jw.WriteLine(JsonSerializer.Serialize(new { probe = "submenu-nav", process = ProcOfPid(pid), mode, r.Step, r.Kind, r.Item, r.EcSelf, r.Menu, r.WinMenu, r.Sc, r.ScTypes, r.ItemsAdded, r.ItemsRemoved, r.MenusAdded, r.MenusRemoved, r.Focus, r.SettleMs, r.Verdict }));
            Log("=== END ===");
        }

        // Raw extended-key taps (SendKeys didn't reach Explorer's XAML context menu).
        private static void Tap(byte vk)
        {
            N.keybd_event(vk, 0, 1, UIntPtr.Zero);
            N.keybd_event(vk, 0, 1 | 2, UIntPtr.Zero);
        }

        private static NavState NavSnap(IntPtr target, uint pid)
        {
            NavState? s = null;
            RunMta(() =>
            {
                var a = (IUIAutomation)new CUIAutomation();
                var hwnds = SamePidPopups(target, pid);
                var ctrs = new HashSet<string>();
                var items = new HashSet<string>();
                var ctrCond = a.CreateAndCondition(
                    a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_MenuControlTypeId),
                    a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId, false));
                var itemCond = a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_MenuItemControlTypeId);
                var cr = a.CreateCacheRequest();
                cr.AddProperty(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId);
                cr.AddProperty(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
                cr.AddProperty(UIA_PROPERTY_ID.UIA_NamePropertyId);
                foreach (var h in hwnds.Prepend(target))
                {
                    try
                    {
                        var r = a.ElementFromHandle((HWND)h);
                        var f = r.FindAllBuildCache(TreeScope.TreeScope_Subtree, ctrCond, cr);
                        for (int i = 0; i < f.Length; i++)
                        {
                            var e = f.GetElement(i);
                            if (ToRect(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId)).Width <= 0) continue;
                            var id = Rid(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId));
                            if (id != "") ctrs.Add(id);
                        }
                        // Count MenuItems with a real rect (IsOffscreen is unreliable — VS Code).
                        var mi = r.FindAllBuildCache(TreeScope.TreeScope_Descendants, itemCond, cr);
                        for (int i = 0; i < mi.Length; i++)
                        {
                            var e = mi.GetElement(i);
                            if (ToRect(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId)).Width > 0)
                                items.Add(ItemKey(e));
                        }
                    }
                    catch { }
                }
                string fr = "", fn = "";
                int fct = 0;
                try
                {
                    var f = a.GetFocusedElement();
                    fr = Rid(f.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId));
                    fn = f.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId) as string ?? "";
                    fct = f.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_ControlTypePropertyId) is int c ? c : 0;
                }
                catch { }
                s = new NavState(hwnds, ctrs, items, fr, fn, fct);
            });
            return s!;
        }

        // RuntimeId, or (MSAA-proxied Win32 menu items often have none) a name+rect key.
        private static string ItemKey(IUIAutomationElement e)
        {
            var id = Rid(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId));
            return id != "" ? id : $"~{e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId)}|{ToRect(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId))}";
        }

        private static bool DebugParents;
        private static void ListMenuItems(IntPtr target, uint pid, HashSet<string> exclude)
        {
            var a = (IUIAutomation)new CUIAutomation();
            var cr = a.CreateCacheRequest();
            foreach (var p in new[] { UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId, UIA_PROPERTY_ID.UIA_NamePropertyId, UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId,
                         UIA_PROPERTY_ID.UIA_IsExpandCollapsePatternAvailablePropertyId, UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId,
                         UIA_PROPERTY_ID.UIA_IsInvokePatternAvailablePropertyId, UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId, UIA_PROPERTY_ID.UIA_LegacyIAccessibleStatePropertyId })
                cr.AddProperty(p);
            var cond = a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_MenuItemControlTypeId);
            Log("menu items that appeared (name | EC avail/state | Invoke | offscreen | MSAA state):");
            foreach (var h in SamePidPopups(target, pid).Prepend(target))
            {
                try
                {
                    var f = a.ElementFromHandle((HWND)h).FindAllBuildCache(TreeScope.TreeScope_Descendants, cond, cr);
                    for (int i = 0; i < f.Length; i++)
                    {
                        var e = f.GetElement(i);
                        if (exclude.Contains(ItemKey(e))) continue;
                        if (ToRect(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId)).Width <= 0) continue;
                        Log($"   '{Trunc(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId) as string, 30)}' | " +
                            $"{e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsExpandCollapsePatternAvailablePropertyId)}/{e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId)} | " +
                            $"{e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsInvokePatternAvailablePropertyId)} | {e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId)} | " +
                            $"0x{e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_LegacyIAccessibleStatePropertyId):X}");
                    }
                }
                catch { }
            }
        }

        // Submenu parents = MenuItems exposing ExpandCollapse in state Collapsed, OR (Win32/Delphi menus, which
        // report every item as LeafNode) MSAA STATE_SYSTEM_HASPOPUP. Offscreen duplicates are skipped.
        private static List<(string rid, IUIAutomationElement el)> SubmenuParents(IntPtr target, uint pid)
        {
            var a = (IUIAutomation)new CUIAutomation();
            var cond = a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_MenuItemControlTypeId);
            var cr = a.CreateCacheRequest();
            cr.AddProperty(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId);
            cr.AddProperty(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
            cr.AddProperty(UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId);
            cr.AddProperty(UIA_PROPERTY_ID.UIA_LegacyIAccessibleStatePropertyId);
            cr.AddProperty(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId);
            cr.AddProperty(UIA_PROPERTY_ID.UIA_NamePropertyId);
            var list = new List<(string, IUIAutomationElement)>();
            foreach (var h in SamePidPopups(target, pid).Prepend(target))
            {
                try
                {
                    var f = a.ElementFromHandle((HWND)h).FindAllBuildCache(TreeScope.TreeScope_Descendants, cond, cr);
                    for (int i = 0; i < f.Length; i++)
                    {
                        var e = f.GetElement(i);
                        if (ToRect(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId)).Width <= 0) continue;
                        bool ecParent = e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId) is 0;
                        bool msaaPopup = e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_LegacyIAccessibleStatePropertyId) is { } st && (Convert.ToInt64(st) & 0x40000000) != 0
                                         && e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId) is not true;
                        if (DebugParents) Log($"     dbg {ClassOf(h)} st={e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_LegacyIAccessibleStatePropertyId)} ({e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_LegacyIAccessibleStatePropertyId)?.GetType().Name}) off={e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId)} ec={e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId)} -> {ecParent}/{msaaPopup}");
                        if (!ecParent && !msaaPopup) continue;
                        var id = ItemKey(e);
                        if (list.All(x => x.Item1 != id)) list.Add((id, e));
                    }
                }
                catch (Exception ex) { Log($"   SubmenuParents({ClassOf(h)}): {ex.GetType().Name} {ex.Message}"); }
            }
            return list;
        }
    }
}
