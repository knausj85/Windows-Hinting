// =============================================================================
// PROTOTYPE — Occluded-hint probe   (issue #123)
// =============================================================================
// THROWAWAY. Lives only on branch `prototype/occlusion-probe`. Do NOT merge.
// Research: docs/architecture/research/occluded-hints-under-menus.md (§7).
//
// For one target window it runs the REAL production scan
// (UIAutomationService.FindClickableElements) and then scores every hint
// against three occlusion techniques, logging per-hint verdicts + timings:
//
//   4.1 container  — popup containers in the scanned tree (ControlType.Menu or
//                    IsDialog=true, plus any UIA MenuOpened sender). A hint NOT
//                    inside a container whose centre lies in a container rect
//                    is "suppressed". Also reports the "menu-only" policy count.
//   4.2 WFP        — GetAncestor(WindowFromPoint(centre), GA_ROOT) != target
//                    => covered by another top-level window. Corners logged too.
//   4.3 EFP        — UIA ElementFromPoint(centre); visible iff the hit is the
//                    hint or a descendant of it (RuntimeId, parent walk).
//   P3             — re-runs 4.2 with a fake overlay (same ex-styles as
//                    OverlayForm) covering the target, to prove hit-through.
//
// RUN (no console; writes a log next to the exe, path echoed to stdout file):
//   Windows-Hinting.exe --probe-occlusion [--label X] [--title SUBSTR | --delay SEC]
//                       [--invoke NAME] [--keys SENDKEYS] [--settle MS] [--efp-cap N]
//   --title   pick the topmost visible window whose title contains SUBSTR
//   --delay   otherwise wait SEC seconds and probe the foreground window
//   --invoke  before measuring, find a descendant by exact Name and Expand/Invoke it
//   --keys    before measuring, SendKeys.SendWait this string (e.g. "+{F10}", "%f")
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using WindowsHinting.Configuration;
using WindowsHinting.Logging;
using WindowsHinting.Services;

namespace WindowsHinting
{
    internal static partial class OcclusionProbe
    {
        private static StreamWriter? _log;
        private static readonly object Gate = new();

        public static void Run(string[] args)
        {
            string label = Arg(args, "--label") ?? "run";
            string logPath = Path.Combine(AppContext.BaseDirectory,
                $"occlusion-probe-{label}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _log = new StreamWriter(logPath, false, Encoding.UTF8) { AutoFlush = true };
            try
            {
                RunCore(args, label, logPath);
            }
            catch (Exception ex)
            {
                Log($"!! FAILED: {ex}");
            }
            finally
            {
                _log.Dispose();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "occlusion-probe-last.txt"), logPath);
            }
        }

