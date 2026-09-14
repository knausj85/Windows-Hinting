// PROTOTYPE — throwaway. Wayfinder ticket #61 (parent map #54): select-a-scroll-region
// overlay + actuation spike. Do NOT merge; do NOT productionize.
//
// PURPOSE: lock the #60 go/no-go empirically. Press the global hotkey (Ctrl+Alt+S) to
// enter "scroll mode" over the focused window: the probe detects scrollable regions
// (reusing the #59 census heuristic), overlays a numbered, per-region-colored rect on
// each, and lets you drive scroll from the keyboard. Default target = the active window
// (driven by an injected wheel to the focused control — works even where UIA exposes
// nothing). Type a digit to switch the target to a specific UIA-exposed region (actuated
// via ScrollPattern or ScrollBar RangeValue). Numbers stay shown until you exit (Esc).
//
//   0 / default = active window (fallback wheel)   1-9 = pick a detected region
//   H / J = scroll up / down     K / L = scroll left / right     PgUp / PgDn = page
//   Esc = exit scroll mode
//
// Colors distinguish regions from each other (NOT the UIA path). The console log DOES
// print each region's actuation path so the eval is legible.
//
// Eval matrix: Visual Studio + GitHub Desktop (well-behaved UIA) · VS Code + Slack
// (questionable — no UIA scroll signal, must be carried by the default/fallback wheel).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using UIA = UIAutomationClient;

namespace ScrollSelectProbe;

internal static class Program
{
    // ---- UIA constants (UIAutomationClient.h) ------------------------------
    private const int UIA_RangeValuePatternId = 10003, UIA_ScrollPatternId = 10004;
    private const int UIA_ControlTypePropertyId = 30003;
    private const int UIA_IsRangeValuePatternAvailablePropertyId = 30033, UIA_IsScrollPatternAvailablePropertyId = 30034;
    private const int UIA_ScrollHorizontalScrollPercentPropertyId = 30053, UIA_ScrollHorizontalViewSizePropertyId = 30054;
    private const int UIA_ScrollVerticalScrollPercentPropertyId = 30055, UIA_ScrollVerticalViewSizePropertyId = 30056;
    private const int UIA_ScrollHorizontallyScrollablePropertyId = 30057, UIA_ScrollVerticallyScrollablePropertyId = 30058;
    private const int UIA_ScrollBarControlTypeId = 50014;
    private const double NoScroll = -1.0;

    private static UIA.IUIAutomation _com = null!;

    // ---- scroll-mode state -------------------------------------------------
    private static bool _active;
    private static IntPtr _targetHwnd;
    private static List<Region> _regions = new();
    private static int _activeIndex = -1; // -1 = default (active window / fallback wheel)
    private static Overlay? _overlay;

    private sealed class Region
    {
        public int Number;
        public RECT Rect;
        public Color Color;
        public string PathLabel = "";
        public UIA.IUIAutomationScrollPattern? Sp;
        public UIA.IUIAutomationRangeValuePattern? Rv;
        public bool VScroll, HScroll;
    }

    private static readonly Color[] Palette =
    {
        Color.FromArgb(255,64,64), Color.FromArgb(64,160,255), Color.FromArgb(80,220,120),
        Color.FromArgb(255,180,40), Color.FromArgb(200,100,255), Color.FromArgb(0,220,220),
        Color.FromArgb(255,110,200), Color.FromArgb(160,200,60), Color.FromArgb(255,140,90),
        Color.FromArgb(120,140,255), Color.FromArgb(90,210,180), Color.FromArgb(230,90,120),
    };

    [STAThread]
    private static void Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        try { _com = new UIA.CUIAutomation(); }
        catch (Exception ex) { Console.WriteLine($"UIA init failed: {ex.Message}"); return; }

        var sink = new HotkeySink();
        bool ok = RegisterHotKey(sink.Handle, HotkeyId, MOD_CONTROL | MOD_ALT, VK_S);
        Console.WriteLine(ok
            ? "ScrollSelectProbe ready. Focus a target app, press Ctrl+Alt+S to toggle scroll mode."
            : "!! Ctrl+Alt+S hotkey registration FAILED — use --target <hwndHex> or the 3s fallback.");
        Console.WriteLine("Keys in scroll mode: 0=window · 1-9=region · H/J=up/down · K/L=left/right · PgUp/PgDn=page · Esc=exit");

