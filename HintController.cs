using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
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

        public HintController()
        {
            Overlay = new OverlayForm();
            Overlay.Show();
            Overlay.RegisterGlobalHotkey();
            Overlay.ToggleRequested += (_, __) => Toggle();
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
               50000, // Button
               50002, // CheckBox
               50003, // ComboBox
               50004, // Edit
               50005, // Hyperlink
               50007, // ListItem (includes ListViewItem)
               50009, // Menu
               50011, // MenuItem
               50013, // RadioButton
               50019, // TabItem
               50024, // TreeItem
               50031, // SplitButton
            };

            var statusAndConditionList = new List<IUIAutomationCondition>()
            {
               _uia.CreatePropertyCondition(30010, true), // UIA_IsEnabledPropertyId
               _uia.CreatePropertyCondition(30022, false), // UIA_IsOffscreenPropertyId
            };

            var statusAndCondition = _uia.CreateAndConditionFromArray(statusAndConditionList.ToArray());

            var controlTypeConditionList = clickableControlTypes
                .Select(t => _uia.CreatePropertyCondition(30003, t))
                .ToArray();

            var controlTypeOrCondition =
                _uia.CreateOrConditionFromArray(controlTypeConditionList);

            var combinedStatusAndTypeCondition = _uia.CreateAndCondition(statusAndCondition, controlTypeOrCondition);

            var cache = _uia.CreateCacheRequest();
            cache.TreeScope = TreeScope.TreeScope_Element;
            cache.AddProperty(30001); // UIA_BoundingRectanglePropertyId
            cache.AddProperty(30003); // UIA_ControlTypePropertyId
            cache.AddProperty(30041); // UIA_IsTogglePatternAvailablePropertyId
            cache.AddProperty(30031); // UIA_IsInvokePatternAvailablePropertyId
            cache.AddProperty(30028); // UIA_IsExpandCollapsePatternAvailablePropertyId
            cache.AddProperty(30036); // UIA_IsSelectionItemPatternAvailablePropertyId
            cache.AddPattern(10000);
            cache.AddPattern(10005);
            cache.AddPattern(10010);
            cache.AddPattern(10015);

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

                // don't intercept hotkey (Ctrl+Alt+H)
                bool ctrlDown = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool altDown = (GetAsyncKeyState(0x12) & 0x8000) != 0;
                if (ctrlDown && altDown && vkCode == 0x48) // H
                {
                    return CallNextHookEx(_kbHook, nCode, wParam, lParam);
                }

                // ignore when Alt or Ctrl are held (except hotkey above)
                if (altDown || ctrlDown)
                    return CallNextHookEx(_kbHook, nCode, wParam, lParam);

                // process application keys when hinting enabled
                if (vkCode >= 0x41 && vkCode <= 0x5A) // A-Z
                {
                    _typed += ((char)vkCode).ToString();
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

                h.TargetOpacity = match ? 1.0f : 0.5f;
            }

            // Kick animation + repaint
            Overlay.SetHints(_currentHints);
            Overlay.StartAnimationIfNeeded();
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
                    if (el.GetCachedPattern(10000) is IUIAutomationInvokePattern invokePattern)
                    {
                        invokePattern.Invoke();
                    }
                    else if (el.GetCachedPattern(10005) is IUIAutomationExpandCollapsePattern expandPattern)
                    {
                        expandPattern.Expand();
                    }
                    else if (el.GetCachedPattern(10010) is IUIAutomationSelectionItemPattern selectionPattern)
                    {
                        selectionPattern.Select();
                    }
                    else if (el.GetCachedPattern(10015) is IUIAutomationTogglePattern togglePattern)
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
    //
}