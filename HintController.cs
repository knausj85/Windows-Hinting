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
        public OverlayForm Overlay { get; }

        private readonly IUIAutomation _uia = new CUIAutomation();
        private bool _enabled;
        private readonly object _gate = new();

        private List<HintItem> _currentHints = new();
        private string _typed = "";

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
            lock (_gate)
            {
                _enabled = !_enabled;
                Overlay.SetEnabled(_enabled);

                if (_enabled)
                {
                    InstallKeyboardHook();

                    Measure("UIA", Refresh);
                }
                else
                {
                    RemoveKeyboardHook();
                    _typed = "";
                    Overlay.SetHints(new List<HintItem>());
                }
            }
        }

        private void Refresh()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            var root = _uia.ElementFromHandle(hwnd);

            //UIA_IsTogglePatternAvailablePropertyId = 30041
            //UIA_IsSelectionItemPatternAvailablePropertyId = 30036
            //UIA_IsInvokePatternAvailablePropertyId = 30031
            // UIA_IsVirtualizedItemPatternAvailablePropertyId 30109
            var invoke_pattern = _uia.CreatePropertyCondition(30031, true);
            var selection_pattern = _uia.CreatePropertyCondition(30036, true);
            var toggle_pattern = _uia.CreatePropertyCondition(30041, true);
            //var virtualized_pattern = _uia.CreatePropertyCondition(30109, true);

            var patterns = new List<IUIAutomationCondition>()
            {
                invoke_pattern,
                selection_pattern,
                toggle_pattern,
                //virtualized_pattern
            };

            var status = new List<IUIAutomationCondition>()
            {
                _uia.CreatePropertyCondition(30010, true), // UIA_IsEnabledPropertyId
                _uia.CreatePropertyCondition(30022, false), // UIA_IsOffscreenPropertyId
                _uia.CreatePropertyCondition(30009, true) // UIA_IsKeyboardFocusablePropertyId
            };


            var invokeCond = _uia.CreateOrConditionFromArray(patterns.ToArray());
            var statusCond = _uia.CreateAndConditionFromArray(status.ToArray());


            var clickableTypes = new int[]
            {
                50000, // Button
                50003, // ComboBox
                50002, // CheckBox
                50004, // Edit
                50005, // Hyperlink
                50024, // TreeItem
                50019, // TabItem
                50031, // SplitButton
                50007, // ListItem (includes ListViewItem)
                50009, // Menu
                50011  // MenuItem
            };

            var typeConditions = clickableTypes
                .Select(t => _uia.CreatePropertyCondition(30003, t))
                .ToArray();

            var controlTypeCondition =
                _uia.CreateOrConditionFromArray(typeConditions);

            var cond = _uia.CreateAndCondition(statusCond, controlTypeCondition);

            var cache = _uia.CreateCacheRequest();  
            cache.TreeScope = TreeScope.TreeScope_Element;
            cache.AddProperty(30001); // UIA_BoundingRectanglePropertyId
            //cache.AddProperty(30010); // UIA_IsEnabledPropertyId
            //cache.AddProperty(30022); // UIA_IsOffscreenPropertyId
            cache.AddProperty(30003); // UIA_ControlTypePropertyId
            //cache.AddProperty(30041); // UIA_IsTogglePatternAvailablePropertyId
            //cache.AddProperty(30031); // UIA_IsInvokePatternAvailablePropertyId

            //cache.AddProperty(30109); // UIA_IsVirtualizedItemPatternAvailablePropertyId

            //IUIAutomationTreeWalker walker = _uia.CreateTreeWalker(cond);
            var elems = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, cond, cache);
            var list = new List<HintItem>();

            for (int i = 0; i < elems.Length; i++)
            {
                var e = elems.GetElement(i);
                tagRECT rect = e.CachedBoundingRectangle;
                bool shouldProcess = clickableTypes.Contains(e.CachedControlType);
                
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
                    Label = labels[i]
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

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _enabled)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (vkCode >= 0x41 && vkCode <= 0x5A) // A-Z
                {
                    _typed += ((char)vkCode).ToString();
                    CheckMatch();
                    return (IntPtr)1; // consume ONLY when enabled
                }
                if (vkCode == 0x08 && _typed.Length > 0) // backspace
                {
                    _typed = _typed[..^1];
                    return (IntPtr)1;
                }
                if (vkCode == 0x1B) // escape clears
                {
                    _typed = "";
                    return (IntPtr)1;
                }
            }

            return CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private void CheckMatch()
        {
            foreach (var h in _currentHints)
            {
                if (h.Label.Equals(_typed, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // Use 10000 directly instead of UIA_PatternIds.UIA_InvokePatternId
                        var patternObj = h.Element.GetCurrentPattern(10000);
                        if (patternObj is IUIAutomationInvokePattern pattern)
                        {
                            pattern.Invoke();
                        }
                    }
                    catch { }

                    Toggle(); // auto-hide after invoke
                    break;
                }
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
    }

    // If 'IUIAutomationInvokePattern' is not available in your referenced UIAutomationClient,
    // you may need to add a COM reference to "UIAutomationClient" in your project.
    // In Visual Studio: Right-click project > Add > Reference... > COM > UIAutomationClient.

    //
    // If you already have 'using UIAutomationClient;' and still get CS0246,
    // you may need to check your project references and ensure that the correct interop assembly is referenced.
    // The type 'IUIAutomationInvokePattern' is defined in the UIAutomationClient COM library.
    //
}