        // --target <hex>: enter scroll mode on that window ~700ms after the loop starts
        // (no hotkey needed — for smoke-testing / targeting a specific window).
        IntPtr forced = IntPtr.Zero;
        foreach (var a in args)
        {
            string s = a.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? a[2..] : a;
            if (long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long v) && v != 0) forced = new IntPtr(v);
        }
        if (forced != IntPtr.Zero)
        {
            var t = new System.Windows.Forms.Timer { Interval = 700 };
            t.Tick += (_, _) => { t.Stop(); EnterScrollMode(forced); };
            t.Start();
        }

        Application.ApplicationExit += (_, _) => { try { UnregisterHotKey(sink.Handle, HotkeyId); } catch { } ExitScrollMode(); };
        Application.Run();
    }

    // ---- hotkey ------------------------------------------------------------
    private const int HotkeyId = 0x5C01;
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, VK_S = 0x53;
    private const int WM_HOTKEY = 0x0312;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private sealed class HotkeySink : NativeWindow
    {
        public HotkeySink() => CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                if (_active) ExitScrollMode();
                else DeferEnter(GetForegroundWindow()); // defer out of the hotkey handler → reliable z-order
            }
            base.WndProc(ref m);
        }
    }

    // ---- enter / exit ------------------------------------------------------
    // Defer EnterScrollMode out of the WM_HOTKEY handler onto the message loop — creating
    // the topmost overlay from inside the hotkey handler (while another app is foreground)
    // lands it below the active window; a one-shot timer (like --target) shows it on top.
    private static void DeferEnter(IntPtr hwnd)
    {
        var t = new System.Windows.Forms.Timer { Interval = 1 };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); EnterScrollMode(hwnd); };
        t.Start();
    }

    private static void EnterScrollMode(IntPtr hwnd)
    {
        if (_active) return;
        _targetHwnd = hwnd;
        if (_targetHwnd == IntPtr.Zero) return;
        // Electron/Chromium builds its a11y tree lazily AFTER a UIA client attaches (#59),
        // so a cold first query under-reports. Retry a few times until regions appear.
        _regions = DetectRegions(_targetHwnd);
        for (int attempt = 0; _regions.Count == 0 && attempt < 3; attempt++)
        {
            Thread.Sleep(350);
            _regions = DetectRegions(_targetHwnd);
        }
        _activeIndex = -1;
        Console.WriteLine($"── SCROLL MODE on '{Title(_targetHwnd)}' — {_regions.Count} region(s) detected ──");
        foreach (var r in _regions)
            Console.WriteLine($"   [{r.Number}] {r.PathLabel}  rect=({r.Rect.left},{r.Rect.top} {r.Rect.right - r.Rect.left}x{r.Rect.bottom - r.Rect.top})");

        _overlay = new Overlay(_regions);
        _overlay.SetActive(_activeIndex);
        _overlay.Show(); // does not activate (see Overlay.ShowWithoutActivation)
        // force into the topmost band without stealing focus (target app keeps focus so
        // the injected-wheel fallback lands on it) — fixes z-order when shown over an app.
        SetWindowPos(_overlay.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

        _hookProc = HookProc; // root delegate
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        _active = true;
    }

    private static void ExitScrollMode()
    {
        _active = false;
        try { if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook); } catch { }
        _hook = IntPtr.Zero;
        try { _overlay?.Close(); _overlay?.Dispose(); } catch { }
        _overlay = null;
        _regions = new();
        _activeIndex = -1;
        Console.WriteLine("── scroll mode off ──");
    }

    // ---- detection (reuses the #59 census heuristic; no actuation here) ----
    private static List<Region> DetectRegions(IntPtr hwnd)
    {
        var list = new List<Region>();
        UIA.IUIAutomationElement winEl;
        try { winEl = _com.ElementFromHandle(hwnd); } catch { return list; }
        RECT winRect = GetWinRect(winEl);
        CollectFrom(winEl, winRect, list);

        // descend separate child-HWND fragments (hosted islands) — see #59 findings
        try
        {
            var top = winEl; var captured = new List<IntPtr>();
            EnumChildWindows(hwnd, (h, _) =>
            {
                if (list.Count >= 24) return false;
                if (!IsWindowVisible(h) || !GetWindowRect(h, out RECT r) || (r.right - r.left) < 100 || (r.bottom - r.top) < 100) return true;
                foreach (var c in captured) if (IsChild(c, h)) return true;
                UIA.IUIAutomationElement el;
                try { el = _com.ElementFromHandle(h); } catch { return true; }
                if (el is null || Same(el, top) || !IsSeparateFragment(el, top)) return true;
                captured.Add(h);
                CollectFrom(el, winRect, list);
                return true;
            }, IntPtr.Zero);
        }
        catch { }

        // Suppress redundant scrollbars: a ScrollBar region that overlaps a ScrollPattern
        // content region is just that pane's (often detached) scrollbar — drop it and keep
        // the pane. But KEEP scrollbars with no content region beside them (VS editor case),
        // where the scrollbar is the only actuation handle.
        var paneRects = new List<RECT>();
        foreach (var rg in list) if (rg.Sp is not null) paneRects.Add(rg.Rect);
        list.RemoveAll(rg => rg.Sp is null && rg.Rv is not null && OverlapsAny(rg.Rect, paneRects));

        for (int i = 0; i < list.Count; i++) { list[i].Number = i + 1; list[i].Color = Palette[i % Palette.Length]; }
        return list;
    }

    private static void CollectFrom(UIA.IUIAutomationElement root, RECT winRect, List<Region> list)
    {
        UIA.IUIAutomationCondition cond = _com.CreateOrCondition(
            _com.CreatePropertyCondition(UIA_IsScrollPatternAvailablePropertyId, true),
            _com.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_ScrollBarControlTypeId));
        UIA.IUIAutomationElementArray found;
        try { found = root.FindAll(UIA.TreeScope.TreeScope_Subtree, cond); } catch { return; }
        int n = found?.Length ?? 0;
        for (int i = 0; i < n && list.Count < 24; i++)
        {
            UIA.IUIAutomationElement el;
            try { el = found!.GetElement(i); } catch { continue; }
            RECT r = GetRect(el);
            int w = r.right - r.left, h = r.bottom - r.top;
            bool onScreen = w > 0 && h > 0 && r.left < winRect.right && r.right > winRect.left && r.top < winRect.bottom && r.bottom > winRect.top;
            if (!onScreen) continue;
            int ctl = 0; try { ctl = el.CurrentControlType; } catch { }

            if (ctl == UIA_ScrollBarControlTypeId)
            {
                var rv = Get<UIA.IUIAutomationRangeValuePattern>(el, UIA_RangeValuePatternId);
                if (rv is null) continue;
                double mn = Try(() => rv.CurrentMinimum), mx = Try(() => rv.CurrentMaximum);
                if (!(mx > mn)) continue; // no overflow
                list.Add(new Region { Rect = r, Rv = rv, PathLabel = $"ScrollBar RangeValue ({mn:F0}..{mx:F0})" });
                continue;
            }

            var sp = Get<UIA.IUIAutomationScrollPattern>(el, UIA_ScrollPatternId);
            if (sp is null) continue;
            bool vS = GetBool(el, UIA_ScrollVerticallyScrollablePropertyId), hS = GetBool(el, UIA_ScrollHorizontallyScrollablePropertyId);
            double vView = GetDbl(el, UIA_ScrollVerticalViewSizePropertyId), hView = GetDbl(el, UIA_ScrollHorizontalViewSizePropertyId);
            bool vOver = vS && vView < 99.5, hOver = hS && hView < 99.5; // scrollable & overflows at all
            if (!(vOver || hOver)) continue;
            if (w < 60 || h < 60) continue;                 // not a micro-scroller
            // Discriminate real scroll CONTAINERS (Tree/List/etc.) from junk that merely
            // reports scrollable (buttons ~98%, virtual rows ~80%) by control type — this is
            // what lets a barely-overflowing but legit Tree (VS Solution Explorer, viewSize
            // ~96%) through while still rejecting the clutter the viewSize gate used to catch.
            if (!IsScrollContainer(ctl)) continue;
            list.Add(new Region { Rect = r, Sp = sp, VScroll = vOver, HScroll = hOver, PathLabel = $"ScrollPattern {(vOver ? "V" : "")}{(hOver ? "H" : "")} {ControlTypeName(ctl)} (viewSize v={vView:F0} h={hView:F0})" });
        }
    }

    // ---- keyboard hook (active only in scroll mode) ------------------------
    private static LowLevelKeyboardProc _hookProc = null!;
    private static IntPtr _hook;
    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;

    private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _active && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vk = Marshal.ReadInt32(lParam);
            if (Handle(vk)) return (IntPtr)1; // swallow keys we consumed
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool Handle(int vk)
    {
        switch (vk)
        {
            case 0x1B: ExitScrollMode(); return true;                 // Esc
            case 0x30: SetActive(-1); return true;                    // 0 → default/window
            case >= 0x31 and <= 0x39: SetActive((vk - 0x31)); return true; // 1-9 → region idx
            case 0x48: Scroll(0, -1, false); return true;             // H up
            case 0x4A: Scroll(0, +1, false); return true;             // J down
            case 0x4B: Scroll(-1, 0, false); return true;             // K left
            case 0x4C: Scroll(+1, 0, false); return true;             // L right
            case 0x21: Scroll(0, -1, true); return true;              // PageUp
            case 0x22: Scroll(0, +1, true); return true;              // PageDown
            default: return false;
        }
    }

    private static void SetActive(int idx)
    {
        _activeIndex = (idx >= 0 && idx < _regions.Count) ? idx : -1;
        _overlay?.SetActive(_activeIndex);
        Console.WriteLine($"   active target = {(_activeIndex < 0 ? "WINDOW (default/fallback wheel)" : $"region [{_regions[_activeIndex].Number}] — {_regions[_activeIndex].PathLabel}")}");
    }

    // ---- actuation ---------------------------------------------------------
    // dx: -1 left / +1 right; dy: -1 up / +1 down; page: large step.
    private static void Scroll(int dx, int dy, bool page)
    {
        try
        {
            if (_activeIndex >= 0)
            {
                var r = _regions[_activeIndex];
                if (r.Sp is not null) { ScrollViaPattern(r, dx, dy, page); return; }
                if (r.Rv is not null) { ScrollViaRange(r, dx, dy, page); return; }
            }
            // default (window) OR a picked region whose pattern actuation is unavailable → injected wheel
            RECT wr = (_activeIndex >= 0) ? _regions[_activeIndex].Rect : GetWinRectHwnd(_targetHwnd);
            WheelFallback(wr, dx, dy, page);
        }
        catch (Exception ex) { Console.WriteLine($"   !! scroll threw: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void ScrollViaPattern(Region r, int dx, int dy, bool page)
    {
        char axis = (dy != 0 && r.VScroll) ? 'V' : (dx != 0 && r.HScroll) ? 'H' : '\0';
        if (axis == '\0') { WheelFallback(r.Rect, dx, dy, page); return; } // axis not scrollable → wheel
        int sign = axis == 'V' ? dy : dx;                                  // -1 up/left, +1 down/right
        double before = ReadPct(r.Sp!, axis);
        var small = sign < 0 ? UIA.ScrollAmount.ScrollAmount_SmallDecrement : UIA.ScrollAmount.ScrollAmount_SmallIncrement;
        var large = sign < 0 ? UIA.ScrollAmount.ScrollAmount_LargeDecrement : UIA.ScrollAmount.ScrollAmount_LargeIncrement;

        // Escalation ladder — stop at the first rung that actually moves the percent.
        // (1) SmallIncrement: correct + gentle for lists/docs. (2) LargeIncrement: some
        // panes' SmallIncrement is a no-op but Large works — confirmed in Accessibility
        // Insights on PowerPoint's slide-thumbnail pane. (3) SetScrollPercent by a visible
        // delta. (4) injected wheel. A huge list moves on rung 1 (a hair) and never escalates.
        if (!page) { TryScroll(r.Sp!, axis, small); if (MovedSince(r.Sp!, axis, before)) return; }
        TryScroll(r.Sp!, axis, large); if (MovedSince(r.Sp!, axis, before)) return;
        if (before > NoScroll)
        {
            double target = Math.Max(0, Math.Min(100, before + sign * (page ? 40.0 : 12.0)));
            try { if (axis == 'V') r.Sp!.SetScrollPercent(NoScroll, target); else r.Sp!.SetScrollPercent(target, NoScroll); } catch { }
            if (MovedSince(r.Sp!, axis, before)) return;
        }
        WheelFallback(r.Rect, dx, dy, page);
    }

    private static void TryScroll(UIA.IUIAutomationScrollPattern sp, char axis, UIA.ScrollAmount amt)
    {
        try { if (axis == 'V') sp.Scroll(UIA.ScrollAmount.ScrollAmount_NoAmount, amt); else sp.Scroll(amt, UIA.ScrollAmount.ScrollAmount_NoAmount); } catch { }
    }

    private static double ReadPct(UIA.IUIAutomationScrollPattern sp, char axis)
    { try { return axis == 'V' ? sp.CurrentVerticalScrollPercent : sp.CurrentHorizontalScrollPercent; } catch { return NoScroll; } }

    // Did the percent change at all since `before`? Poll briefly (Chromium commits async).
    private static bool MovedSince(UIA.IUIAutomationScrollPattern sp, char axis, double before)
    {
        if (before <= NoScroll) return false; // unreadable → treat as not-moved so we escalate
        for (int i = 0; i < 4; i++)
        {
            double now = ReadPct(sp, axis);
            if (now > NoScroll && Math.Abs(now - before) > 0.0001) return true;
            System.Threading.Thread.Sleep(60);
        }
        return false;
    }

    private static void ScrollViaRange(Region r, int dx, int dy, bool page)
    {
        double mn = Try(() => r.Rv!.CurrentMinimum), mx = Try(() => r.Rv!.CurrentMaximum), cur = Try(() => r.Rv!.CurrentValue);
        if (double.IsNaN(mn) || double.IsNaN(mx) || !(mx > mn)) { WheelFallback(r.Rect, dx, dy, page); return; }
        double step = (mx - mn) * (page ? 0.20 : 0.06);
        int sign = (dy != 0) ? dy : dx;              // up/left decrease, down/right increase
        double next = Math.Max(mn, Math.Min(mx, cur + sign * step));
        try { r.Rv!.SetValue(next); } catch { WheelFallback(r.Rect, dx, dy, page); }
    }

    // Synthesized wheel to the control under the region/window center (no cursor move).
    private static void WheelFallback(RECT rect, int dx, int dy, bool page)
    {
        int cx = (rect.left + rect.right) / 2, cy = (rect.top + rect.bottom) / 2;
        IntPtr h = WindowFromPoint(new POINT { x = cx, y = cy });
        if (h == IntPtr.Zero) h = _targetHwnd;
        int notches = page ? 3 : 1;
        IntPtr lp = (IntPtr)((cy << 16) | (cx & 0xFFFF));
        if (dy != 0)
        {
            int delta = (dy < 0 ? 1 : -1) * 120 * notches;          // wheel-up (+) scrolls content up
            SendMessage(h, WM_MOUSEWHEEL, (IntPtr)(delta << 16), lp);
        }
        else if (dx != 0)
        {
            int delta = (dx < 0 ? -1 : 1) * 120 * notches;          // hwheel + = right
            SendMessage(h, WM_MOUSEHWHEEL, (IntPtr)(delta << 16), lp);
        }
    }

    // ---- overlay -----------------------------------------------------------
    private sealed class Overlay : Form
    {
        private readonly List<Region> _rs;
        private int _act = -1;
        public Overlay(List<Region> rs)
        {
            _rs = rs;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            var vs = SystemInformation.VirtualScreen;
            Bounds = vs;
            BackColor = Color.Black;
            TransparencyKey = Color.Black; // black = transparent (click-through visual)
            DoubleBuffered = true;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/ | 0x20 /*TRANSPARENT*/ | 0x00080000 /*LAYERED*/; return cp; }
        }
        public void SetActive(int idx) { _act = idx; if (IsHandleCreated) BeginInvoke(new Action(Invalidate)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int ox = Left, oy = Top;
            using var badgeFont = new Font("Consolas", 14, FontStyle.Bold);
            using var legendFont = new Font("Consolas", 11, FontStyle.Bold);

            // legend
            string legend = $"SCROLL MODE   active={( _act < 0 ? "WINDOW (default/wheel)" : $"[{_rs[_act].Number}] {_rs[_act].PathLabel}")}   |   0=window  1-9=region  H/J=up/down  K/L=left/right  PgUp/PgDn=page  Esc=exit";
            var lsz = g.MeasureString(legend, legendFont);
            var lr = new RectangleF(8, 8, lsz.Width + 16, lsz.Height + 10);
            using (var bg = new SolidBrush(Color.FromArgb(220, 20, 20, 30))) g.FillRectangle(bg, lr);
            g.DrawString(legend, legendFont, Brushes.White, lr.X + 8, lr.Y + 5);

            foreach (var r in _rs)
            {
                var rect = new Rectangle(r.Rect.left - ox, r.Rect.top - oy, r.Rect.right - r.Rect.left, r.Rect.bottom - r.Rect.top);
                bool active = _act >= 0 && _rs[_act] == r;
                using var pen = new Pen(r.Color, active ? 5 : 3) { DashStyle = active ? System.Drawing.Drawing2D.DashStyle.Solid : System.Drawing.Drawing2D.DashStyle.Dash };
                g.DrawRectangle(pen, rect);
                // number badge (top-left of the region)
                string num = r.Number.ToString(CultureInfo.InvariantCulture);
                var nsz = g.MeasureString(num, badgeFont);
                var br = new RectangleF(rect.X + 2, rect.Y + 2, nsz.Width + 12, nsz.Height + 6);
                using (var bb = new SolidBrush(r.Color)) g.FillRectangle(bb, br);
                g.DrawString(num, badgeFont, Brushes.Black, br.X + 6, br.Y + 3);
            }
        }
    }

    // ---- UIA fragment helpers (from #59) -----------------------------------
    private static bool Same(UIA.IUIAutomationElement a, UIA.IUIAutomationElement b) { try { return _com.CompareElements(a, b) != 0; } catch { return false; } }
    private static bool IsSeparateFragment(UIA.IUIAutomationElement child, UIA.IUIAutomationElement top)
    {
        try { var w = _com.ControlViewWalker; var cur = child; for (int i = 0; i < 80 && cur is not null; i++) { if (Same(cur, top)) return false; cur = w.GetParentElement(cur); } return true; }
        catch { return false; }
    }

    // ---- small helpers -----------------------------------------------------
    // Control types that are real scroll CONTAINERS (vs. buttons/rows/edits that merely
    // report scrollable). List 50008 · Tree 50023 · DataGrid 50028 · Table 50036 ·
    // Document 50030 · Pane 50033 · Group 50026 · Custom 50025 · Window 50032.
    private static readonly HashSet<int> ContainerCtls = new() { 50008, 50023, 50028, 50036, 50030, 50033, 50026, 50025, 50032 };
    private static bool IsScrollContainer(int ctl) => ContainerCtls.Contains(ctl);

    // A scrollbar sits at the edge of its pane, so it intersects (within a small margin)
    // that pane's rect. Used to drop redundant scrollbars when a content region exists.
    private static bool OverlapsAny(RECT bar, List<RECT> panes)
    {
        const int m = 8;
        foreach (var p in panes)
            if (bar.left < p.right + m && bar.right > p.left - m && bar.top < p.bottom + m && bar.bottom > p.top - m)
                return true;
        return false;
    }

    private static readonly Dictionary<int, string> CtlNames = new()
    {
        [50008] = "List", [50023] = "Tree", [50028] = "DataGrid", [50036] = "Table", [50030] = "Document",
        [50033] = "Pane", [50026] = "Group", [50025] = "Custom", [50032] = "Window", [50014] = "ScrollBar",
        [50004] = "Edit", [50000] = "Button", [50011] = "MenuItem", [50007] = "ListItem", [50024] = "TreeItem",
    };
    private static string ControlTypeName(int ctl) => CtlNames.TryGetValue(ctl, out var n) ? n : ctl.ToString();

    private static T? Get<T>(UIA.IUIAutomationElement el, int patternId) where T : class
    { try { return el.GetCurrentPattern(patternId) as T; } catch { return null; } }
    private static bool GetBool(UIA.IUIAutomationElement el, int propId)
    { try { return el.GetCurrentPropertyValue(propId) is bool b && b; } catch { return false; } }
    private static double GetDbl(UIA.IUIAutomationElement el, int propId)
    { try { return el.GetCurrentPropertyValue(propId) is double d ? d : 100.0; } catch { return 100.0; } }
    private static double Try(Func<double> f) { try { return f(); } catch { return double.NaN; } }
    private static RECT GetRect(UIA.IUIAutomationElement el) { try { var t = el.CurrentBoundingRectangle; return new RECT { left = t.left, top = t.top, right = t.right, bottom = t.bottom }; } catch { return default; } }
    private static RECT GetWinRect(UIA.IUIAutomationElement el) => GetRect(el);
    private static RECT GetWinRectHwnd(IntPtr h) { GetWindowRect(h, out RECT r); return r; }
    private static string Title(IntPtr h) { var sb = new System.Text.StringBuilder(256); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }

    // ---- P/Invoke ----------------------------------------------------------
    private const int WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, LowLevelKeyboardProc_Enum cb, IntPtr l);
    private delegate bool LowLevelKeyboardProc_Enum(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
