using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using HintOverlay.Logging;
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
        private readonly IWindowManager _windowManager;
        private readonly ILogger _logger;
        private readonly HintStateManager _stateManager;
        private readonly HintInputHandler _inputHandler;
        private readonly TrayIconManager _trayIcon;
        private readonly ElementActivatorChain _activatorChain;
        
        private HintOverlayOptions _options;
        private long _lastToggleTicks;
        private const long ToggleDebounceMs = 200;
        private bool _disposed;

        public HintController(
            OverlayForm overlay,
            IUIAutomationService uiaService,
            IKeyboardHookService keyboardService,
            IPreferencesService preferencesService,
            IWindowManager windowManager,
            ILogger logger,
            TrayIconManager trayIcon)
        {
            using (PerformanceMetrics.Start("HintController.Constructor", logger, HintOverlay.Logging.LogLevel.Info))
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
                _logger.Info("Initializing HintController");
                
                _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
                _uiaService = uiaService ?? throw new ArgumentNullException(nameof(uiaService));
                _keyboardService = keyboardService ?? throw new ArgumentNullException(nameof(keyboardService));
                _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
                _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
                _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
                
                _stateManager = new HintStateManager();
                _inputHandler = new HintInputHandler(_stateManager);
                _activatorChain = new ElementActivatorChain(logger);
                
                // Load preferences
                _logger.Debug("Loading preferences");
                _options = PerformanceMetricsExtensions.MeasureExecution(
                    "LoadPreferences",
                    () => _preferencesService.Load(),
                    _logger,
                    HintOverlay.Logging.LogLevel.Debug);
                ApplyOptions();
                
                // Wire up events
                _logger.Debug("Wiring up event handlers");
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
                _logger.Debug("Showing overlay");
                _overlay.Show();
                
                _logger.Info("HintController initialized successfully");
            }
        }

        private void ApplyOptions()
        {
            _logger.Debug($"Applying options - ShowRectangles: {_options.ShowRectangles}, Hotkey: {_options.Hotkey.Modifiers}+{_options.Hotkey.VirtualKey}");
            _overlay.ShowRectangles = _options.ShowRectangles;
            _overlay.RegisterGlobalHotkey(_options.Hotkey.Modifiers, _options.Hotkey.VirtualKey);
        }

        public void Toggle()
        {
            using (PerformanceMetrics.Start("Toggle", _logger, LogLevel.Debug))
            {
                long now = Stopwatch.GetTimestamp();
                long elapsedMs = (now - _lastToggleTicks) * 1000 / Stopwatch.Frequency;
                
                if (elapsedMs < ToggleDebounceMs)
                {
                    _logger.Debug($"Toggle debounced - only {elapsedMs}ms since last toggle");
                    return;
                }
                    
                _lastToggleTicks = now;
                
                if (_stateManager.CurrentMode == HintMode.Inactive)
                {
                    _logger.Info("Activating hint mode");
                    _stateManager.Activate();
                    ScanForHints();
                }
                else
                {
                    _logger.Info("Deactivating hint mode");
                    _stateManager.Deactivate();
                }
            }
        }

        private void ScanForHints()
        {
            using (PerformanceMetrics.Start("ScanForHints", _logger, LogLevel.Info))
            {
                var hwnd = _windowManager.GetForegroundWindow();
                if (!_windowManager.IsWindowValid(hwnd))
                {
                    _logger.Warning("No valid foreground window found");
                    _stateManager.Deactivate();
                    return;
                }
                
                _logger.Debug($"Scanning window: {hwnd}");
                
                var elements = PerformanceMetricsExtensions.MeasureExecution(
                    "FindClickableElements",
                    () => _uiaService.FindClickableElements(hwnd),
                    _logger,
                    LogLevel.Info);
                
                _logger.Info($"Found {elements.Count} clickable elements");
                
                if (elements.Count == 0)
                {
                    _logger.Info("No clickable elements found, deactivating");
                    _stateManager.Deactivate();
                    return;
                }
                
                // Generate labels
                var labels = PerformanceMetricsExtensions.MeasureExecution(
                    "GenerateLabels",
                    () => LabelGenerator.Generate(elements.Count),
                    _logger,
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
                    _logger,
                    LogLevel.Debug);
                
                _logger.Debug($"Created {hints.Count} hint items");
                _stateManager.SetHints(hints);
            }
        }

        private void OnModeChanged(object? sender, HintMode mode)
        {
            _logger.Info($"Mode changed: {mode}");
            
            bool enabled = mode != HintMode.Inactive;
            _overlay.SetEnabled(enabled);
            
            if (enabled)
            {
                _logger.Debug("Starting keyboard service");
                _keyboardService.Start();
            }
            else
            {
                _logger.Debug("Stopping keyboard service");
                _keyboardService.Stop();
                _inputHandler.Reset();
            }
        }

        private void OnHintsChanged(object? sender, System.Collections.Generic.IReadOnlyList<HintItem> hints)
        {
            _logger.Debug($"Hints changed - count: {hints.Count}");
            _overlay.SetHints(hints.ToList());
        }

        private void OnFilterChanged(object? sender, string filter)
        {
            _logger.Debug($"Filter changed: '{filter}'");
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
                _logger.Debug("Hotkey pressed, not consuming");
                return;
            }
            
            // Let the input handler process it
            bool handled = _inputHandler.ProcessKeyDown(e.VirtualKeyCode, e.Modifiers);
            _logger.Debug($"Key pressed: VK={e.VirtualKeyCode}, Mods={e.Modifiers}, Handled={handled}");
            e.Handled = handled;
        }

        private void OnKeyReleased(object? sender, KeyboardEventArgs e)
        {
            _logger.Debug($"Key released: VK={e.VirtualKeyCode}");
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
            using (PerformanceMetrics.Start("OnSelectionCommitted", _logger, LogLevel.Info))
            {
                var match = _stateManager.GetExactMatch();
                if (match == null)
                {
                    _logger.Warning("Selection committed but no exact match found");
                    return;
                }
                
                _logger.Info($"Activating element with label: {match.Label}");
                
                try
                {
                    _activatorChain.TryActivate(match.Element);
                }
                catch (Exception ex)
                {
                    _logger.Error("Error activating element", ex);
                }
                finally
                {
                    // Hide hints after activation
                    _logger.Debug("Deactivating hints after element activation");
                    _stateManager.Deactivate();
                }
            }
        }

        private void OnPreferencesRequested(object? sender, EventArgs e)
        {
            using (PerformanceMetrics.Start("ShowPreferencesDialog", _logger, LogLevel.Info))
            {
                _logger.Info("Opening preferences dialog");
                var dialog = new PreferencesDialog(_options);
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _logger.Info("Preferences saved, reloading and applying");
                    // Reload and apply
                    _options = _preferencesService.Load();
                    ApplyOptions();
                    _overlay.Invalidate();
                }
                else
                {
                    _logger.Debug("Preferences dialog cancelled");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _logger.Info("Disposing HintController");
            _keyboardService.Stop();
            _trayIcon.Dispose();
            _overlay.Dispose();
            _uiaService.Dispose();
            _logger.Info("HintController disposed");
            
            _disposed = true;
        }
    }
}
