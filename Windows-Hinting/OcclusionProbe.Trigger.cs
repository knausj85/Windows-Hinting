// =============================================================================
// PROTOTYPE — auto-hint trigger probe   (issue #123 follow-up, map #44 / #72)
// =============================================================================
// THROWAWAY. `--probe-occlusion --trigger` mode: opens a popup in the target
// (real mouse click via --click NAME, or UIA via --invoke NAME) and records,
// relative to the trigger instant:
//   * every WinEvent from the target process (out-of-context hook; LOCATIONCHANGE
//     only counted), with MSAA name/role for focus/show/menu/dialog events
//   * UIA MenuOpened/Closed, Window_WindowOpened, FocusChanged (global) and
//     ExpandCollapseState property changes (target subtree)
//   * a readiness poll every 50 ms: popup containers (Menu / IsDialog) + their
//     member counts and same-process popup HWNDs, logged on change
// Answers: which signal could auto-trigger hints for this popup, how early, and
// when is its content actually scannable.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace WindowsHinting
{
    internal static partial class OcclusionProbe
    {
        private static readonly Stopwatch T = new();
        private static readonly List<(double ms, string src, string text)> Timeline = new();
        private static readonly Dictionary<string, int> EventCounts = new();

        private static void Mark(string src, string text)
        {
            lock (Timeline) Timeline.Add((T.Elapsed.TotalMilliseconds, src, text));
        }

        private static void Count(string key)
        {
            lock (EventCounts) EventCounts[key] = EventCounts.GetValueOrDefault(key) + 1;
        }

        private static bool AnyPid;
        // Shell surface hosts (per #45's FINDINGS) that are not necessarily WS_EX_TOPMOST.
        private static readonly HashSet<string> ShellPopupClasses = new()
        {
            "Windows.UI.Core.CoreWindow", "Xaml_WindowedPopupClass", "XamlExplorerHostIslandWindow",
            "Microsoft.UI.Content.PopupWindowSiteBridge", "#32768", "TopLevelWindowForOverflowXamlIsland",
            "ControlCenterWindow", "NotifyIconOverflowWindow",
        };
        private static readonly Dictionary<uint, string> ProcNames = new();
        private static string ProcOf(IntPtr h)
        {
            N.GetWindowThreadProcessId(h, out uint pid);
            return ProcOfPid(pid);
        }
        private static string ProcOfPid(uint pid)
        {
            lock (ProcNames)
            {
                if (!ProcNames.TryGetValue(pid, out var n))
                {
                    try { n = Process.GetProcessById((int)pid).ProcessName; } catch { n = pid.ToString(); }
                    ProcNames[pid] = n;
                }
                return n;
            }
        }

        private static void RunTrigger(string[] args, IntPtr target)
        {
            AnyPid = Array.Exists(args, a => a == "--any-pid");
            int watchMs = int.TryParse(Arg(args, "--watch"), out var w) ? w : 3000;
            N.GetWindowThreadProcessId(target, out uint pid);
            var stop = new ManualResetEventSlim(false);

            // ---- WinEvent hook on a pumping STA thread --------------------------------
            var hooked = new ManualResetEventSlim(false);
            var hookThread = new Thread(() =>
            {
                WinEventProc proc = OnWinEvent;
                var hook = SetWinEventHook(0x0001, 0x80FF, IntPtr.Zero, proc, AnyPid ? 0 : pid, 0, 0x0000 | 0x0002 /*OUTOFCONTEXT|SKIPOWNPROCESS*/);
                hooked.Set();
                while (!stop.IsSet) { Application.DoEvents(); Thread.Sleep(2); }
                UnhookWinEvent(hook);
                GC.KeepAlive(proc);
            });
            hookThread.SetApartmentState(ApartmentState.STA);
            hookThread.Start();
            hooked.Wait();

            // ---- UIA subscriptions (MTA) ------------------------------------------------
            IUIAutomation? ua = null;
            RunMta(() =>
            {
                ua = (IUIAutomation)new CUIAutomation();
                var cr = ua.CreateCacheRequest();
                foreach (var p in new[] { UIA_PROPERTY_ID.UIA_NamePropertyId, UIA_PROPERTY_ID.UIA_ControlTypePropertyId,
                             UIA_PROPERTY_ID.UIA_ClassNamePropertyId, UIA_PROPERTY_ID.UIA_AriaRolePropertyId,
                             UIA_PROPERTY_ID.UIA_ProcessIdPropertyId, UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId })
                    cr.AddProperty(p);
                string D(IUIAutomationElement e) =>
                    $"ct={e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ControlTypePropertyId)} " +
                    $"aria='{e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_AriaRolePropertyId)}' " +
                    $"class='{Trunc(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ClassNamePropertyId) as string, 30)}' " +
                    $"name='{Trunc(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId) as string, 40)}' " +
                    $"rect={ToRect(e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId))}" +
                    (AnyPid && e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ProcessIdPropertyId) is int pp ? $" proc={ProcOfPid((uint)pp)}" : "");
                bool Ours(IUIAutomationElement e) => AnyPid || (e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ProcessIdPropertyId) is int p && p == pid);

                var root = ua.GetRootElement();
                var evh = new MenuHandler((e, id) => { try { Mark("UIA", $"{id} {D(e)}{(Ours(e) ? "" : " [other pid]")}"); } catch (Exception ex) { Mark("UIA", $"{id} err {ex.Message}"); } });
                ua.AddAutomationEventHandler(UIA_EVENT_ID.UIA_MenuOpenedEventId, root, TreeScope.TreeScope_Subtree, cr, evh);
                ua.AddAutomationEventHandler(UIA_EVENT_ID.UIA_MenuClosedEventId, root, TreeScope.TreeScope_Subtree, cr, evh);
                ua.AddAutomationEventHandler(UIA_EVENT_ID.UIA_Window_WindowOpenedEventId, root, TreeScope.TreeScope_Subtree, cr, evh);
                ua.AddFocusChangedEventHandler(cr, new FocusHandler(e =>
                {
                    try { if (Ours(e)) Mark("UIA", $"FocusChanged {D(e)}"); } catch (Exception ex) { Mark("UIA", "FocusChanged err " + ex.Message); }
                }));
                var targetEl = ua.ElementFromHandle((HWND)target);
                Windows.Win32.UI_Accessibility_IUIAutomation_Extensions.AddPropertyChangedEventHandlerNativeArray(ua, targetEl, TreeScope.TreeScope_Subtree, cr, new PropHandler((e, id, v) =>
                {
                    try { Mark("UIA", $"PropertyChanged ExpandCollapseState={v} {D(e)}"); } catch (Exception ex) { Mark("UIA", "Prop err " + ex.Message); }
                }), new ReadOnlySpan<UIA_PROPERTY_ID>(new[] { UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId }));
            });
            bool sc = Array.Exists(args, a => a == "--sc");
            if (sc)
                RunMta(() =>
                {
                    var cr = ua!.CreateCacheRequest();
                    cr.AddProperty(UIA_PROPERTY_ID.UIA_ProcessIdPropertyId);
                    cr.AddProperty(UIA_PROPERTY_ID.UIA_ClassNamePropertyId);
                    cr.AddProperty(UIA_PROPERTY_ID.UIA_ControlTypePropertyId);
                    var scRoot = AnyPid ? ua.GetRootElement() : ua.ElementFromHandle((HWND)target);
                    ua.AddStructureChangedEventHandler(scRoot, TreeScope.TreeScope_Subtree, cr, new ScHandler((e, t) =>
                    {
                        if (!T.IsRunning) return;
                        string proc = "?", cls = "", ct = "";
                        try
                        {
                            proc = e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ProcessIdPropertyId) is int pp ? ProcOfPid((uint)pp) : "?";
                            cls = e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ClassNamePropertyId) as string ?? "";
                            ct = e.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ControlTypePropertyId)?.ToString() ?? "";
                        }
                        catch { }
                        lock (ScEvents) ScEvents.Add((T.Elapsed.TotalMilliseconds, t.ToString().Replace("StructureChangeType_", ""), proc, cls, ct));
                    }));
                    Log($"StructureChanged subscribed on {(AnyPid ? "desktop root" : "target")} (Subtree)");
                });
            Log($"trigger: subscribed WinEvents ({(AnyPid ? "ALL processes" : "pid-filtered")}) + UIA MenuOpened/Closed, WindowOpened, FocusChanged, ExpandCollapseState");
            Thread.Sleep(500);
            lock (Timeline) Timeline.Clear();
            lock (ScEvents) ScEvents.Clear();
            lock (EventCounts) EventCounts.Clear();

            // ---- locate the trigger element, then fire ---------------------------------
            string? click = Arg(args, "--click");
            string? invoke = Arg(args, "--invoke");
            string? rclick = Arg(args, "--rclick");
            string? keys = Arg(args, "--keys");
            click ??= rclick;
            Rectangle triggerRect = Rectangle.Empty;
            IUIAutomationExpandCollapsePattern? ec = null;
            IUIAutomationInvokePattern? inv = null;
            IUIAutomationTogglePattern? tog = null;
            RunMta(() =>
            {
                var a = (IUIAutomation)new CUIAutomation();
                var el = FindByName(a, a.ElementFromHandle((HWND)target), click ?? invoke ?? "");
                if (el == null) return;
                triggerRect = ToRect(el.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId));
                ec = el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ExpandCollapsePatternId) as IUIAutomationExpandCollapsePattern;
                inv = el.GetCurrentPattern(UIA_PATTERN_ID.UIA_InvokePatternId) as IUIAutomationInvokePattern;
                tog = el.GetCurrentPattern(UIA_PATTERN_ID.UIA_TogglePatternId) as IUIAutomationTogglePattern;
            });
            if (keys != null) { triggerRect = new Rectangle(0, 0, 1, 1); }
            else if (triggerRect.IsEmpty) { Log($"!! trigger element '{click ?? invoke}' not found"); stop.Set(); return; }
            var pt = Centre(triggerRect);
            Log($"trigger element rect={triggerRect} centre={pt} mode={(keys != null ? "keys " + keys : rclick != null ? "right click" : click != null ? "mouse click" : "UIA")}");

            var readiness = new List<string>();
            var poller = new Thread(() => PollReadiness(target, pid, watchMs, readiness));
            poller.SetApartmentState(ApartmentState.MTA);

            T.Restart();
            Mark("probe", "TRIGGER t0");
            if (keys != null)
            {
                SendKeys.SendWait(keys);
            }
            else if (click != null)
            {
                N.GetCursorPos(out var old);
                SetCursorPos(pt.X, pt.Y);
                uint down = rclick != null ? 0x0008u : 0x0002u;
                mouse_event(down, 0, 0, 0, UIntPtr.Zero);
                mouse_event(down << 1, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(30);
                SetCursorPos(old.X, old.Y); // move away so hover effects don't muddy the read
            }
            else
            {
                RunMta(() => { if (ec != null) ec.Expand(); else if (inv != null) inv.Invoke(); else tog?.Toggle(); });
            }
            Mark("probe", "trigger sent");
            poller.Start();
            poller.Join();
            RunMta(() => DescribePopup(target, click ?? invoke));
            stop.Set();
            hookThread.Join();

            // ---- report ------------------------------------------------------------------
            Log("");
            Log("=== TIMELINE (ms after trigger) ===");
            lock (Timeline)
                foreach (var e in Timeline.OrderBy(e => e.ms))
                    Log($"{e.ms,8:F1}  {e.src,-6} {e.text}");
            Log("");
            ReportStructureChanged();
            Log("=== WinEvent counts (target pid) ===");
            lock (EventCounts)
                foreach (var kv in EventCounts.OrderByDescending(kv => kv.Value))
                    Log($"  {kv.Key,-40} {kv.Value}");
            Log("=== END ===");
        }

        private static readonly List<(double ms, string type, string proc, string cls, string ct)> ScEvents = new();

        private static void ReportStructureChanged()
        {
            List<(double ms, string type, string proc, string cls, string ct)> ev;
            lock (ScEvents) ev = ScEvents.OrderBy(e => e.ms).ToList();
            if (ev.Count == 0) { Log("=== StructureChanged: none (or not subscribed) ==="); return; }
            Log($"=== StructureChanged: {ev.Count} events ===");
            foreach (var g in ev.GroupBy(e => e.proc))
            {
                var l = g.ToList();
                Log($"  proc={g.Key,-24} n={l.Count,4} first={l[0].ms,7:F0} last={l[^1].ms,7:F0}  " +
                    string.Join(" ", l.GroupBy(x => x.type).Select(x => $"{x.Key}={x.Count()}")));
                // bursts: split on gaps > 150 ms
                var bursts = new List<List<(double ms, string type, string proc, string cls, string ct)>> { new() { l[0] } };
                for (int i = 1; i < l.Count; i++)
                {
                    if (l[i].ms - l[i - 1].ms > 150) bursts.Add(new());
                    bursts[^1].Add(l[i]);
                }
                foreach (var b in bursts)
                    Log($"     burst {b[0].ms,7:F0}..{b[^1].ms,7:F0}  n={b.Count,4}  " +
                        string.Join(" ", b.GroupBy(x => x.type).Select(x => $"{x.Key}={x.Count()}")) + "  top senders: " +
                        string.Join(", ", b.GroupBy(x => x.cls + "/" + x.ct).OrderByDescending(x => x.Count()).Take(3).Select(x => $"{Trunc(x.Key, 40)}×{x.Count()}")));
            }
        }

        [ComVisible(true)]
        private sealed class ScHandler : IUIAutomationStructureChangedEventHandler
        {
            private readonly Action<IUIAutomationElement, StructureChangeType> _cb;
            public ScHandler(Action<IUIAutomationElement, StructureChangeType> cb) => _cb = cb;
            public unsafe void HandleStructureChangedEvent(IUIAutomationElement sender, StructureChangeType changeType, Windows.Win32.System.Com.SAFEARRAY* runtimeId) => _cb(sender, changeType);
        }

        private static void DescribePopup(IntPtr target, string? openerSpec)
        {
            var a = (IUIAutomation)new CUIAutomation();
            string D(IUIAutomationElement e) =>
                $"ct={e.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_ControlTypePropertyId)} " +
                $"aria='{e.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_AriaRolePropertyId)}' " +
                $"isDialog={e.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_IsDialogPropertyId)} " +
                $"class='{Trunc(e.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_ClassNamePropertyId) as string, 50)}' " +
                $"name='{Trunc(e.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId) as string, 40)}' " +
                $"rect={ToRect(e.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId))}";
            try
            {
                if (openerSpec != null && FindByName(a, a.ElementFromHandle((HWND)target), openerSpec) is { } opener)
                {
                    var cf = opener.CurrentControllerFor;
                    Log($"opener ControllerFor (aria-controls): {cf?.Length ?? 0}");
                    for (int i = 0; i < (cf?.Length ?? 0); i++) Log($"   -> {D(cf!.GetElement(i))}");
                }
                var f = a.GetFocusedElement();
                var w = a.RawViewWalker;
                Log("focused element ancestry:");
                for (int d = 0; f != null && d < 10; d++, f = w.GetParentElement(f))
                {
                    Log($"   {d}: {D(f)}");
                    if (f.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_ControlTypePropertyId) is 50030) break; // Document
                }
            }
            catch (Exception ex) { Log("DescribePopup error " + ex.Message); }
        }

        private static void PollReadiness(IntPtr target, uint pid, int watchMs, List<string> _)
        {
            var a = (IUIAutomation)new CUIAutomation();
            var cache = a.CreateCacheRequest();
            cache.AddProperty(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_ControlTypePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_AriaRolePropertyId);
            var cond = a.CreateOrCondition(
                a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_MenuControlTypeId),
                a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsDialogPropertyId, true));
            var itemCond = a.CreateOrCondition(
                a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_MenuItemControlTypeId),
                a.CreateOrCondition(
                    a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_HyperlinkControlTypeId),
                    a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId)));
            var root = a.ElementFromHandle((HWND)target);
            string last = "";
            while (T.ElapsedMilliseconds < watchMs)
            {
                double start = T.Elapsed.TotalMilliseconds;
                var parts = new List<string>();
                try
                {
                    var found = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, cond, cache);
                    for (int i = 0; i < found.Length; i++)
                    {
                        var el = found.GetElement(i);
                        var r = ToRect(el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId));
                        if (r.Width <= 0 || el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId) is true) continue;
                        int items = el.FindAll(TreeScope.TreeScope_Descendants, itemCond).Length;
                        parts.Add($"{(el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ControlTypePropertyId) is 50009 ? "Menu" : "Dialog")}" +
                                  $"(aria={el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_AriaRolePropertyId)},items={items},{r.Width}x{r.Height})");
                    }
                }
                catch (Exception ex) { parts.Add("err " + ex.GetType().Name); }
                try
                {
                    var mi = root.FindAllBuildCache(TreeScope.TreeScope_Descendants,
                        a.CreateAndCondition(a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_MenuItemControlTypeId),
                                             a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId, false)), cache);
                    parts.Add($"menuItemsInTree={mi.Length}");
                }
                catch { }
                // Popup candidates: the foreground window plus (with --any-pid) the UIA desktop's
                // top-level children whose class is a shell surface host or that are topmost.
                // (EnumWindows does not return Start/Search CoreWindows: they live in another z-band.)
                var fg = N.GetForegroundWindow();
                var cands = new List<(IUIAutomationElement el, string tag)>();
                try { if (fg != target && fg != IntPtr.Zero) cands.Add((a.ElementFromHandle((HWND)fg), "FG")); } catch { }
                if (AnyPid)
                {
                    try
                    {
                        var kids = a.GetRootElement().FindAll(TreeScope.TreeScope_Children, a.CreateTrueCondition());
                        for (int k = 0; k < kids.Length; k++)
                        {
                            var kid = kids.GetElement(k);
                            var kh = (IntPtr)(int)(kid.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_NativeWindowHandlePropertyId) ?? 0);
                            if (kh == target || kh == fg) continue;
                            string kc = kid.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_ClassNamePropertyId) as string ?? "";
                            bool topmost = kh != IntPtr.Zero && (N.GetWindowLongPtr(kh, -20).ToInt64() & 0x8) != 0;
                            if (kid.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId) is true) continue;
                            if (ToRect(kid.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId)).Width <= 1) continue;
                            if (ShellPopupClasses.Contains(kc) || (topmost && kc != "Shell_TrayWnd" && kc != "Shell_SecondaryTrayWnd"))
                                cands.Add((kid, "TOP"));
                        }
                    }
                    catch { }
                }
                foreach (var (wRoot, tag) in cands)
                {
                    try
                    {
                        var cts = new[] {
                            a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId),
                            a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_ListItemControlTypeId),
                            a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_MenuItemControlTypeId),
                            a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_HyperlinkControlTypeId),
                            a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_TreeItemControlTypeId),
                            a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_CheckBoxControlTypeId) };
                        var clickable = a.CreateOrConditionFromNativeArray(cts, cts.Length);
                        int n = wRoot.FindAll(TreeScope.TreeScope_Descendants,
                            a.CreateAndCondition(clickable, a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId, false))).Length;
                        var wr = ToRect(wRoot.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId));
                        string proc = wRoot.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_ProcessIdPropertyId) is int wp ? ProcOfPid((uint)wp) : "?";
                        parts.Add($"{tag}[{wRoot.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_ClassNamePropertyId)}/{proc} " +
                                  $"'{Trunc(wRoot.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId) as string, 20)}' {wr.Width}x{wr.Height} clickables={n}]");
                    }
                    catch (Exception ex) { parts.Add($"{tag}[err {ex.GetType().Name}]"); }
                }
                var hw = new List<string>();
                N.EnumWindows((h, _) =>
                {
                    if (h == target) return false;
                    N.GetWindowThreadProcessId(h, out uint p);
                    if (!AnyPid && p == pid && N.IsWindowVisible(h))
                    {
                        N.DwmGetWindowAttribute(h, 14, out int cloaked, 4);
                        N.GetWindowRect(h, out var r);
                        if (cloaked == 0 && r.Right > r.Left) hw.Add($"{ClassOf(h)}{(AnyPid ? "/" + ProcOfPid(p) : "")} {r.Right - r.Left}x{r.Bottom - r.Top}");
                    }
                    return true;
                }, IntPtr.Zero);
                foreach (var g in hw.GroupBy(x => x.Split(' ')[0] + (x.EndsWith("x50") ? " keytip-sized" : " " + x.Split(' ')[1])))
                    parts.Add($"HWND[{g.Key}]" + (g.Count() > 1 ? $"x{g.Count()}" : ""));
                string now = parts.Count == 0 ? "(no popup)" : string.Join(" ", parts);
                if (now != last) { Mark("ready", $"{now}   (poll took {T.Elapsed.TotalMilliseconds - start:F0}ms)"); last = now; }
                Thread.Sleep(50);
            }
        }

        private static readonly Dictionary<uint, string> WinEventNames = new()
        {
            [0x0001] = "SYSTEM_SOUND", [0x0002] = "SYSTEM_ALERT", [0x0003] = "SYSTEM_FOREGROUND",
            [0x0004] = "SYSTEM_MENUSTART", [0x0005] = "SYSTEM_MENUEND", [0x0006] = "SYSTEM_MENUPOPUPSTART",
            [0x0007] = "SYSTEM_MENUPOPUPEND", [0x0008] = "SYSTEM_CAPTURESTART", [0x0009] = "SYSTEM_CAPTUREEND",
            [0x0010] = "SYSTEM_DIALOGSTART", [0x0011] = "SYSTEM_DIALOGEND", [0x0016] = "SYSTEM_MINIMIZESTART",
            [0x8000] = "OBJECT_CREATE", [0x8001] = "OBJECT_DESTROY", [0x8002] = "OBJECT_SHOW", [0x8003] = "OBJECT_HIDE",
            [0x8004] = "OBJECT_REORDER", [0x8005] = "OBJECT_FOCUS", [0x8006] = "OBJECT_SELECTION",
            [0x800A] = "OBJECT_STATECHANGE", [0x800B] = "OBJECT_LOCATIONCHANGE", [0x800C] = "OBJECT_NAMECHANGE",
            [0x800E] = "OBJECT_VALUECHANGE", [0x8012] = "OBJECT_DEFACTIONCHANGE", [0x8017] = "OBJECT_CLOAKED",
            [0x8018] = "OBJECT_UNCLOAKED", [0x8019] = "OBJECT_LIVEREGIONCHANGED", [0x8020] = "OBJECT_DRAGSTART",
        };

        private static readonly HashSet<uint> Detailed = new() { 0x0003, 0x0004, 0x0005, 0x0006, 0x0007, 0x0010, 0x0011, 0x8002, 0x8003, 0x8005 };

        private static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (!T.IsRunning) return;
            string name = WinEventNames.GetValueOrDefault(ev) ?? $"0x{ev:X4}";
            bool isWindow = idObject == 0 && idChild == 0; // OBJID_WINDOW / CHILDID_SELF
            Count(name + (isWindow ? " (window)" : ""));
            if (ev == 0x800B || ev == 0x8001 || ev == 0x800C || ev == 0x8004) return; // counted only
            if (!Detailed.Contains(ev) && !(isWindow && (ev == 0x8000 || ev == 0x8017 || ev == 0x8018))) return;
            string acc = "";
            if (!isWindow || ev < 0x8000)
            {
                try
                {
                    if (AccessibleObjectFromEvent(hwnd, idObject, idChild, out var a, out var child) == 0 && a != null)
                        acc = $" role={a.get_accRole(child)} name='{Trunc(a.get_accName(child), 40)}'";
                }
                catch { }
            }
            Mark("WinEv", $"{name} hwnd=0x{hwnd.ToInt64():X}[{ClassOf(hwnd)}]{(AnyPid ? " " + ProcOf(hwnd) : "")} obj={idObject} child={idChild}{acc}");
        }

        private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
        [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromEvent(IntPtr hwnd, int idObject, int idChild,
            [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible acc, [MarshalAs(UnmanagedType.Struct)] out object child);

        [ComVisible(true)]
        private sealed class FocusHandler : IUIAutomationFocusChangedEventHandler
        {
            private readonly Action<IUIAutomationElement> _cb;
            public FocusHandler(Action<IUIAutomationElement> cb) => _cb = cb;
            public void HandleFocusChangedEvent(IUIAutomationElement sender) => _cb(sender);
        }

        [ComVisible(true)]
        private sealed class PropHandler : IUIAutomationPropertyChangedEventHandler
        {
            private readonly Action<IUIAutomationElement, UIA_PROPERTY_ID, object> _cb;
            public PropHandler(Action<IUIAutomationElement, UIA_PROPERTY_ID, object> cb) => _cb = cb;
            public void HandlePropertyChangedEvent(IUIAutomationElement sender, UIA_PROPERTY_ID propertyId, object newValue) => _cb(sender, propertyId, newValue);
        }
    }
}
