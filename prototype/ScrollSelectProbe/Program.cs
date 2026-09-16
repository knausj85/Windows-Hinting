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

        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "ssp-actuator" };
        _worker.Start();

        var sink = new HotkeySink();
        bool ok = RegisterHotKey(sink.Handle, HotkeyId, MOD_CONTROL | MOD_ALT, VK_S);
        Console.WriteLine(ok
            ? "ScrollSelectProbe ready. Focus a target app, press Ctrl+Alt+S to toggle scroll mode."
            : "!! Ctrl+Alt+S hotkey registration FAILED — use --target <hwndHex> or the 3s fallback.");
        Console.WriteLine("Keys in scroll mode: 0=window · 1-9=region · H/J=up/down · K/L=left/right · PgUp/PgDn=page · Home/End=top/bottom · [/]=debounce∓ · U=uwp-probe · Esc=exit");

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

        Application.ApplicationExit += (_, _) => { try { UnregisterHotKey(sink.Handle, HotkeyId); } catch { } try { _work.CompleteAdding(); } catch { } ExitScrollMode(); };
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
        StartVelocity(); // smooth lane ticker (drives only while the window target is active)
    }

    private static void ExitScrollMode()
    {
        _active = false;
        StopVelocity();
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
                // Detect by control type + geometry, NOT by RangeValue min<max: the range is unreliable
                // (Windows Terminal reports 0..0 despite real scrollback). A sizable on-screen ScrollBar
                // signals a scrollable area; we actuate via the wheel (RangeValue isn't trusted anyway).
                int barLong = Math.Max(w, h);
                if (barLong < 40) continue; // ignore tiny/degenerate bars
                var rv = Get<UIA.IUIAutomationRangeValuePattern>(el, UIA_RangeValuePatternId);
                double mn = rv is null ? 0 : Try(() => rv.CurrentMinimum), mx = rv is null ? 0 : Try(() => rv.CurrentMaximum);
                bool vBar = h >= w; // tall = vertical, wide = horizontal
                list.Add(new Region { Rect = r, Rv = rv, VScroll = vBar, HScroll = !vBar, PathLabel = $"ScrollBar {(vBar ? "V" : "H")} ({mn:F0}..{mx:F0})" });
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
    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;

    private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _active)
        {
            int msg = wParam.ToInt32();
            int vk = Marshal.ReadInt32(lParam);
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) { if (HandleDown(vk)) return (IntPtr)1; }
            else if (msg == WM_KEYUP || msg == WM_SYSKEYUP) { if (HandleUp(vk)) return (IntPtr)1; }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool HandleDown(int vk)
    {
        switch (vk)
        {
            case 0x1B: PostUi(ExitScrollMode); return true;                 // Esc  (mode/overlay → UI thread)
            case 0x30: PostUi(() => SetActive(-1)); return true;            // 0 → default/window
            case >= 0x31 and <= 0x39: int idx = vk - 0x31; PostUi(() => SetActive(idx)); return true; // 1-9
            case 0x48: return Dir(0, -1, vk);                              // H up
            case 0x4A: return Dir(0, +1, vk);                              // J down
            case 0x4B: return Dir(-1, 0, vk);                              // K left
            case 0x4C: return Dir(+1, 0, vk);                              // L right
            case 0x21: PostWork(() => Scroll(0, -1, true)); return true;    // PageUp  (precise, both lanes)
            case 0x22: PostWork(() => Scroll(0, +1, true)); return true;    // PageDown
            case 0x24: PostWork(() => Jump(false)); return true;           // Home → ToStart (top)
            case 0x23: PostWork(() => Jump(true)); return true;            // End  → ToEnd (bottom)
            case 0x55: PostWork(UwpWheelProbe); return true;               // U — UWP wheel-family probe
            case 0x44: PostWork(DumpScan); return true;                    // D — verbose detection dump
            case 0xDB: _debounceMs = Math.Max(0, _debounceMs - 10); Console.WriteLine($"[DBG] debounce = {_debounceMs}ms"); return true;   // [
            case 0xDD: _debounceMs = Math.Min(400, _debounceMs + 10); Console.WriteLine($"[DBG] debounce = {_debounceMs}ms"); return true; // ]
            default: return false;
        }
    }

    // Directional key DOWN. Window target (_activeIndex < 0) → SMOOTH lane: feed the velocity engine.
    // A selected region → PRECISE lane: the existing coalesced positional step.
    private static readonly HashSet<int> _dirDown = new(); // directional keys physically down (hook thread only)

    private static bool Dir(int dx, int dy, int vk)
    {
        // No axis-gate: the directional keypress is authoritative intent, honored via the wheel regardless of
        // what UIA claims (Chromium lies about the axis). Region = debounced cursor-borrow + SendInput wheel
        // (reaches WPF/UWP/Chromium); window = SendInput wheel (focus-routed).
        int ai = _activeIndex;
        if (_dirDown.Add(vk))
        {
            // NEW press → TAP: one wheel notch.
            if (ai >= 0) PostWork(() => BorrowWheelTap(dx, dy));   // region: borrow → debounce → wheel → restore
            else SendInput(1, new[] { WheelInput(dy != 0 ? -Math.Sign(dy) * TAP_WHEEL : Math.Sign(dx) * TAP_WHEEL, dx != 0) }, Marshal.SizeOf<INPUT>()); // window
        }
        else
        {
            // AUTO-REPEAT (key held past the OS repeat delay) → HOLD: engage the velocity fling
            // (window = SendInput; region = debounced-park + SendInput wheel, in the velocity loop).
            if (dy != 0) _heldY = dy;
            if (dx != 0) _heldX = dx;
        }
        return true;
    }

    // Directional key UP: end the hold (velocity decays) and clear the down-state so the next
    // press is a fresh tap. Always swallow HJKL key-up so the focused app never sees a lone key-up.
    private static bool HandleUp(int vk)
    {
        switch (vk)
        {
            case 0x48: case 0x4A: _dirDown.Remove(vk); _heldY = 0; return true;   // H / J released
            case 0x4B: case 0x4C: _dirDown.Remove(vk); _heldX = 0; return true;   // K / L released
            default: return false;
        }
    }

    // Actuation must NOT run on the low-level keyboard hook's thread. The ScrollPattern verify-polls
    // (MovedSince Thread.Sleep, ~240ms) exceed the ~300ms LowLevelHooksTimeout; Windows then delivers
    // the "swallowed" key to the focused app (Solution Explorer type-search caught 'H'). Deferring to
    // the UI message loop fixed single presses but NOT key-repeat: a queued 240ms actuation blocks the
    // loop between repeats, so the next repeat's hook callback times out and leaks. Fix: run actuation
    // on a dedicated worker thread. The hook enqueues (instant) and returns 1; COM calls marshal to the
    // main STA (sub-ms to service) while only the Sleeps live on the worker, so hook + UI stay responsive.
    // Design note for the engine: input→intent must be instant; effect execution + verify run on their
    // own execution context, never the input/UI thread.
    private static readonly System.Collections.Concurrent.BlockingCollection<Action> _work = new();
    private static Thread? _worker;
    private static void WorkerLoop()
    {
        foreach (var a in _work.GetConsumingEnumerable())
        {
            try { a(); } catch (Exception ex) { Console.WriteLine($"[DBG] worker exception: {ex.GetType().Name}: {ex.Message}"); }
        }
    }
    private static void PostUi(Action a)     // mode + overlay changes → UI thread (Forms are thread-affine)
    {
        var ov = _overlay;
        if (ov is not null && ov.IsHandleCreated) { try { ov.BeginInvoke(a); return; } catch { } }
        try { a(); } catch { }
    }
    // COM actuation → dedicated worker thread, COALESCED. Actuation (ScrollPattern verify ~240ms) is
    // far slower than key-repeat (~33ms); queuing every repeat grows an unbounded backlog that drains
    // long after the key is released (the "freeze", and "indefinite" if you keep holding). Cap to ONE
    // in-flight actuation and DROP repeats that arrive while busy. Design note for the engine: the
    // effect executor must coalesce redundant scroll intents — latest-wins, never a growing queue.
    private static int _scrollBusy;
    private static long _dropped;
    private static void PostWork(Action a)
    {
        if (Interlocked.CompareExchange(ref _scrollBusy, 1, 0) != 0)
        {
            long n = Interlocked.Increment(ref _dropped);
            if (n % 10 == 1) Console.WriteLine($"[DBG] coalesce: dropped repeat while actuator busy (total={n})");
            return;
        }
        _work.Add(() => { try { a(); } finally { Interlocked.Exchange(ref _scrollBusy, 0); } });
    }

    // ---- SMOOTH lane: velocity engine (window target, SendInput wheel) -----
    // Windows-native "fling" feel requires SendInput (the only path that feeds the compositor's
    // inertia — SendMessage bypasses the pipeline and hard-steps). SendInput has no HWND, so it
    // scrolls the FOCUS window (the target app; our overlay is NOACTIVATE) — hence smooth is
    // window-target-only, per the accepted split. A key-hold sets a velocity that ramps up, and
    // decays with friction on release; a ~60Hz high-res ticker emits sub-WHEEL_DELTA impulses with
    // fractional carry (mirrors real high-res wheels). Starting curve from docs/research/smooth-scroll-mimicry.md.
    private static volatile bool _velRunning;
    private static Thread? _velThread;
    private static volatile int _heldY, _heldX;      // -1/0/+1 current drive direction (last key wins)
    private static double _vY, _vX, _carryY, _carryX; // velocity (notches/sec) + fractional wheel remainder

    private const double VEL_MAX = 22.0;     // notches/sec at full hold
    private const double VEL_RAMP = 0.12;    // seconds to approach the target velocity (onset ramp)
    private const double VEL_DECAY16 = 0.90; // velocity retained per 16ms after release (exponential friction)
    private const double VEL_MIN = 0.6;      // below this (and no key held) → snap to 0
    private const int TAP_WHEEL = 120;       // one wheel notch for a single window tap (calibratable)

    private static void StartVelocity()
    {
        if (_velRunning) return;
        _heldY = _heldX = 0; _vY = _vX = _carryY = _carryX = 0;
        _velRunning = true;
        _velThread = new Thread(VelocityLoop) { IsBackground = true, Name = "ssp-smooth" };
        _velThread.Start();
    }
    private static void StopVelocity() { _velRunning = false; _heldY = _heldX = 0; Unpark(); try { _dirDown.Clear(); } catch { } }
    private static void ResetVelocity() { _heldY = _heldX = 0; _vY = _vX = _carryY = _carryX = 0; Unpark(); } // on target switch

    private static void VelocityLoop()
    {
        try { timeBeginPeriod(1); } catch { }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double last = sw.Elapsed.TotalSeconds;
        try
        {
            while (_velRunning)
            {
                double t = sw.Elapsed.TotalSeconds, dt = t - last; last = t;
                if (dt <= 0) dt = 1.0 / 60; else if (dt > 0.1) dt = 0.1;

                _vY = Step(_vY, _heldY, dt);
                _vX = Step(_vX, _heldX, dt);

                int ai = _activeIndex;
                bool parkActive = false;
                if (ai < 0)
                {
                    // WINDOW: continuous sub-notch wheel via SendInput (real pipeline → app inertia = glide).
                    EmitInputWheel(dt);
                }
                else if (ai < _regions.Count)
                {
                    // REGION (DEBOUNCED PARK): borrow the cursor into the region ONCE, wait _debounceMs for the
                    // hover-enter to register, then drive SendInput wheel (real pipeline → inertia) for the hold;
                    // restore the cursor once the fling is spent. SendInput reaches WPF/UWP/Chromium alike.
                    parkActive = _heldY != 0 || _heldX != 0 || Math.Abs(_vY) > 0.001 || Math.Abs(_vX) > 0.001;
                    if (parkActive)
                    {
                        EnsureParked(_regions[ai]);
                        if (Environment.TickCount64 - _parkAt >= _debounceMs) EmitInputWheel(dt);
                    }
                }
                if (_parked && !parkActive) Unpark(); // fling spent / target changed → restore cursor once

                Thread.Sleep(15);
            }
        }
        catch (Exception ex) { Console.WriteLine($"[DBG] velocity loop: {ex.Message}"); }
        finally { try { timeEndPeriod(1); } catch { } }
    }

    private static void EmitInputWheel(double dt)
    {
        _carryY += _vY * dt * 120.0;   // notches/sec → wheel units/frame
        _carryX += _vX * dt * 120.0;
        int dY = (int)_carryY; _carryY -= dY;
        int dX = (int)_carryX; _carryX -= dX;
        if (dY != 0) SendInput(1, new[] { WheelInput(-dY, false) }, Marshal.SizeOf<INPUT>()); // wheel-down = negative
        if (dX != 0) SendInput(1, new[] { WheelInput(dX, true) }, Marshal.SizeOf<INPUT>());
    }

    // Debounced park: borrow the cursor into the region (pane center, or content-beside-bar for a scrollbar)
    // via a real-pipeline move so a hover-enter is generated, and record the time. Callers hold wheels off
    // until _debounceMs has elapsed, so the wheel lands after the target knows the cursor is over it.
    private static volatile int _debounceMs = 40; // tunable live with [ and ] (VMs want more)
    private static POINT _parkOrig;
    private static volatile bool _parked;
    private static long _parkAt;
    private static void EnsureParked(Region r)
    {
        if (_parked) return;
        RECT t = r.Sp is not null ? r.Rect : ContentBesideBar(r.Rect);
        GetCursorPos(out _parkOrig);
        SendInput(1, new[] { AbsMoveInput((t.left + t.right) / 2, (t.top + t.bottom) / 2) }, Marshal.SizeOf<INPUT>());
        _parkAt = Environment.TickCount64;
        _parked = true;
    }

    // Region TAP: borrow the cursor → debounce → one wheel notch → debounce → restore. All on the worker
    // thread, so the Sleeps never block the hook. Honors the requested direction regardless of UIA axis.
    private static void BorrowWheelTap(int dx, int dy)
    {
        int ai = _activeIndex;
        if (ai < 0 || ai >= _regions.Count) return;
        var r = _regions[ai];
        RECT t = r.Sp is not null ? r.Rect : ContentBesideBar(r.Rect);
        GetCursorPos(out POINT orig);
        int tx = (t.left + t.right) / 2, ty = (t.top + t.bottom) / 2;
        try
        {
            SendInput(1, new[] { AbsMoveInput(tx, ty) }, Marshal.SizeOf<INPUT>());
            Thread.Sleep(_debounceMs);
            GetCursorPos(out POINT chk); // did the park move actually stick to the region point?
            Console.WriteLine($"[DBG] borrow: target=({tx},{ty}) cursor-after-move=({chk.x},{chk.y}) match={(Math.Abs(chk.x - tx) < 3 && Math.Abs(chk.y - ty) < 3)}");
            SendInput(1, new[] { WheelInput(dy != 0 ? -Math.Sign(dy) * TAP_WHEEL : Math.Sign(dx) * TAP_WHEEL, dx != 0) }, Marshal.SizeOf<INPUT>());
            Thread.Sleep(_debounceMs);
        }
        finally { SendInput(1, new[] { AbsMoveInput(orig.x, orig.y) }, Marshal.SizeOf<INPUT>()); }
    }
    private static void Unpark()
    {
        if (!_parked) return;
        _parked = false;
        try { SendInput(1, new[] { AbsMoveInput(_parkOrig.x, _parkOrig.y) }, Marshal.SizeOf<INPUT>()); } catch { }
    }

    private static double Step(double v, int held, double dt)
    {
        if (held != 0) v += (held * VEL_MAX - v) * Math.Min(1.0, dt / VEL_RAMP); // ramp toward target
        else { v *= Math.Pow(VEL_DECAY16, dt / 0.016); if (Math.Abs(v) < VEL_MIN) v = 0; } // friction decay
        return v;
    }

    private static INPUT MouseInput(MOUSEINPUT mi) { var i = new INPUT { type = INPUT_MOUSE }; i.U.mi = mi; return i; }
    private static INPUT WheelInput(int delta, bool horizontal) =>
        MouseInput(new MOUSEINPUT { mouseData = delta, dwFlags = horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL });
    private static INPUT AbsMoveInput(int x, int y)
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN), vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN), vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        int ax = (int)Math.Round((x - vx) * 65535.0 / Math.Max(1, vw - 1));
        int ay = (int)Math.Round((y - vy) * 65535.0 / Math.Max(1, vh - 1));
        return MouseInput(new MOUSEINPUT { dx = ax, dy = ay, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK });
    }

    // ALWAYS-WHEEL experiment: one cadenced SendMessage wheel at the region content point — pane center
    // for a ScrollPattern region, content-beside-bar for a scrollbar region. No UIA increments.
    private static void RegionStep(int dx, int dy)
    {
        int ai = _activeIndex;
        if (ai < 0 || ai >= _regions.Count) return;
        var r = _regions[ai];
        WheelFallback(r.Sp is not null ? r.Rect : ContentBesideBar(r.Rect), dx, dy, false);
    }

    // Definitively test whether ANY wheel-family message scrolls a UWP surface. Logs the window
    // hierarchy at the target point, finds the hosted CoreWindow, and blasts WM_MOUSEWHEEL and
    // WM_POINTERWHEEL (down) at the point-hwnd, the CoreWindow, and the top window. Press 'U' in
    // Settings and watch whether the content moves on any of them.
    private static void UwpWheelProbe()
    {
        int cx, cy, ai = _activeIndex;
        if (ai >= 0 && ai < _regions.Count) { var rr = _regions[ai].Rect; cx = (rr.left + rr.right) / 2; cy = (rr.top + rr.bottom) / 2; }
        else { RECT wr = GetWinRectHwnd(_targetHwnd); cx = (wr.left + wr.right) / 2; cy = (wr.top + wr.bottom) / 2; }

        IntPtr hit = WindowFromPoint(new POINT { x = cx, y = cy });
        Console.WriteLine($"[UWP] === probe at ({cx},{cy}) — target=0x{_targetHwnd.ToInt64():X} '{ClassOf(_targetHwnd)}' ===");
        Console.WriteLine($"[UWP] WindowFromPoint = 0x{hit.ToInt64():X} '{ClassOf(hit)}'");
        for (IntPtr a = hit, i = IntPtr.Zero; a != IntPtr.Zero && i.ToInt32() < 6; i = (IntPtr)(i.ToInt32() + 1), a = GetParent(a))
            Console.WriteLine($"[UWP]   ancestor 0x{a.ToInt64():X} '{ClassOf(a)}'");
        IntPtr core = FindDescendantByClass(_targetHwnd, "Windows.UI.Core.CoreWindow");
        Console.WriteLine($"[UWP] CoreWindow = 0x{core.ToInt64():X}");

        IntPtr lp = (IntPtr)((cy << 16) | (cx & 0xFFFF));
        int delta = -120;
        foreach (var (name, h) in new (string, IntPtr)[] { ("point", hit), ("core", core), ("top", _targetHwnd) })
        {
            if (h == IntPtr.Zero) { Console.WriteLine($"[UWP] {name}: (null)"); continue; }
            IntPtr mw = SendMessage(h, WM_MOUSEWHEEL, (IntPtr)(delta << 16), lp);
            IntPtr pw = SendMessage(h, WM_POINTERWHEEL, (IntPtr)((delta << 16) | 1), lp); // HIWORD=delta, LOWORD=pointerId
            Console.WriteLine($"[UWP] {name} 0x{h.ToInt64():X} '{ClassOf(h)}': WM_MOUSEWHEEL→{mw.ToInt64()}  WM_POINTERWHEEL→{pw.ToInt64()}");
        }
        Console.WriteLine($"[UWP] === did the content move on ANY line above? ===");
    }

    // Verbose scan dump: shows exactly what our OrCondition FindAll surfaces on the target window's
    // subtree and each visible child-HWND fragment — to see whether Terminal's ScrollBar is missed by
    // the tree walk (a fragment boundary) or found-but-filtered (e.g. RangeValue min==max).
    private static void DumpScan()
    {
        IntPtr hwnd = _targetHwnd;
        Console.WriteLine($"[SCAN] target 0x{hwnd.ToInt64():X} '{ClassOf(hwnd)}' '{Title(hwnd)}'");
        UIA.IUIAutomationElement winEl;
        try { winEl = _com.ElementFromHandle(hwnd); } catch (Exception ex) { Console.WriteLine($"[SCAN] ElementFromHandle failed: {ex.Message}"); return; }
        DumpFindAll("window-subtree", winEl);
        try
        {
            EnumChildWindows(hwnd, (h, _) =>
            {
                if (!IsWindowVisible(h) || !GetWindowRect(h, out RECT r)) return true;
                int w = r.right - r.left, ht = r.bottom - r.top;
                Console.WriteLine($"[SCAN] child 0x{h.ToInt64():X} '{ClassOf(h)}' {w}x{ht}");
                if (w < 40 || ht < 40) return true;
                UIA.IUIAutomationElement el; try { el = _com.ElementFromHandle(h); } catch { return true; }
                if (el is not null) DumpFindAll($"  child-0x{h.ToInt64():X}", el);
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) { Console.WriteLine($"[SCAN] child enum threw: {ex.Message}"); }
        Console.WriteLine($"[SCAN] === end ===");
    }

    private static void DumpFindAll(string label, UIA.IUIAutomationElement root)
    {
        UIA.IUIAutomationCondition cond = _com.CreateOrCondition(
            _com.CreatePropertyCondition(UIA_IsScrollPatternAvailablePropertyId, true),
            _com.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_ScrollBarControlTypeId));
        UIA.IUIAutomationElementArray found;
        try { found = root.FindAll(UIA.TreeScope.TreeScope_Subtree, cond); }
        catch (Exception ex) { Console.WriteLine($"[SCAN] {label}: FindAll threw {ex.Message}"); return; }
        int n = found?.Length ?? 0;
        Console.WriteLine($"[SCAN] {label}: {n} scroll-candidate(s)");
        for (int i = 0; i < n; i++)
        {
            UIA.IUIAutomationElement el; try { el = found!.GetElement(i); } catch { continue; }
            int ctl = 0; try { ctl = el.CurrentControlType; } catch { }
            RECT r = GetRect(el);
            string nm = ""; try { nm = el.CurrentName ?? ""; } catch { }
            var rv = Get<UIA.IUIAutomationRangeValuePattern>(el, UIA_RangeValuePatternId);
            var sp = Get<UIA.IUIAutomationScrollPattern>(el, UIA_ScrollPatternId);
            string rvs = rv is null ? "" : $" RV[{Try(() => rv.CurrentMinimum):F0}..{Try(() => rv.CurrentMaximum):F0}]";
            string sps = sp is null ? "" : $" SP(v={GetDbl(el, UIA_ScrollVerticalViewSizePropertyId):F0} h={GetDbl(el, UIA_ScrollHorizontalViewSizePropertyId):F0})";
            Console.WriteLine($"[SCAN]   [{i}] {ControlTypeName(ctl)} '{nm}' ({r.left},{r.top} {r.right - r.left}x{r.bottom - r.top}){rvs}{sps}");
        }
    }

    private static string ClassOf(IntPtr h)
    { var sb = new System.Text.StringBuilder(128); try { GetClassName(h, sb, sb.Capacity); } catch { } return sb.ToString(); }

    private static IntPtr FindDescendantByClass(IntPtr parent, string cls)
    {
        IntPtr found = IntPtr.Zero;
        try { EnumChildWindows(parent, (h, _) => { if (ClassOf(h) == cls) { found = h; return false; } return true; }, IntPtr.Zero); } catch { }
        return found;
    }

    private static void SendWheel(int delta, bool horizontal)
    {
        int ai = _activeIndex;
        if (ai >= 0 && ai < _regions.Count)
        {
            // Region smooth = POINT-ADDRESSED SendMessage wheel at the content (pane center, or the
            // content beside a scrollbar) — NO cursor move. The atomic-warp (SendInput move→wheel→
            // restore) was ABANDONED: warping the cursor ~120x/sec leaked hover/selection events into
            // neighbouring panes (Solution Explorer highlighting) and mis-routed the wheel (editor
            // "stuck until you move the mouse"). SendMessage can't trigger app inertia, so region holds
            // are smooth cadenced stepping, not an inertial fling — this IS the accepted split.
            var r = _regions[ai];
            RECT tgt = r.Sp is not null ? r.Rect : ContentBesideBar(r.Rect);
            int cx = (tgt.left + tgt.right) / 2, cy = (tgt.top + tgt.bottom) / 2;
            IntPtr h = WindowFromPoint(new POINT { x = cx, y = cy });
            if (h == IntPtr.Zero) h = _targetHwnd;
            IntPtr lp = (IntPtr)((cy << 16) | (cx & 0xFFFF));
            SendMessage(h, horizontal ? WM_MOUSEHWHEEL : WM_MOUSEWHEEL, (IntPtr)(delta << 16), lp);
        }
        else
        {
            // Window smooth = SendInput real-pipeline wheel → engages the app's own inertia (the glide).
            SendInput(1, new[] { WheelInput(delta, horizontal) }, Marshal.SizeOf<INPUT>());
        }
    }

    private static void SetActive(int idx)
    {
        _activeIndex = (idx >= 0 && idx < _regions.Count) ? idx : -1;
        ResetVelocity(); // stop any in-flight smooth fling when the target changes
        _overlay?.SetActive(_activeIndex);
        Console.WriteLine($"   active target = {(_activeIndex < 0 ? "WINDOW (default/fallback wheel)" : $"region [{_regions[_activeIndex].Number}] — {_regions[_activeIndex].PathLabel}")}");
    }

    // ---- actuation ---------------------------------------------------------
    // dx: -1 left / +1 right; dy: -1 up / +1 down; page: large step.
    private static void Scroll(int dx, int dy, bool page)
    {
        try
        {
            Console.WriteLine($"[DBG] key dx={dx} dy={dy} page={page} target={(_activeIndex < 0 ? "WINDOW" : $"region[{_regions[_activeIndex].Number}] {_regions[_activeIndex].PathLabel}")}");
            if (_activeIndex >= 0)
            {
                var r = _regions[_activeIndex];
                // ALWAYS-WHEEL experiment: region taps/pages actuate via the wheel at the content point too
                // (pane center, or content-beside-bar for a scrollbar) — no UIA ScrollPattern/RangeValue.
                WheelFallback(r.Sp is not null ? r.Rect : ContentBesideBar(r.Rect), dx, dy, page);
                return;
            }
            // default (window) OR a picked region with no usable pattern → injected wheel
            RECT wr = (_activeIndex >= 0) ? _regions[_activeIndex].Rect : GetWinRectHwnd(_targetHwnd);
            WheelFallback(wr, dx, dy, page);
        }
        catch (Exception ex) { Console.WriteLine($"   !! scroll threw: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void ScrollViaPattern(Region r, int dx, int dy, bool page)
    {
        char axis = (dy != 0 && r.VScroll) ? 'V' : (dx != 0 && r.HScroll) ? 'H' : '\0';
        if (axis == '\0')
        {
            // Requested axis isn't scrollable on this region — NO-OP. (Previously this fell back to
            // a wheel in the unsupported axis — a horizontal wheel on a V-only Tree — which scrolled
            // the wrong thing. Bug found in VS.)
            Console.WriteLine($"[DBG] pattern: axis unsupported (dx={dx} dy={dy} V={r.VScroll} H={r.HScroll}) -> no-op");
            return;
        }
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
        // Don't escalate to a wheel when already at the scroll limit in this direction: the
        // "no move" is expected (nothing left to scroll), and a wheel here bubbles to a PARENT
        // scrollable and scrolls the wrong pane. Bug found in VS at the top of a Tree. Only guard
        // when the percent is readable — an unreadable pane still needs the wheel fallback.
        double now = ReadPct(r.Sp!, axis);
        bool atLimit = now > NoScroll && ((sign < 0 && now <= 0.01) || (sign > 0 && now >= 99.99));
        if (atLimit) { Console.WriteLine($"[DBG] pattern: at limit (pct={now:F1} sign={sign}) -> no wheel escalation"); return; }
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
        // axis gate: a vertical scrollbar ignores horizontal keys and vice-versa. Bug found in VS —
        // a horizontal key on a V-only bar was actuating / sending a horizontal wheel.
        bool wantV = dy != 0, wantH = dx != 0;
        if ((wantV && !r.VScroll) || (wantH && !r.HScroll))
        {
            Console.WriteLine($"[DBG] range: axis unsupported (dx={dx} dy={dy} V={r.VScroll} H={r.HScroll}) -> no-op");
            return;
        }
        double mn = Try(() => r.Rv!.CurrentMinimum), mx = Try(() => r.Rv!.CurrentMaximum), cur = Try(() => r.Rv!.CurrentValue);
        if (double.IsNaN(mn) || double.IsNaN(mx) || !(mx > mn)) { WheelFallback(r.Rect, dx, dy, page); return; }
        double step = (mx - mn) * (page ? 0.20 : 0.06);
        int sign = (dy != 0) ? dy : dx;              // up/left decrease, down/right increase
        double next = Math.Max(mn, Math.Min(mx, cur + sign * step));
        try
        {
            r.Rv!.SetValue(next);
            double after = Try(() => r.Rv!.CurrentValue);
            bool asked = Math.Abs(next - cur) > 0.001;   // did we actually request a move (not at a boundary)?
            bool moved = Math.Abs(after - cur) > 0.001;
            Console.WriteLine($"[DBG] range {(page ? "PAGE" : "line")}: min={mn:F1} max={mx:F1} cur={cur:F2} step={step:F2} -> next={next:F2}, readback after={after:F2} asked={asked} moved={moved}");
            // VERIFY-AND-ESCALATE (Phase-3 fix): if we asked the value to move but it snapped
            // back / didn't take (Beyond Compare's write-ignored scrollbar), the RangeValue is
            // non-functional — escalate to the injected wheel. Aim it at the CONTENT beside the
            // bar, since the bar's own center is the wrong target for a wheel message.
            if (asked && !moved)
            {
                RECT content = ContentBesideBar(r.Rect);
                Console.WriteLine($"[DBG] range no-move -> ESCALATE wheel at content-beside-bar ({(content.left + content.right) / 2},{(content.top + content.bottom) / 2})");
                WheelFallback(content, dx, dy, page);
            }
        }
        catch (Exception ex) { Console.WriteLine($"[DBG] range SetValue THREW {ex.Message} -> wheel"); WheelFallback(r.Rect, dx, dy, page); }
    }

    // ---- absolute jump (Home/End = ToStart/ToEnd) — ticket #65 Q3a validation --
    // Home → top (percent 0), End → bottom (percent 100), vertical.
    //  • Selected region: SetScrollPercent on its ScrollPattern, else SetValue min/max
    //    on its ScrollBar RangeValue.
    //  • WINDOW target (default, _activeIndex < 0): the PROPOSED behavior under test —
    //    just-in-time resolve the UIA element under the window's scroll point (the SAME
    //    center point the wheel fallback aims at) and SetScrollPercent on it if it exposes
    //    a ScrollPattern. If not (pure-wheel apps — Monaco/Slack), a documented no-op.
    private static void Jump(bool toEnd)
    {
        double pct = toEnd ? 100.0 : 0.0;
        string k = toEnd ? "End" : "Home";
        try
        {
            if (_activeIndex >= 0)
            {
                var r = _regions[_activeIndex];
                if (r.Sp is not null)
                {
                    try { r.Sp.SetScrollPercent(NoScroll, pct); Console.WriteLine($"   {k}: region[{r.Number}] SetScrollPercent V={pct}"); }
                    catch (Exception ex) { Console.WriteLine($"   !! {k} region SetScrollPercent threw: {ex.Message}"); }
                    return;
                }
                if (r.Rv is not null)
                {
                    double mn = Try(() => r.Rv!.CurrentMinimum), mx = Try(() => r.Rv!.CurrentMaximum);
                    if (!double.IsNaN(mn) && !double.IsNaN(mx) && mx > mn)
                    {
                        try { r.Rv!.SetValue(toEnd ? mx : mn); Console.WriteLine($"   {k}: region[{r.Number}] RangeValue SetValue={(toEnd ? mx : mn):F0}"); }
                        catch (Exception ex) { Console.WriteLine($"   !! {k} region SetValue threw: {ex.Message}"); }
                    }
                    else Console.WriteLine($"   {k}: region[{r.Number}] RangeValue has no range — no-op");
                    return;
                }
                Console.WriteLine($"   {k}: region[{r.Number}] has no Sp/Rv handle — no-op");
                return;
            }

            // WINDOW target — the behavior under validation.
            RECT wr = GetWinRectHwnd(_targetHwnd);
            int cx = (wr.left + wr.right) / 2, cy = (wr.top + wr.bottom) / 2;
            UIA.IUIAutomationElement? el = null;
            try { el = _com.ElementFromPoint(new UIA.tagPOINT { x = cx, y = cy }); } catch { }
            var sp = el is null ? null : Get<UIA.IUIAutomationScrollPattern>(el, UIA_ScrollPatternId);
            if (sp is not null)
            {
                int ct = 0; try { ct = el!.CurrentControlType; } catch { }
                bool vScrollable = el is not null && GetBool(el, UIA_ScrollVerticallyScrollablePropertyId);
                try
                {
                    sp.SetScrollPercent(NoScroll, pct);
                    Console.WriteLine($"   {k}: WINDOW → resolved '{ControlTypeName(ct)}' under center ({cx},{cy}), SetScrollPercent V={pct} (vScrollable={vScrollable})");
                }
                catch (Exception ex) { Console.WriteLine($"   !! {k} WINDOW SetScrollPercent threw: {ex.Message}"); }
            }
            else
            {
                Console.WriteLine($"   {k}: WINDOW → element under center ({cx},{cy}) exposes no ScrollPattern → documented no-op (pure-wheel app)");
            }
        }
        catch (Exception ex) { Console.WriteLine($"   !! {k} threw: {ex.GetType().Name}: {ex.Message}"); }
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
            IntPtr res = SendMessage(h, WM_MOUSEWHEEL, (IntPtr)(delta << 16), lp);
            Console.WriteLine($"[DBG] wheel V hwnd=0x{h.ToInt64():X} at ({cx},{cy}) delta={delta} notches={notches} sendResult={res}");
        }
        else if (dx != 0)
        {
            int delta = (dx < 0 ? -1 : 1) * 120 * notches;          // hwheel + = right
            IntPtr res = SendMessage(h, WM_MOUSEHWHEEL, (IntPtr)(delta << 16), lp);
            Console.WriteLine($"[DBG] wheel H hwnd=0x{h.ToInt64():X} at ({cx},{cy}) delta={delta} notches={notches} sendResult={res}");
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
            string legend = $"SCROLL MODE   active={( _act < 0 ? "WINDOW (default/wheel)" : $"[{_rs[_act].Number}] {_rs[_act].PathLabel}")}   |   0=window  1-9=region  H/J=up/down  K/L=left/right  PgUp/PgDn=page  Home/End=top/bottom  Esc=exit";
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

    // For wheel-escalation off a bare scrollbar: the bar's own center is the wrong wheel
    // target (it IS the bar, not the content). A vertical bar (taller than wide) has its
    // content pane to the LEFT; a horizontal bar has it ABOVE. Return a thin rect centered
    // ~40px into that content, so WheelFallback's rect-center lands on the scrolled pane.
    private static RECT ContentBesideBar(RECT bar)
    {
        int w = bar.right - bar.left, h = bar.bottom - bar.top;
        if (h >= w) { int x = bar.left - 40; return new RECT { left = x - 2, top = bar.top, right = x + 2, bottom = bar.bottom }; }
        int y = bar.top - 40; return new RECT { left = bar.left, top = y - 2, right = bar.right, bottom = y + 2 };
    }

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
    private const int WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E, WM_POINTERWHEEL = 0x024E;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }

    // SendInput (smooth lane — real input pipeline → engages app inertia) + high-res timer
    private const uint INPUT_MOUSE = 0, MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000;
    private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public int mouseData; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);
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
