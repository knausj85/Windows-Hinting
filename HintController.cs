using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using HintOverlay.Models;
using HintOverlay.Services;
using UIAutomationClient;

namespace HintOverlay
{
    internal sealed class HintController : IDisposable
    {
        private readonly OverlayForm _overlay;
        private readonly IUIAutomationService _uiaService;
        private readonly IKeyboardHookService _keyboardService;
        private readonly IPreferencesService _preferencesService;
        private readonly HintStateManager _stateManager;
        private readonly HintInputHandler _inputHandler;
        private readonly TrayIconManager _trayIcon;
        
        private HintOverlayOptions _options;
        private long _lastToggleTicks;
        private const long ToggleDebounceMs = 200;

        public HintController(
            OverlayForm overlay,
            IUIAutomationService uiaService,
            IKeyboardHookService keyboardService,
            IPreferencesService preferencesService,
            TrayIconManager trayIcon)
        {
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            _uiaService = uiaService ?? throw new ArgumentNullException(nameof(uiaService));
            _keyboardService = keyboardService ?? throw new ArgumentNullException(nameof(keyboardService));
            _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
            _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
            
            _stateManager = new HintStateManager();
            _inputHandler = new HintInputHandler(_stateManager);
            
            // Load preferences
            _options = _preferencesService.Load();
            ApplyOptions();
            
            // Wire up events
            _overlay.ToggleRequested += (s, e) => Toggle();
            _trayIcon.ToggleRequested += (s, e) => Toggle();
            _trayIcon.PreferencesRequested += OnPreferencesRequested;
            _trayIcon.ExitRequested += (s, e) => Application.Exit();
            
            _stateManager.ModeChanged += OnModeChanged;
            _stateManager.HintsChanged += OnHintsChanged;
            _stateManager.FilterChanged += OnFilterChanged;
            
            _inputHandler.SelectionCommitted += OnSelectionCommitted;
            
            _keyboardService.KeyPressed += OnKeyPressed;
            _keyboardService.KeyReleased += OnKeyReleased;
            
            // Show overlay
            _overlay.Show();
        }

        private void ApplyOptions()
        {
            _overlay.ShowRectangles = _options.ShowRectangles;
            _overlay.RegisterGlobalHotkey(_options.Hotkey.Modifiers, _options.Hotkey.VirtualKey);
        }

        public void Toggle()
        {
            long now = Stopwatch.GetTimestamp();
            long elapsedMs = (now - _lastToggleTicks) * 1000 / Stopwatch.Frequency;
            
            if (elapsedMs < ToggleDebounceMs)
                return;
                
            _lastToggleTicks = now;
            
            if (_stateManager.CurrentMode == HintMode.Inactive)
            {
                _stateManager.Activate();
                ScanForHints();
            }
            else
            {
                _stateManager.Deactivate();
            }
        }

        private void ScanForHints()
        {
            var sw = Stopwatch.StartNew();
            
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                Debug.WriteLine("No foreground window");
                return;
            }
            
            var elements = _uiaService.FindClickableElements(hwnd);
            Debug.WriteLine($"Found {elements.Count} clickable elements in {sw.ElapsedMilliseconds}ms");
            
            if (elements.Count == 0)
            {
                _stateManager.Deactivate();
                return;
            }
            
            // Generate labels
            var labels = LabelGenerator.Generate(elements.Count);
            
            // Create hint items
            var hints = elements.Select((e, i) => new HintItem
            {
                Rect = e.Bounds,
                Element = e.Element,
                Label = labels[i],
                CurrentOpacity = 1.0f,
                TargetOpacity = 1.0f
            }).ToList();
            
            _stateManager.SetHints(hints);
        }

        private void OnModeChanged(object? sender, HintMode mode)
        {
            Debug.WriteLine($"Mode changed: {mode}");
            
            bool enabled = mode != HintMode.Inactive;
            _overlay.SetEnabled(enabled);
            
            if (enabled)
            {
                _keyboardService.Start();
            }
            else
            {
                _keyboardService.Stop();
                _inputHandler.Reset();
            }
        }

        private void OnHintsChanged(object? sender, System.Collections.Generic.IReadOnlyList<HintItem> hints)
        {
            _overlay.SetHints(hints.ToList());
        }

