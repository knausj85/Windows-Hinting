using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
            using (PerformanceMetrics.Start("HintController.Constructor", LogLevel.Info))
            {
                Logger.Info("Initializing HintController");
                
                _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
                _uiaService = uiaService ?? throw new ArgumentNullException(nameof(uiaService));
                _keyboardService = keyboardService ?? throw new ArgumentNullException(nameof(keyboardService));
                _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
                _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
                
                _stateManager = new HintStateManager();
                _inputHandler = new HintInputHandler(_stateManager);
                
                // Load preferences
                Logger.Debug("Loading preferences");
                _options = PerformanceMetricsExtensions.MeasureExecution(
                    "LoadPreferences",
                    () => _preferencesService.Load(),
                    LogLevel.Debug);
                ApplyOptions();
                
                // Wire up events
                Logger.Debug("Wiring up event handlers");
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
                Logger.Debug("Showing overlay");
                _overlay.Show();
                
                Logger.Info("HintController initialized successfully");
            }
        }

        private void ApplyOptions()
        {
            Logger.Debug($"Applying options - ShowRectangles: {_options.ShowRectangles}, Hotkey: {_options.Hotkey.Modifiers}+{_options.Hotkey.VirtualKey}");
            _overlay.ShowRectangles = _options.ShowRectangles;
            _overlay.RegisterGlobalHotkey(_options.Hotkey.Modifiers, _options.Hotkey.VirtualKey);
        }

        public void Toggle()
        {
            using (PerformanceMetrics.Start("Toggle", LogLevel.Debug))
            {
                long now = Stopwatch.GetTimestamp();
                long elapsedMs = (now - _lastToggleTicks) * 1000 / Stopwatch.Frequency;
                
                if (elapsedMs < ToggleDebounceMs)
                {
                    Logger.Debug($"Toggle debounced - only {elapsedMs}ms since last toggle");
                    return;
                }
                    
                _lastToggleTicks = now;
                
                if (_stateManager.CurrentMode == HintMode.Inactive)
                {
                    Logger.Info("Activating hint mode");
                    _stateManager.Activate();
                    ScanForHints();
                }
                else
                {
                    Logger.Info("Deactivating hint mode");
                    _stateManager.Deactivate();
                }
            }
        }

        private void ScanForHints()
        {
            using (PerformanceMetrics.Start("ScanForHints", LogLevel.Info))
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    Logger.Warning("No foreground window found");
                    return;
                }
                
                Logger.Debug($"Scanning window: {hwnd}");
                
                var elements = PerformanceMetricsExtensions.MeasureExecution(
                    "FindClickableElements",
                    () => _uiaService.FindClickableElements(hwnd),
                    LogLevel.Info);
                
                Logger.Info($"Found {elements.Count} clickable elements");
                
                if (elements.Count == 0)
                {
                    Logger.Info("No clickable elements found, deactivating");
                    _stateManager.Deactivate();
                    return;
                }
                
                // Generate labels
                var labels = PerformanceMetricsExtensions.MeasureExecution(
                    "GenerateLabels",
                    () => LabelGenerator.Generate(elements.Count),
                    LogLevel.Debug);
                
                // Create hint items
                var hints = PerformanceMetricsExtensions.MeasureExecution(
                    "CreateHintItems",
                    () => elements.Select((e, i) => new HintItem
                    {
                        Rect = e.Bounds,
                        Element = e.Element,
                        Label = labels[i],
                        CurrentOpacity = 1.0f,
                        TargetOpacity = 1.0f
                    }).ToList(),
                    LogLevel.Debug);
                
                Logger.Debug($"Created {hints.Count} hint items");
                _stateManager.SetHints(hints);
            }
        }

        private void OnModeChanged(object? sender, HintMode mode)
        {
            Logger.Info($"Mode changed: {mode}");
            
            bool enabled = mode != HintMode.Inactive;
            _overlay.SetEnabled(enabled);
            
            if (enabled)
            {
                Logger.Debug("Starting keyboard service");
                _keyboardService.Start();
            }
            else
            {
                Logger.Debug("Stopping keyboard service");
                _keyboardService.Stop();
                _inputHandler.Reset();
            }
        }

        private void OnHintsChanged(object? sender, System.Collections.Generic.IReadOnlyList<HintItem> hints)
        {
            Logger.Debug($"Hints changed - count: {hints.Count}");
            _overlay.SetHints(hints.ToList());
        }

        private void OnFilterChanged(object? sender, string filter)
        {
            Logger.Debug($"Filter changed: '{filter}'");
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
                Logger.Debug("Hotkey pressed, not consuming");
                return;
            }
            
            // Let the input handler process it
            bool handled = _inputHandler.ProcessKeyDown(e.VirtualKeyCode, e.Modifiers);
            Logger.Debug($"Key pressed: VK={e.VirtualKeyCode}, Mods={e.Modifiers}, Handled={handled}");
            e.Handled = handled;
        }

        private void OnKeyReleased(object? sender, KeyboardEventArgs e)
        {
            Logger.Debug($"Key released: VK={e.VirtualKeyCode}");
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
            using (PerformanceMetrics.Start("OnSelectionCommitted", LogLevel.Info))
            {
                var match = _stateManager.GetExactMatch();
                if (match == null)
                {
                    Logger.Warning("Selection committed but no exact match found");
                    return;
                }
                
                Logger.Info($"Activating element with label: {match.Label}");
                
                try
                {
                    var element = match.Element;
                    
                    // Try different interaction patterns in priority order
                    if (TryInvokePattern(element))
                    {
                        Logger.Info("Successfully invoked element");
                    }
                    else if (TryExpandCollapsePattern(element))
                    {
                        Logger.Info("Successfully expanded/collapsed element");
                    }
                    else if (TrySelectionItemPattern(element))
                    {
                        Logger.Info("Successfully selected element");
                    }
                    else if (TryTogglePattern(element))
                    {
                        Logger.Info("Successfully toggled element");
                    }
                    else
                    {
                        Logger.Warning("No interaction pattern succeeded for element");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Error activating element", ex);
                }
                finally
                {
                    // Hide hints after activation
                    Logger.Debug("Deactivating hints after element activation");
                    _stateManager.Deactivate();
                }
            }
        }

        private bool TryInvokePattern(IUIAutomationElement element)
        {
            try
            {
                if (element.GetCachedPattern(UIA_PatternIds.UIA_InvokePatternId) is IUIAutomationInvokePattern pattern)
                {
                    pattern.Invoke();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"InvokePattern failed: {ex.Message}");
            }
            return false;
        }

        private bool TryExpandCollapsePattern(IUIAutomationElement element)
        {
            try
            {
                if (element.GetCachedPattern(UIA_PatternIds.UIA_ExpandCollapsePatternId) is IUIAutomationExpandCollapsePattern pattern)
                {
                    pattern.Expand();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"ExpandCollapsePattern failed: {ex.Message}");
            }
            return false;
        }

        private bool TrySelectionItemPattern(IUIAutomationElement element)
        {
            try
            {
                if (element.GetCachedPattern(UIA_PatternIds.UIA_SelectionItemPatternId) is IUIAutomationSelectionItemPattern pattern)
                {
                    pattern.Select();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"SelectionItemPattern failed: {ex.Message}");
            }
            return false;
        }

        private bool TryTogglePattern(IUIAutomationElement element)
        {
            try
            {
                if (element.GetCachedPattern(UIA_PatternIds.UIA_TogglePatternId) is IUIAutomationTogglePattern pattern)
                {
                    pattern.Toggle();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"TogglePattern failed: {ex.Message}");
            }
            return false;
        }

        private void OnPreferencesRequested(object? sender, EventArgs e)
        {
            using (PerformanceMetrics.Start("ShowPreferencesDialog", LogLevel.Info))
            {
                Logger.Info("Opening preferences dialog");
                var dialog = new PreferencesDialog(_options);
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    Logger.Info("Preferences saved, reloading and applying");
                    // Reload and apply
                    _options = _preferencesService.Load();
                    ApplyOptions();
                    _overlay.Invalidate();
                }
                else
                {
                    Logger.Debug("Preferences dialog cancelled");
                }
            }
        }

        public void Dispose()
        {
            Logger.Info("Disposing HintController");
            _keyboardService.Stop();
            _trayIcon.Dispose();
            _overlay.Dispose();
            Logger.Info("HintController disposed");
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }

    internal static class Logger
    {
        private static LogLevel _minLevel = LogLevel.Debug;
        private static readonly object _lock = new object();

        public static LogLevel MinimumLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        public static void Debug(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.Debug, message, memberName, filePath);
        }

        public static void Info(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.Info, message, memberName, filePath);
        }

        public static void Warning(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.Warning, message, memberName, filePath);
        }

        public static void Error(string message, Exception? ex = null, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            var fullMessage = ex != null ? $"{message} - Exception: {ex.Message}\n{ex.StackTrace}" : message;
            Log(LogLevel.Error, fullMessage, memberName, filePath);
        }

        private static void Log(LogLevel level, string message, string memberName, string filePath)
        {
            if (level < _minLevel)
                return;

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] [{level}] [{fileName}.{memberName}] {message}";

            lock (_lock)
            {
                System.Diagnostics.Debug.WriteLine(logMessage);
            }
        }
    }
}
