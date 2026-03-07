using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using UIAutomationClient;

namespace HintOverlay
{
    internal sealed class HintController : IDisposable
    {
        private const int WM_INPUT = 0x00FF;
        private const int RIM_TYPEKEYBOARD = 1;
        private const int RID_INPUT = 0x10000003;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public int dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public int dwType;
            public int dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct RAWINPUT
        {
            [FieldOffset(0)] public RAWINPUTHEADER header;
            [FieldOffset(16)] public RAWKEYBOARD keyboard;
        }

        public OverlayForm Overlay { get; }

        private readonly IUIAutomation _uia = new CUIAutomation();
        private bool _enabled;
        private readonly object _gate = new();

        private List<HintItem> _currentHints = new();
        private string _typed = "";

        // pressed key set to suppress auto-repeat
        private readonly HashSet<int> _pressedKeys = new();

        private long _lastToggleTicks = 0;
        private IntPtr _kbHook = IntPtr.Zero;
        private LowLevelKeyboardProc? _kbProc;

        private NotifyIcon? _trayIcon;
        private Preferences _preferences;

        public HintController()
        {
            _preferences = Preferences.Load();

            Overlay = new OverlayForm();
            Overlay.ShowRectangles = _preferences.ShowRectangles;
            Overlay.Show();
            Overlay.RegisterGlobalHotkey(_preferences.HotkeyModifiers, _preferences.HotkeyVirtualKey);
            Overlay.ToggleRequested += (_, __) => Toggle();

            InitializeTrayIcon();
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "HintOverlay",
                Visible = true
            };

