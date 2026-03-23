using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Preferences;
using UIAutomationClient;
using WindowsHinting.Configuration;
using WindowsHinting.Forms;
using WindowsHinting.Logging;
using WindowsHinting.Models;
using WindowsHinting.Services;

namespace WindowsHinting
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
        private readonly CommandFileService _commandFileService;
        private readonly WindowRuleRegistry _ruleRegistry;
        private readonly MouseClickService _mouseClickService;

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
            TrayIconManager trayIcon,
            WindowRuleRegistry ruleRegistry,
            HintStateManager stateManager,
            HintInputHandler inputHandler,
            ElementActivatorChain activatorChain,
            CommandFileService commandFileService,
            MouseClickService mouseClickService)
        {
            using (PerformanceMetrics.Start("HintController.Constructor", logger, LogLevel.Info))
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
                _logger.Info("Initializing HintController");

                _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
                _uiaService = uiaService ?? throw new ArgumentNullException(nameof(uiaService));
                _keyboardService = keyboardService ?? throw new ArgumentNullException(nameof(keyboardService));
                _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
                _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
                _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
                _ruleRegistry = ruleRegistry ?? throw new ArgumentNullException(nameof(ruleRegistry));

                _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
                _inputHandler = inputHandler ?? throw new ArgumentNullException(nameof(inputHandler));
                _activatorChain = activatorChain ?? throw new ArgumentNullException(nameof(activatorChain));
                _commandFileService = commandFileService ?? throw new ArgumentNullException(nameof(commandFileService));
                _mouseClickService = mouseClickService ?? throw new ArgumentNullException(nameof(mouseClickService));

                // Load preferences
                _logger.Debug("Loading preferences");
                _options = PerformanceMetricsExtensions.MeasureExecution(
                    "LoadPreferences",
                    () => _preferencesService.Load(),
                    _logger,
                    LogLevel.Debug);
                ApplyOptions();

                // Wire up events
                _logger.Debug("Wiring up event handlers");
                _overlay.ToggleRequested += (s, e) => Toggle();
                _overlay.TaskbarToggleRequested += (s, e) => ToggleTaskbar();
                _trayIcon.ToggleRequested += (s, e) => Toggle();
                _trayIcon.PreferencesRequested += OnPreferencesRequested;
                _trayIcon.ExitRequested += (s, e) => Application.Exit();

                _stateManager.ModeChanged += OnModeChanged;
                _stateManager.HintsChanged += OnHintsChanged;
                _stateManager.FilterChanged += OnFilterChanged;
                _stateManager.ClickActionChanged += OnClickActionChanged;

                _inputHandler.SelectionCommitted += OnSelectionCommitted;

                _keyboardService.KeyPressed += OnKeyPressed;
                _keyboardService.KeyReleased += OnKeyReleased;

                _commandFileService.CommandReceived += OnCommandReceived;

                // Start command file service
                _logger.Debug("Starting command file service");
                _commandFileService.Start();

                // Show overlay
                _logger.Debug("Showing overlay");
                _overlay.Show();

                _logger.Info("HintController initialized successfully");
            }
        }

        private void ApplyOptions()
        {
            _logger.Debug($"Applying options - ShowRectangles: {_options.ShowRectangles}, HintPosition: {_options.HintPosition}, Hotkey: {_options.Hotkey.Modifiers}+{_options.Hotkey.VirtualKey}");
            _overlay.ShowRectangles = _options.ShowRectangles;
            _overlay.HintPosition = _options.HintPosition;

            if (_options.Hotkey.Enabled)
                _overlay.RegisterGlobalHotkey(_options.Hotkey.Modifiers, _options.Hotkey.VirtualKey);
            else
                _overlay.UnregisterGlobalHotkey();

            if (_options.TaskbarHotkey.Enabled)
                _overlay.RegisterTaskbarHotkey(_options.TaskbarHotkey.Modifiers, _options.TaskbarHotkey.VirtualKey);
            else
                _overlay.UnregisterTaskbarHotkey();

            _inputHandler.ApplyOptions(_options.ClickActionShortcuts);

            var rules = WindowRuleRegistry.MergeWithDefaults(_options.WindowRules);
            _ruleRegistry.SetRules(rules);
            _logger.Debug($"Window rules applied: {rules.Count} rule(s)");
        }

        private void OnCommandReceived(object? sender, CommandFileCommand command)
        {
            _logger.Debug($"Processing command: {command.CommandType}");

            switch (command.CommandType)
            {
                case CommandType.Toggle:
                    Toggle();
                    break;

                case CommandType.ToggleTaskbar:
                    ToggleTaskbar();
                    break;

                case CommandType.Select:
                    if (!string.IsNullOrEmpty(command.HintLabel))
                    {
                        SelectHintByLabel(command.HintLabel, command.Action);
                    }
                    break;

                case CommandType.Deactivate:
                    _stateManager.Deactivate();
                    break;
            }
        }

        private void SelectHintByLabel(string label, ClickAction action = ClickAction.Default)
        {
            _logger.Info($"Attempting to select hint with label: {label}, action: {action}");

            var hint = _stateManager.CurrentHints.FirstOrDefault(h =>
                h.Label.Equals(label, StringComparison.OrdinalIgnoreCase));

            if (hint == null)
            {
                _logger.Warning($"Hint with label '{label}' not found");
                return;
            }

            _logger.Info($"Activating hint: {hint.Label}, action: {action}");

            try
            {
                if (action == ClickAction.Default)
                {
                    _activatorChain.TryActivate(hint.Element);
                }
                else
                {
                    _mouseClickService.PerformClick(hint.Rect, action);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error activating hint '{label}'", ex);
            }
            finally
            {
                // Hide hints after activation
                _logger.Debug("Deactivating hints after direct selection");
                _stateManager.Deactivate();
            }
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

                if (_stateManager.CurrentMode != HintMode.Inactive)
                {
                    if (_stateManager.CurrentSource == HintSource.Taskbar)
                    {
                        // Taskbar hints showing — dismiss them and show foreground window hints instead
                        _logger.Info("Switching from taskbar hints to foreground window hints");
                        _stateManager.Deactivate();
                        _stateManager.Activate(HintSource.ForegroundWindow);
                        ScanForHints();
                    }
                    else
                    {
                        _logger.Info("Deactivating hint mode");
                        _stateManager.Deactivate();
                    }
                }
                else
                {
                    _logger.Info("Activating hint mode (foreground window)");
                    _stateManager.Activate(HintSource.ForegroundWindow);
                    ScanForHints();
                }
            }
        }

        public void ToggleTaskbar()
        {
            using (PerformanceMetrics.Start("ToggleTaskbar", _logger, LogLevel.Debug))
            {
                long now = Stopwatch.GetTimestamp();
                long elapsedMs = (now - _lastToggleTicks) * 1000 / Stopwatch.Frequency;

                if (elapsedMs < ToggleDebounceMs)
                {
                    _logger.Debug($"ToggleTaskbar debounced - only {elapsedMs}ms since last toggle");
                    return;
                }

                _lastToggleTicks = now;

                if (_stateManager.CurrentMode != HintMode.Inactive)
                {
                    if (_stateManager.CurrentSource == HintSource.Taskbar)
                    {
                        // Taskbar hints already showing — toggle off
                        _logger.Info("Deactivating taskbar hints");
                        _stateManager.Deactivate();
                    }
                    else
                    {
                        // Global hints showing — dismiss and show taskbar hints instead
                        _logger.Info("Switching from foreground window hints to taskbar hints");
                        _stateManager.Deactivate();
                        _stateManager.Activate(HintSource.Taskbar);
                        ScanTaskbarForHints();
                    }
                }
                else
                {
                    _logger.Info("Activating taskbar hint mode");
                    _stateManager.Activate(HintSource.Taskbar);
                    ScanTaskbarForHints();
                }
            }
        }

        private async void ScanForHints()
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

                // Ensure overlay is topmost before scanning
                _overlay.EnsureTopmost();

                IReadOnlyList<Services.ClickableElement> elements;
                var timeoutMs = _options.ScanTimeoutMs;
                var timedOut = false;
                if (timeoutMs > 0)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    elements = await _uiaService.FindClickableElementsAsync(hwnd, timeoutMs);
                    sw.Stop();
                    timedOut = sw.ElapsedMilliseconds >= timeoutMs;
                    _logger.Info($"FindClickableElements completed in {sw.ElapsedMilliseconds}ms (timeout={timeoutMs}ms)");
                }
                else
                {
                    elements = PerformanceMetricsExtensions.MeasureExecution(
                        "FindClickableElements",
                        () => _uiaService.FindClickableElements(hwnd),
                        _logger,
                        LogLevel.Info);
                }

                _logger.Info($"Found {elements.Count} clickable elements");

                if (elements.Count == 0)
                {
                    if (timedOut)
                    {
                        _logger.Warning($"Hint population timed out after {timeoutMs}ms");
                        _trayIcon.ShowNotification("Hint Timeout", $"Hint population timed out after {timeoutMs}ms. Try increasing the timeout in preferences.");
                    }

                    _logger.Info("No clickable elements found, deactivating");
                    _stateManager.Deactivate();
                    return;
                }

                // Deduplicate overlapping elements
                //var deduped = PerformanceMetricsExtensions.MeasureExecution(
                //    "DeduplicateElements",
                //    () => ElementDeduplicator.Deduplicate(elements, _logger, _options.OverlapThreshold),
                //    _logger,
                //    LogLevel.Debug);

                //if (deduped.Count == 0)
                //{
                //    _logger.Info("No elements after deduplication, deactivating");
                //    _stateManager.Deactivate();
                //    return;
                //}

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

        private async void ScanTaskbarForHints()
        {
            using (PerformanceMetrics.Start("ScanTaskbarForHints", _logger, LogLevel.Info))
            {
                var hwnd = _windowManager.GetTaskbarWindow();
                if (!_windowManager.IsWindowValid(hwnd))
                {
                    _logger.Warning("Taskbar window not found");
                    _stateManager.Deactivate();
                    return;
                }

                _logger.Debug($"Scanning taskbar window: {hwnd}");

                // Ensure overlay is topmost before scanning
                _overlay.EnsureTopmost();

                IReadOnlyList<Services.ClickableElement> elements;
                var timeoutMs = _options.ScanTimeoutMs;
                var timedOut = false;
                if (timeoutMs > 0)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    elements = await _uiaService.FindClickableElementsAsync(hwnd, timeoutMs);
                    sw.Stop();
                    timedOut = sw.ElapsedMilliseconds >= timeoutMs;
                    _logger.Info($"FindClickableElements(Taskbar) completed in {sw.ElapsedMilliseconds}ms (timeout={timeoutMs}ms)");
                }
                else
                {
                    elements = PerformanceMetricsExtensions.MeasureExecution(
                        "FindClickableElements(Taskbar)",
                        () => _uiaService.FindClickableElements(hwnd),
                        _logger,
                        LogLevel.Info);
                }

                _logger.Info($"Found {elements.Count} taskbar clickable elements");

                if (elements.Count == 0)
                {
                    if (timedOut)
                    {
                        _logger.Warning($"Taskbar hint population timed out after {timeoutMs}ms");
                        _trayIcon.ShowNotification("Hint Timeout", $"Taskbar hint population timed out after {timeoutMs}ms. Try increasing the timeout in preferences.");
                    }

                    _logger.Info("No taskbar clickable elements found, deactivating");
                    _stateManager.Deactivate();
                    return;
                }

                // Deduplicate overlapping elements
                var deduped = PerformanceMetricsExtensions.MeasureExecution(
                    "DeduplicateElements(Taskbar)",
                    () => ElementDeduplicator.Deduplicate(elements, _logger, _options.OverlapThreshold),
                    _logger,
                    LogLevel.Debug);

                if (deduped.Count == 0)
                {
                    _logger.Info("No taskbar elements after deduplication, deactivating");
                    _stateManager.Deactivate();
                    return;
                }

                // Generate labels
                var labels = PerformanceMetricsExtensions.MeasureExecution(
                    "GenerateLabels(Taskbar)",
                    () => LabelGenerator.Generate(deduped.Count),
                    _logger,
                    LogLevel.Debug);

                // Create hint items
                var hints = PerformanceMetricsExtensions.MeasureExecution(
                    "CreateHintItems(Taskbar)",
                    () => deduped.Select((e, i) => new HintItem
                    {
                        Rect = e.Bounds,
                        Element = e.Element,
                        Label = labels[i],
                        CurrentOpacity = 1.0f,
                        TargetOpacity = 1.0f
                    }).ToList(),
                    _logger,
                    LogLevel.Debug);

                _logger.Debug($"Created {hints.Count} taskbar hint items");
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
                _trayIcon.ResetIcon();
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

        private void OnClickActionChanged(object? sender, ClickAction action)
        {
            _logger.Debug($"Click action changed: {action}");
            _trayIcon.SetClickAction(action);
        }

        private void OnKeyPressed(object? sender, KeyboardEventArgs e)
        {
            if (_stateManager.CurrentMode == HintMode.Inactive)
                return;

            KeyModifiers actualMods = e.Modifiers;

            // Check if this is the global hotkey
            bool hotkeyMatches = e.VirtualKeyCode == _options.Hotkey.VirtualKey &&
                                CheckModifiersMatch(_options.Hotkey.Modifiers, actualMods);

            if (hotkeyMatches)
            {
                _logger.Debug("Hotkey pressed, not consuming");
                return;
            }

            // Check if this is the taskbar hotkey
            bool taskbarHotkeyMatches = _options.TaskbarHotkey.Enabled &&
                                       e.VirtualKeyCode == _options.TaskbarHotkey.VirtualKey &&
                                       CheckModifiersMatch(_options.TaskbarHotkey.Modifiers, actualMods);

            if (taskbarHotkeyMatches)
            {
                _logger.Debug("Taskbar hotkey pressed, not consuming");
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

        private void OnSelectionCommitted(object? sender, SelectionCommittedEventArgs e)
        {
            using (PerformanceMetrics.Start("OnSelectionCommitted", _logger, LogLevel.Info))
            {
                var match = _stateManager.GetExactMatch();
                if (match == null)
                {
                    _logger.Warning("Selection committed but no exact match found");
                    return;
                }

                _logger.Info($"Activating element with label: {match.Label}, action: {e.Action}");

                try
                {
                    if (e.Action == ClickAction.Default)
                    {
                        _activatorChain.TryActivate(match.Element);
                    }
                    else
                    {
                        _mouseClickService.PerformClick(match.Rect, e.Action);
                    }
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
                dialog.HotkeyRecordingStarted += (_, _) =>
                {
                    _logger.Debug("Hotkey recording started, unregistering global hotkeys");
                    _overlay.UnregisterGlobalHotkey();
                    _overlay.UnregisterTaskbarHotkey();
                };
                dialog.HotkeyRecordingStopped += (_, _) =>
                {
                    _logger.Debug("Hotkey recording stopped, re-registering global hotkeys");
                    if (_options.Hotkey.Enabled)
                        _overlay.RegisterGlobalHotkey(_options.Hotkey.Modifiers, _options.Hotkey.VirtualKey);
                    if (_options.TaskbarHotkey.Enabled)
                        _overlay.RegisterTaskbarHotkey(_options.TaskbarHotkey.Modifiers, _options.TaskbarHotkey.VirtualKey);
                };
                var previousPosition = _overlay.HintPosition;
                dialog.HintPositionChanged += (_, newPos) =>
                {
                    _logger.Debug($"Live preview: hint position changed to {newPos}");
                    _overlay.HintPosition = newPos;
                    _overlay.Invalidate();
                };
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
                    _logger.Debug("Preferences dialog cancelled, reverting hint position");
                    _overlay.HintPosition = previousPosition;
                    _overlay.Invalidate();
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _logger.Info("Disposing HintController");
            _commandFileService.Dispose();
            _keyboardService.Stop();
            _trayIcon.Dispose();
            _overlay.Dispose();
            _uiaService.Dispose();
            _logger.Info("HintController disposed");

            _disposed = true;
        }
    }
}