        private static void RunCore(string[] args, string label, string logPath)
        {
            if (Array.Exists(args, a => a == "--pmv2")) Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Log($"=== OCCLUSION PROBE (#123) label={label} ===");
            Log($"log: {logPath}");
            Log($"OS {Environment.OSVersion}  arch {RuntimeInformation.ProcessArchitecture}");
            Log($"thread DPI awareness: {N.GetAwarenessFromDpiAwarenessContext(N.GetThreadDpiAwarenessContext())} (0=unaware,1=system,2=per-monitor)");

            int settleMs = int.TryParse(Arg(args, "--settle"), out var s) ? s : 1500;
            int efpCap = int.TryParse(Arg(args, "--efp-cap"), out var c) ? c : 600;

            // ---- pick target --------------------------------------------------
            IntPtr target;
            string? title = Arg(args, "--title");
            if (title != null)
            {
                target = FindWindowByTitle(title);
                if (target == IntPtr.Zero) { Log($"!! no visible window title contains '{title}'"); return; }
                ForceForeground(target);
            }
            else
            {
                int delay = int.TryParse(Arg(args, "--delay"), out var d) ? d : 5;
                Log($"waiting {delay}s, then probing the foreground window...");
                Thread.Sleep(delay * 1000);
                target = N.GetForegroundWindow();
            }
            Log($"target: {Describe(target)}  dpi={N.GetDpiForWindow(target)}");
            if (Array.Exists(args, a => a == "--trigger")) { RunTrigger(args, target); return; }

            // ---- MenuOpened subscription on an MTA thread (per #48/#77) ------------
            var menuOpened = new List<(long ms, string desc, int[]? rid, Rectangle rect)>();
            var sw = Stopwatch.StartNew();
            IUIAutomation? evAutomation = null;
            RunMta(() =>
            {
                evAutomation = (IUIAutomation)new CUIAutomation();
                var cr = evAutomation.CreateCacheRequest();
                cr.AddProperty(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId);
                cr.AddProperty(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
                cr.AddProperty(UIA_PROPERTY_ID.UIA_NamePropertyId);
                cr.AddProperty(UIA_PROPERTY_ID.UIA_ControlTypePropertyId);
                cr.AddProperty(UIA_PROPERTY_ID.UIA_ClassNamePropertyId);
                cr.AddProperty(UIA_PROPERTY_ID.UIA_NativeWindowHandlePropertyId);
                var handler = new MenuHandler((el, id) =>
                {
                    try
                    {
                        var rid = el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId) as int[];
                        var rect = ToRect(el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId));
                        string desc = $"{id} ct={el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ControlTypePropertyId)} " +
                                      $"name='{el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId)}' " +
                                      $"class='{el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ClassNamePropertyId)}' " +
                                      $"hwnd=0x{el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_NativeWindowHandlePropertyId):X} rect={rect}";
                        lock (menuOpened) menuOpened.Add((sw.ElapsedMilliseconds, desc, rid, rect));
                    }
                    catch (Exception ex) { lock (menuOpened) menuOpened.Add((sw.ElapsedMilliseconds, "handler error " + ex.Message, null, Rectangle.Empty)); }
                });
                var root = evAutomation.GetRootElement();
                evAutomation.AddAutomationEventHandler(UIA_EVENT_ID.UIA_MenuOpenedEventId, root, TreeScope.TreeScope_Subtree, cr, handler);
                evAutomation.AddAutomationEventHandler(UIA_EVENT_ID.UIA_MenuClosedEventId, root, TreeScope.TreeScope_Subtree, cr, handler);
            });
            Log("subscribed MenuOpened/MenuClosed (root, Subtree)");

            // ---- drive the scenario ------------------------------------------------
            string? invoke = Arg(args, "--invoke");
            string? keys = Arg(args, "--keys");
            if (invoke != null) RunMta(() => InvokeByName(target, invoke));
            if (keys != null) { Log($"SendKeys '{keys}'  fg before={Describe(N.GetForegroundWindow())}"); SendKeys.SendWait(keys); }
            if (invoke != null || keys != null) { Log($"settling {settleMs}ms"); Thread.Sleep(settleMs); }

            IntPtr fg = N.GetForegroundWindow();
            Log($"foreground at measure: {Describe(fg)}{(fg == target ? "  (== target)" : "  (!= target)")}");
            lock (menuOpened)
                foreach (var m in menuOpened) Log($"  event @{m.ms}ms: {m.desc}");
            if (menuOpened.Count == 0) Log("  (no MenuOpened/Closed events seen)");

            LogZOrderAbove(target);

            // ---- the measurement (MTA) ----------------------------------------------
            List<HintRow>? rows = null;
            RunMta(() => rows = Measure(target, menuOpened, efpCap));
            if (rows == null) return;

            // ---- P3: fake overlay, re-run WFP ----------------------------------------
            var withOverlay = WfpUnderFakeOverlay(target, rows);