            // Create a simple icon (you can replace this with a custom icon file)
            _trayIcon.Icon = CreateTrayIcon();

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Preferences...", null, OnPreferences);
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, OnExit);

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (_, __) => Toggle();
        }

        private Icon CreateTrayIcon()
        {
            // Create a simple 16x16 icon with a yellow 'H' on transparent background
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var font = new Font("Segoe UI", 10, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.Yellow))
                {
                    g.DrawString("H", font, brush, -2, -2);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private void OnPreferences(object? sender, EventArgs e)
        {
            var dialog = new PreferencesDialog(_preferences);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                // Reload preferences and apply
                _preferences = Preferences.Load();
                Overlay.ShowRectangles = _preferences.ShowRectangles;
                Overlay.RegisterGlobalHotkey(_preferences.HotkeyModifiers, _preferences.HotkeyVirtualKey);
                Overlay.Invalidate();
            }
        }

        private void OnExit(object? sender, EventArgs e)
        {
            Application.Exit();
        }

        static void Measure(string name, Action func)
        {
            var sw = Stopwatch.StartNew();
            func();
            sw.Stop();
            Debug.WriteLine($"{name}: {sw.Elapsed.TotalMilliseconds:F3} ms");
        }

        public void Toggle()
        {
            long now = Stopwatch.GetTimestamp();

            if (now - _lastToggleTicks < Stopwatch.Frequency / 5) // 200ms
                return;

            _lastToggleTicks = now;

            lock (_gate)
            {
                _enabled = !_enabled;
                Debug.WriteLine(string.Format("Toggle {0}", _enabled));

                if (_enabled)
                {
                    _typed = "";
                    Overlay.SetFilterPrefix(""); 
                    Measure("UIA", Refresh);

                    foreach (var h in _currentHints)
                    {
                        h.TargetOpacity = 1.0f;
                        h.CurrentOpacity = 1.0f;
                    }
                    Overlay.SetHints(_currentHints);

                    InstallKeyboardHook();
                }
                else
                {
                    _typed = "";
                    Overlay.SetFilterPrefix(""); 
                    Overlay.SetHints(new List<HintItem>());
                    RemoveKeyboardHook();
                    _pressedKeys.Clear();
                }

                Overlay.SetEnabled(_enabled);
            }
        }

        private void Refresh()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            var root = _uia.ElementFromHandle(hwnd);

            var clickableControlTypes = new int[]
            {
               UIA_ControlTypeIds.UIA_ButtonControlTypeId,
               UIA_ControlTypeIds.UIA_CheckBoxControlTypeId,
               UIA_ControlTypeIds.UIA_ComboBoxControlTypeId,
               UIA_ControlTypeIds.UIA_EditControlTypeId,
               UIA_ControlTypeIds.UIA_HyperlinkControlTypeId,
               UIA_ControlTypeIds.UIA_ListItemControlTypeId,
               UIA_ControlTypeIds.UIA_MenuControlTypeId,
               UIA_ControlTypeIds.UIA_MenuItemControlTypeId,
               UIA_ControlTypeIds.UIA_RadioButtonControlTypeId,
               UIA_ControlTypeIds.UIA_TabItemControlTypeId,
               UIA_ControlTypeIds.UIA_TreeItemControlTypeId,
               UIA_ControlTypeIds.UIA_SplitButtonControlTypeId,
            };

            var statusAndConditionList = new List<IUIAutomationCondition>()
            {
               _uia.CreatePropertyCondition(UIA_PropertyIds.UIA_IsEnabledPropertyId, true),
               _uia.CreatePropertyCondition(UIA_PropertyIds.UIA_IsOffscreenPropertyId, false),
            };

            var statusAndCondition = _uia.CreateAndConditionFromArray(statusAndConditionList.ToArray());

            var controlTypeConditionList = clickableControlTypes
                .Select(t => _uia.CreatePropertyCondition(UIA_PropertyIds.UIA_ControlTypePropertyId, t))
                .ToArray();

            var controlTypeOrCondition =
                _uia.CreateOrConditionFromArray(controlTypeConditionList);

            var combinedStatusAndTypeCondition = _uia.CreateAndCondition(statusAndCondition, controlTypeOrCondition);

            var cache = _uia.CreateCacheRequest();
            cache.TreeScope = TreeScope.TreeScope_Element;
            cache.AddProperty(UIA_PropertyIds.UIA_BoundingRectanglePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_ControlTypePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_IsTogglePatternAvailablePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_IsInvokePatternAvailablePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_IsExpandCollapsePatternAvailablePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_IsSelectionItemPatternAvailablePropertyId);
            cache.AddPattern(UIA_PatternIds.UIA_InvokePatternId);
            cache.AddPattern(UIA_PatternIds.UIA_ExpandCollapsePatternId);
            cache.AddPattern(UIA_PatternIds.UIA_SelectionItemPatternId);
            cache.AddPattern(UIA_PatternIds.UIA_TogglePatternId);

            var elems = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, combinedStatusAndTypeCondition, cache);

            var list = new List<HintItem>();

            for (int i = 0; i < elems.Length; i++)
            {
                var e = elems.GetElement(i);
                tagRECT rect = e.CachedBoundingRectangle;
                bool shouldProcess = clickableControlTypes.Contains(e.CachedControlType);

                if (shouldProcess && (rect.right > rect.left && rect.bottom > rect.top))
                {
                    list.Add(new HintItem
                    {
                        Rect = new Rectangle(
                        (int)rect.left,
                        (int)rect.top,
                        (int)(rect.right - rect.left),
                        (int)(rect.bottom - rect.top)),
                        Element = e
                    });
                }
            }

            var labels = LabelGenerator.Generate(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                list[i] = new HintItem
                {
                    Rect = list[i].Rect,
                    Element = list[i].Element,
                    Label = labels[i],
                    CurrentOpacity = 1.0f,
                    TargetOpacity = 1.0f
                };
            }

            _currentHints = list;
            Overlay.SetHints(list);
        }

        // ================= Keyboard Hook =================

        private void InstallKeyboardHook()
        {
            if (_kbHook != IntPtr.Zero) return;

            _kbProc = HookCallback;
            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, GetModuleHandle(null), 0);
        }

        private void RemoveKeyboardHook()
        {
            if (_kbHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_kbHook);
                _kbHook = IntPtr.Zero;
            }
        }

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _enabled)
            {
                // read vk code (first field of KBDLLHOOKSTRUCT)
                int vkCode = Marshal.ReadInt32(lParam);

                bool isKeyDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
                bool isKeyUp = (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP);

                // handle key releases: clear pressed set and return
                if (isKeyUp)
                {
                    _pressedKeys.Remove(vkCode);
                    // always pass along key-up events
                    return CallNextHookEx(_kbHook, nCode, wParam, lParam);
                }

                // ignore repeats: if already pressed, skip processing but pass through
                if (!isKeyDown) // only process keydown messages here
                    return CallNextHookEx(_kbHook, nCode, wParam, lParam);

                if (_pressedKeys.Contains(vkCode))
                {
                    // it's an auto-repeat; don't process but let other hooks/apps see it
                    return CallNextHookEx(_kbHook, nCode, wParam, lParam);
                }

                // mark as pressed to suppress further repeats until key up
                _pressedKeys.Add(vkCode);

                // don't intercept hotkey - check against current preferences
                bool ctrlDown = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool altDown = (GetAsyncKeyState(0x12) & 0x8000) != 0;
                bool shiftDown = (GetAsyncKeyState(0x10) & 0x8000) != 0;
                
                const int MOD_CONTROL = 0x0002;
                const int MOD_ALT = 0x0001;
                const int MOD_SHIFT = 0x0004;

                bool hotkeyCtrl = (_preferences.HotkeyModifiers & MOD_CONTROL) != 0;
                bool hotkeyAlt = (_preferences.HotkeyModifiers & MOD_ALT) != 0;
                bool hotkeyShift = (_preferences.HotkeyModifiers & MOD_SHIFT) != 0;

                if (vkCode == _preferences.HotkeyVirtualKey &&
                    ctrlDown == hotkeyCtrl &&
                    altDown == hotkeyAlt &&
                    shiftDown == hotkeyShift)
                {
                    return CallNextHookEx(_kbHook, nCode, wParam, lParam);
                }

                // ignore when Alt or Ctrl are held (except hotkey above)
                if (altDown || ctrlDown)
                    return CallNextHookEx(_kbHook, nCode, wParam, lParam);

                // process application keys when hinting enabled
                if (vkCode >= 0x41 && vkCode <= 0x5A) // A-Z
                {
                    var candidate = _typed + ((char)vkCode).ToString();

                    bool anyMatch = _currentHints.Any(h =>
                           h.Label.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));

                    if (!anyMatch)
                    {
                        System.Media.SystemSounds.Beep.Play(); // invalid input sound
                        return (IntPtr)1; // consume but do not change state
                    }

                    _typed = candidate;
                    UpdateMatchesAndAnimate();
                    return (IntPtr)1; // consume ONLY when hinting is enabled
                }
                if (vkCode == 0x08) // backspace
                {
                    if (_typed.Length > 0)
                        _typed = _typed[..^1];

                    UpdateMatchesAndAnimate();
                    return (IntPtr)1;
                }
                if (vkCode == 0x1B) // escape clears typed buffer (keeps hints up)
                {
                    _typed = "";
                    UpdateMatchesAndAnimate();
                    return (IntPtr)1;
                }
                if (vkCode == 0x20 || vkCode == 0x0D) // SPACE or ENTER commits selection
                {
                    CommitSelection();
                    return (IntPtr)1;
                }
            }

            return CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private void UpdateMatchesAndAnimate()
        {
            // Update overlay prefix highlight
            Overlay.SetFilterPrefix(_typed);

            // Animate fade: non-matching hints fade to 50%
            foreach (var h in _currentHints)
            {
                bool match = string.IsNullOrEmpty(_typed) ||
                             h.Label.StartsWith(_typed, StringComparison.OrdinalIgnoreCase);

                h.TargetOpacity = match ? 1.0f : 0.0f;
            }

            // Kick animation + repaint
            Overlay.SetHints(_currentHints);
        }

        private void CommitSelection()
        {
            if (string.IsNullOrEmpty(_typed))
                return;

            foreach (var h in _currentHints)
            {
                if (!h.Label.Equals(_typed, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var el = h.Element;

                    // Prefer Invoke, then Expand/Collapse, then SelectionItem, then Toggle.
                    if (el.GetCachedPattern(UIA_PatternIds.UIA_InvokePatternId) is IUIAutomationInvokePattern invokePattern)
                    {
                        invokePattern.Invoke();
                    }
                    else if (el.GetCachedPattern(UIA_PatternIds.UIA_ExpandCollapsePatternId) is IUIAutomationExpandCollapsePattern expandPattern)
                    {
                        expandPattern.Expand();
                    }
                    else if (el.GetCachedPattern(UIA_PatternIds.UIA_SelectionItemPatternId) is IUIAutomationSelectionItemPattern selectionPattern)
                    {
                        selectionPattern.Select();
                    }
                    else if (el.GetCachedPattern(UIA_PatternIds.UIA_TogglePatternId) is IUIAutomationTogglePattern togglePattern)
                    {
                        togglePattern.Toggle();
                    }
                }
                catch { }

                // hide hints after commit
                Toggle();
                return;
            }
        }

        public void Dispose()
        {
            RemoveKeyboardHook();
            _trayIcon?.Dispose();
        }

        // ============== Win32 keyboard hook ==============

        private const int WH_KEYBOARD_LL = 13;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
            IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool RegisterRawInputDevices(
            RAWINPUTDEVICE[] pRawInputDevices,
            uint uiNumDevices,
            uint cbSize);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(
            IntPtr hRawInput,
            uint uiCommand,
            IntPtr pData,
            ref uint pcbSize,
            uint cbSizeHeader);
    }
}