        private void OnFilterChanged(object? sender, string filter)
        {
            _overlay.SetFilterPrefix(filter);
        }

        private void OnKeyPressed(object? sender, KeyboardEventArgs e)
        {
            if (_stateManager.CurrentMode == HintMode.Inactive)
                return;
            
            // Check if this is the hotkey
            int expectedModifiers = _options.Hotkey.Modifiers;
            KeyModifiers actualMods = e.Modifiers;
            
            bool hotkeyMatches = e.VirtualKeyCode == _options.Hotkey.VirtualKey &&
                                CheckModifiersMatch(expectedModifiers, actualMods);
            
            if (hotkeyMatches)
            {
                // Don't consume the hotkey
                return;
            }
            
            // Let the input handler process it
            bool handled = _inputHandler.ProcessKeyDown(e.VirtualKeyCode, e.Modifiers);
            e.Handled = handled;
        }

        private void OnKeyReleased(object? sender, KeyboardEventArgs e)
        {
            _inputHandler.ProcessKeyUp(e.VirtualKeyCode);
        }

        private bool CheckModifiersMatch(int expectedWin32Mods, KeyModifiers actualMods)
        {
            const int MOD_CONTROL = 0x0002;
            const int MOD_ALT = 0x0001;
            const int MOD_SHIFT = 0x0004;
            
            bool expectCtrl = (expectedWin32Mods & MOD_CONTROL) != 0;
            bool expectAlt = (expectedWin32Mods & MOD_ALT) != 0;
            bool expectShift = (expectedWin32Mods & MOD_SHIFT) != 0;
            
            bool hasCtrl = (actualMods & KeyModifiers.Control) != 0;
            bool hasAlt = (actualMods & KeyModifiers.Alt) != 0;
            bool hasShift = (actualMods & KeyModifiers.Shift) != 0;
            
            return expectCtrl == hasCtrl && expectAlt == hasAlt && expectShift == hasShift;
        }

        private void OnSelectionCommitted(object? sender, EventArgs e)
        {
            var match = _stateManager.GetExactMatch();
            if (match == null)
                return;
            
            try
            {
                var element = match.Element;
                
                // Try different interaction patterns in priority order
                if (TryInvokePattern(element))
                {
                    Debug.WriteLine("Invoked element");
                }
                else if (TryExpandCollapsePattern(element))
                {
                    Debug.WriteLine("Expanded/collapsed element");
                }
                else if (TrySelectionItemPattern(element))
                {
                    Debug.WriteLine("Selected element");
                }
                else if (TryTogglePattern(element))
                {
                    Debug.WriteLine("Toggled element");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error activating element: {ex.Message}");
            }
            finally
            {
                // Hide hints after activation
                _stateManager.Deactivate();
            }
        }

        private bool TryInvokePattern(IUIAutomationElement element)
        {
            try
            {
                if (element.GetCachedPattern(10000) is IUIAutomationInvokePattern pattern)
                {
                    pattern.Invoke();
                    return true;
                }
            }
            catch { }
            return false;
        }

        private bool TryExpandCollapsePattern(IUIAutomationElement element)
        {
            try
            {
                if (element.GetCachedPattern(10005) is IUIAutomationExpandCollapsePattern pattern)
                {
                    pattern.Expand();
                    return true;
                }
            }
            catch { }
            return false;
        }

        private bool TrySelectionItemPattern(IUIAutomationElement element)
        {
            try
            {
                if (element.GetCachedPattern(10010) is IUIAutomationSelectionItemPattern pattern)
                {
                    pattern.Select();
                    return true;
                }
            }
            catch { }
            return false;
        }

        private bool TryTogglePattern(IUIAutomationElement element)
        {
            try
            {
                if (element.GetCachedPattern(10015) is IUIAutomationTogglePattern pattern)
                {
                    pattern.Toggle();
                    return true;
                }
            }
            catch { }
            return false;
        }

        private void OnPreferencesRequested(object? sender, EventArgs e)
        {
            var dialog = new PreferencesDialog(_options);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                // Reload and apply
                _options = _preferencesService.Load();
                ApplyOptions();
                _overlay.Invalidate();
            }
        }

        public void Dispose()
        {
            _keyboardService.Stop();
            _trayIcon.Dispose();
            _overlay.Dispose();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