            // ---- report ---------------------------------------------------------------
            Log("");
            Log("idx | FINAL    | pop | ct    | in-ctr | 4.1b      | 4.2 WFP (root class)         | corners vis | 4.3 EFP   | ovl | name / rect");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                Log($"{i,3} | {r.Final,-8} | {(r.InPopup ? "yes" : "  -")} | {r.ControlType,5} | {(r.InContainer ? "yes" : "  -"),6} | {(r.Suppress41 ? "SUPPRESS" : "keep"),-9} | " +
                    $"{(r.Covered42 ? "COVERED" : "keep"),-7} {Trunc(r.HitRootClass, 20),-20} | {r.CornersVisible}/4         | {r.Efp,-9} | " +
                    $"{(withOverlay != null && withOverlay[i] != r.Covered42 ? "DIFF" : "ok"),3} | '{Trunc(r.Name, 40)}' {r.Bounds}");
            }

            Log("");
            Log("=== SUMMARY ===");
            Log($"hints (production scan): {rows.Count}");
            Log($"inside popup containers: {rows.Count(r => r.InContainer)}");
            Log($"4.1b would suppress:      {rows.Count(r => r.Suppress41)}");
            Log($"4.1a menu-only would drop:{(rows.Any(r => r.InContainer) ? rows.Count(r => !r.InContainer) : 0)}");
            Log($"4.2 WFP centre covered:   {rows.Count(r => r.Covered42)}   (of which also 4.1b: {rows.Count(r => r.Covered42 && r.Suppress41)})");
            Log($"4.2 all 5 points covered: {rows.Count(r => r.Covered42 && r.CornersVisible == 0)}");
            Log($"union 4.1b ∪ 4.2:         {rows.Count(r => r.Suppress41 || r.Covered42)}");
            Log($"FINAL: suppress(rect)={rows.Count(r => r.Final == "SUPPRESS")} wfp-drop={rows.Count(r => r.Final == "WFP-DROP")} keep={rows.Count(r => r.Final.StartsWith("keep"))} (keep* = in container/popup but hit elsewhere: {rows.Count(r => r.Final == "keep*")})");
            Log($"in popup HWND subtree:    {rows.Count(r => r.InPopup)}");
            Log($"4.3 EFP verdicts:         {string.Join(", ", rows.GroupBy(r => r.Efp).Select(g => $"{g.Key}={g.Count()}"))}");
            Log($"4.3 EFP 'covered' not caught by 4.1b∪4.2: {rows.Count(r => r.Efp == "covered" && !r.Suppress41 && !r.Covered42)}");
            if (withOverlay != null)
                Log($"P3 overlay: WFP verdict changed for {rows.Where((r, i) => withOverlay[i] != r.Covered42).Count()} hints with fake overlay shown");
            foreach (var t in Timings) Log($"time {t.Key}: {t.Value:F1} ms");
            Log("=== END ===");
        }

        // ---------------------------------------------------------------------------
        private sealed class HintRow
        {
            public string Name = "";
            public int ControlType;
            public Rectangle Bounds;
            public bool InContainer, Suppress41, Covered42, InPopup;
            public string Final = "";
            public int CornersVisible;
            public string HitRootClass = "";
            public string Efp = "-";
        }

        private static readonly Dictionary<string, double> Timings = new();

        private static void Time(string key, Action a)
        {
            var t = Stopwatch.StartNew();
            a();
            Timings[key] = t.Elapsed.TotalMilliseconds;
        }

        private static List<HintRow> Measure(IntPtr target,
            List<(long ms, string desc, int[]? rid, Rectangle rect)> menuOpened, int efpCap)
        {
            var registry = new WindowRuleRegistry();
            registry.SetRules(Array.Empty<WindowRule>());
            using var service = new UIAutomationService(new ProbeLogger(), registry);
            var automation = (IUIAutomation)new CUIAutomation();

            // warm-up so the timed scan is representative
            service.FindClickableElements(target);
            IReadOnlyList<ClickableElement> hints = Array.Empty<ClickableElement>();
            Time("production scan (FindClickableElements)", () => hints = service.FindClickableElements(target));

            // ---- 4.1 containers ------------------------------------------------------
            var containers = new List<(string desc, Rectangle rect, HashSet<string> members)>();
            var root = automation.ElementFromHandle((HWND)target);
            var ctrCache = automation.CreateCacheRequest();
            foreach (var p in new[] { UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId, UIA_PROPERTY_ID.UIA_ControlTypePropertyId,
                         UIA_PROPERTY_ID.UIA_NamePropertyId, UIA_PROPERTY_ID.UIA_IsDialogPropertyId, UIA_PROPERTY_ID.UIA_IsPeripheralPropertyId,
                         UIA_PROPERTY_ID.UIA_AriaRolePropertyId, UIA_PROPERTY_ID.UIA_ClassNamePropertyId, UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId,
                         UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId })
                ctrCache.AddProperty(p);
            var ctrCond = automation.CreateOrCondition(
                automation.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_MenuControlTypeId),
                automation.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsDialogPropertyId, true));
            var ridCache = automation.CreateCacheRequest();
            ridCache.AddProperty(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId);
            var trueCond = automation.CreateTrueCondition();

            IUIAutomationElementArray? found = null;
            Time("4.1 container FindAll", () => found = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, ctrCond, ctrCache));
            var membersTimer = Stopwatch.StartNew();
            Log("");
            Log($"popup containers in target tree: {found?.Length ?? 0}");
            for (int i = 0; i < (found?.Length ?? 0); i++)
            {
                var el = found!.GetElement(i);
                var rect = ToRect(el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId));
                string desc = $"ct={el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ControlTypePropertyId)} " +
                              $"isDialog={el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsDialogPropertyId)} " +
                              $"isPeripheral={el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsPeripheralPropertyId)} " +
                              $"aria='{el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_AriaRolePropertyId)}' " +
                              $"class='{el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ClassNamePropertyId)}' " +
                              $"offscreen={el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId)} " +
                              $"name='{Trunc(el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId) as string, 40)}' rect={rect}";
                bool usable = rect.Width > 0 && rect.Height > 0 &&
                              el.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId) is not true;
                var members = new HashSet<string>();
                if (usable)
                {
                    var sub = el.FindAllBuildCache(TreeScope.TreeScope_Descendants, trueCond, ridCache);
                    for (int j = 0; j < sub.Length; j++)
                        members.Add(Rid(sub.GetElement(j).GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId)));
                    containers.Add((desc, rect, members));
                }
                Log($"  [{(usable ? "USE" : "skip")}] {desc}  members={members.Count}");
            }
            Timings["4.1 container member FindAlls"] = membersTimer.Elapsed.TotalMilliseconds;

            // MenuOpened senders that are still alive and inside the target's tree count too.
            lock (menuOpened)
                foreach (var m in menuOpened.Where(m => m.desc.StartsWith("UIA_MenuOpened") && m.rid != null && m.rect.Width > 0))
                    if (!containers.Any(c => c.rect == m.rect))
                        Log($"  (MenuOpened sender not among tree containers: {m.desc})");

            // ---- popup HWNDs: visible same-process windows above the target ------------
            // (Chromium Views menus, XAML PopupHost, #32768). Their UIA subtree = members,
            // their window rect = occluding rect.
            var popups = new List<(IntPtr hwnd, Rectangle rect, HashSet<string> members)>();
            var popupTimer = Stopwatch.StartNew();
            N.GetWindowThreadProcessId(target, out uint targetPid);
            N.EnumWindows((h, _) =>
            {
                if (h == target) return false;
                N.GetWindowThreadProcessId(h, out uint pid);
                if (pid != targetPid || !N.IsWindowVisible(h)) return true;
                N.DwmGetWindowAttribute(h, 14, out int cloaked, 4);
                if (cloaked != 0) return true;
                N.GetWindowRect(h, out var r);
                popups.Add((h, Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), new HashSet<string>()));
                return true;
            }, IntPtr.Zero);
            foreach (var pw in popups)
            {
                try
                {
                    var pe = automation.ElementFromHandle((HWND)pw.hwnd);
                    pw.members.Add(Rid(pe.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId)));
                    var sub = pe.FindAllBuildCache(TreeScope.TreeScope_Descendants, trueCond, ridCache);
                    for (int j = 0; j < sub.Length; j++)
                        pw.members.Add(Rid(sub.GetElement(j).GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId)));
                }
                catch (Exception ex) { Log($"  popup 0x{pw.hwnd:X} UIA error {ex.Message}"); }
                Log($"  [popup HWND] {Describe(pw.hwnd)} rect={pw.rect} uiaMembers={pw.members.Count}");
            }
            Timings["popup HWND enum + member FindAlls"] = popupTimer.Elapsed.TotalMilliseconds;

            // ---- per-hint ---------------------------------------------------------------
            // Clip each hint to the target window (huge scroll containers' centres are off-window).
            N.GetWindowRect(target, out var twr);
            var winRect = Rectangle.FromLTRB(twr.Left, twr.Top, twr.Right, twr.Bottom);
            var vis = hints.Select(h => Rectangle.Intersect(h.Bounds, winRect)).ToList();

            var rows = new List<HintRow>();
            var hintRids = new List<string>();
            Time("RuntimeId per hint (probe-only; prod would cache it)", () =>
            {
                foreach (var h in hints)
                {
                    string rid = "";
                    try { rid = Rid(h.Element.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId)); } catch { }
                    hintRids.Add(rid);
                }
            });

            Time("4.1 rect math", () =>
            {
                for (int i = 0; i < hints.Count; i++)
                {
                    var h = hints[i];
                    var row = new HintRow
                    {
                        Bounds = vis[i],
                        Name = h.Element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId) as string ?? "",
                        ControlType = h.Element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ControlTypePropertyId) is int ct ? ct : 0,
                    };
                    var centre = Centre(vis[i]);
                    row.InContainer = containers.Any(c => c.members.Contains(hintRids[i]));
                    row.Suppress41 = !row.InContainer && containers.Any(c => c.rect.Contains(centre));
                    rows.Add(row);
                }
            });

            Time("4.2 WFP centre+corners", () =>
            {
                for (int i = 0; i < hints.Count; i++)
                {
                    var (covered, cls) = WfpCovered(target, Centre(vis[i]));
                    rows[i].Covered42 = covered;
                    rows[i].HitRootClass = cls;
                    rows[i].CornersVisible = Corners(vis[i]).Count(p => !WfpCovered(target, p).covered);
                }
            });
            Time("FINAL rule", () =>
            {
                for (int i = 0; i < hints.Count; i++)
                {
                    var centre = Centre(vis[i]);
                    var home = popups.FirstOrDefault(pw => pw.members.Contains(hintRids[i]));
                    rows[i].InPopup = home.hwnd != IntPtr.Zero;
                    bool inAny = rows[i].InContainer || rows[i].InPopup;
                    bool underRect = containers.Any(c => c.rect.Contains(centre)) || popups.Any(pw => pw.rect.Contains(centre));
                    var hitRoot = N.GetAncestor(N.WindowFromPoint(new N.POINT { X = centre.X, Y = centre.Y }), 2);
                    bool hitOk = hitRoot == target || (rows[i].InPopup && hitRoot == home.hwnd);
                    rows[i].Final = !inAny && underRect ? "SUPPRESS" : !hitOk && !inAny ? "WFP-DROP" : !hitOk ? "keep*" : "keep";
                }
            });
            var wfpOnly = Stopwatch.StartNew();
            foreach (var v in vis) WfpCovered(target, Centre(v));
            Timings["4.2 WFP centre only"] = wfpOnly.Elapsed.TotalMilliseconds;

            // ---- 4.3 ElementFromPoint ---------------------------------------------------
            var walker = automation.RawViewWalker;
            Time($"4.3 EFP (cap {efpCap})", () =>
            {
                for (int i = 0; i < hints.Count && i < efpCap; i++)
                {
                    try
                    {
                        var hit = automation.ElementFromPoint(Centre(vis[i]));
                        string verdict = "covered";
                        var cur = hit;
                        for (int depth = 0; cur != null && depth < 12; depth++)
                        {
                            if (Rid(cur.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId)) == hintRids[i])
                            { verdict = depth == 0 ? "self" : "descend"; break; }
                            cur = walker.GetParentElement(cur);
                        }
                        rows[i].Efp = verdict;
                    }
                    catch (Exception ex) { rows[i].Efp = "err:" + ex.GetType().Name; }
                }
            });

            foreach (var h in hints) try { Marshal.ReleaseComObject(h.Element); } catch { }
            return rows;
        }

        // ---------------------------------------------------------------------------
        private static (bool covered, string cls) WfpCovered(IntPtr target, Point p)
        {
            var hit = N.WindowFromPoint(new N.POINT { X = p.X, Y = p.Y });
            var rootHit = hit == IntPtr.Zero ? IntPtr.Zero : N.GetAncestor(hit, 2 /*GA_ROOT*/);
            return (rootHit != target, ClassOf(rootHit));
        }

        private static bool[]? WfpUnderFakeOverlay(IntPtr target, List<HintRow> rows)
        {
            bool[]? result = null;
            var t = new Thread(() =>
            {
                N.GetWindowRect(target, out var wr);
                using var form = new FakeOverlay
                {
                    StartPosition = FormStartPosition.Manual,
                    Bounds = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom),
                };
                form.Show();
                for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(20); }
                bool visible = N.IsWindowVisible(form.Handle);
                result = rows.Select(r => WfpCovered(target, Centre(r.Bounds)).covered).ToArray();
                Log($"P3 fake overlay hwnd={Describe(form.Handle)} visible={visible} exstyle=0x{N.GetWindowLongPtr(form.Handle, -20):X}");
                form.Close();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            return result;
        }

        private sealed class FakeOverlay : Form
        {
            public FakeOverlay()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                BackColor = Color.Magenta;
                TransparencyKey = Color.Magenta; // same trick a hint overlay uses
                Opacity = 0.99;
            }
            protected override bool ShowWithoutActivation => true;
            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams; // mirror OverlayForm.CreateParams
                    cp.ExStyle |= 0x20 | 0x80 | 0x80000 | 0x8 | 0x08000000;
                    return cp;
                }
            }
        }

        private static void InvokeByName(IntPtr target, string name)
        {
            var a = (IUIAutomation)new CUIAutomation();
            var root = a.ElementFromHandle((HWND)target);
            var el = root.FindFirst(TreeScope.TreeScope_Descendants,
                a.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_NamePropertyId, name));
            if (el == null) { Log($"!! --invoke: no element named '{name}'"); return; }
            Log($"--invoke '{name}' ct={el.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_ControlTypePropertyId)} rect={ToRect(el.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId))}");
            if (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_ExpandCollapsePatternId) is IUIAutomationExpandCollapsePattern ec)
            { ec.Expand(); Log("  -> ExpandCollapse.Expand()"); }
            else if (el.GetCurrentPattern(UIA_PATTERN_ID.UIA_InvokePatternId) is IUIAutomationInvokePattern inv)
            { inv.Invoke(); Log("  -> Invoke()"); }
            else Log("  !! no Expand/Invoke pattern");
        }

        private static void LogZOrderAbove(IntPtr target)
        {
            N.GetWindowRect(target, out var tr);
            var trect = Rectangle.FromLTRB(tr.Left, tr.Top, tr.Right, tr.Bottom);
            Log("visible top-level windows ABOVE target that intersect it (z-order, top first):");
            int n = 0;
            N.EnumWindows((h, _) =>
            {
                if (h == target) return false;
                if (!N.IsWindowVisible(h)) return true;
                N.DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out int cloaked, 4);
                N.GetWindowRect(h, out var r);
                var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                if (rect.IntersectsWith(trect) && rect.Width > 0)
                {
                    Log($"  {Describe(h)} owner={ClassOf(N.GetWindow(h, 4))} cloaked={cloaked} rect={rect}");
                    n++;
                }
                return true;
            }, IntPtr.Zero);
            if (n == 0) Log("  (none)");
        }

        // ---------------------------------------------------------------------------
        private static Point Centre(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);

        private static IEnumerable<Point> Corners(Rectangle r)
        {
            int dx = Math.Max(1, Math.Min(3, r.Width / 4)), dy = Math.Max(1, Math.Min(3, r.Height / 4));
            yield return new(r.Left + dx, r.Top + dy);
            yield return new(r.Right - 1 - dx, r.Top + dy);
            yield return new(r.Left + dx, r.Bottom - 1 - dy);
            yield return new(r.Right - 1 - dx, r.Bottom - 1 - dy);
        }

        private static Rectangle ToRect(object? o) =>
            o is double[] a && a.Length == 4 ? new Rectangle((int)a[0], (int)a[1], (int)a[2], (int)a[3]) : Rectangle.Empty;

        private static string Rid(object? o) => o is int[] a ? string.Join(".", a) : "";

        private static void RunMta(Action a)
        {
            Exception? err = null;
            var t = new Thread(() => { try { a(); } catch (Exception ex) { err = ex; } });
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            t.Join();
            if (err != null) Log($"!! MTA work failed: {err}");
        }

        private static IntPtr FindWindowByTitle(string sub)
        {
            IntPtr found = IntPtr.Zero;
            N.EnumWindows((h, _) =>
            {
                if (N.IsWindowVisible(h) && TitleOf(h).Contains(sub, StringComparison.OrdinalIgnoreCase)) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static void ForceForeground(IntPtr h)
        {
            if (N.GetForegroundWindow() == h) return;
            // Alt tap lifts the foreground lock for a background process.
            N.keybd_event(0x12, 0, 0, UIntPtr.Zero);
            N.keybd_event(0x12, 0, 2, UIntPtr.Zero);
            N.SetForegroundWindow(h);
            Thread.Sleep(400);
        }

        private static string TitleOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            N.GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string ClassOf(IntPtr h)
        {
            if (h == IntPtr.Zero) return "(null)";
            var sb = new StringBuilder(256);
            N.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string Describe(IntPtr h)
        {
            N.GetWindowThreadProcessId(h, out uint pid);
            string proc = "";
            try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { }
            return $"0x{h.ToInt64():X} [{ClassOf(h)}] '{Trunc(TitleOf(h), 50)}' {proc}";
        }

        private static string Trunc(string? s, int n) => s == null ? "" : s.Length <= n ? s : s[..(n - 1)] + "…";

        private static string? Arg(string[] args, string name)
        {
            int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static void Log(string s)
        {
            lock (Gate) _log?.WriteLine(s);
        }

        private sealed class ProbeLogger : ILogger
        {
            public LogLevel MinimumLevel { get; set; } = LogLevel.Info;
            public void Debug(string m, string mn = "", string fp = "") { }
            public void Info(string m, string mn = "", string fp = "") => Log("  [svc] " + m);
            public void Warning(string m, string mn = "", string fp = "") => Log("  [svc WARN] " + m);
            public void Error(string m, Exception? ex = null, string mn = "", string fp = "") => Log($"  [svc ERR] {m} {ex?.Message}");
        }

        [ComVisible(true)]
        private sealed class MenuHandler : IUIAutomationEventHandler
        {
            private readonly Action<IUIAutomationElement, UIA_EVENT_ID> _cb;
            public MenuHandler(Action<IUIAutomationElement, UIA_EVENT_ID> cb) => _cb = cb;
            public void HandleAutomationEvent(IUIAutomationElement sender, UIA_EVENT_ID eventId) => _cb(sender, eventId);
        }

        private static class N
        {
            [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
            [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
            public delegate bool EnumProc(IntPtr h, IntPtr l);

            [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
            [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
            [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
            [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
            [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
            [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
            [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
            [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
            [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
            [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
            [DllImport("user32.dll")] public static extern IntPtr GetThreadDpiAwarenessContext();
            [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
            [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
            [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
            [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        }
    }
}
