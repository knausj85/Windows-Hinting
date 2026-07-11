using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
        private readonly OverlayManager _overlay;
        private readonly HotkeyWindow _hotkeyWindow;
        private readonly IUIAutomationService _uiaService;
        private readonly IKeyboardHookService _keyboardService;
        private readonly IForegroundWindowHookService _foregroundWindowHookService;
        private readonly IPreferencesService _preferencesService;
        private readonly IWindowManager _windowManager;
        private readonly ILogger _logger;
        private readonly HintStateManager _stateManager;
        private readonly ScrollModeStateManager _scrollModeStateManager;
        private readonly ScrollController _scrollController;
        private readonly HintInputHandler _inputHandler;
        private readonly TrayIconManager _trayIcon;
        private readonly ElementActivatorChain _activatorChain;
        //private readonly NamedPipeService _namedPipeService;
        private readonly WindowRuleRegistry _ruleRegistry;
        private readonly MouseClickService _mouseClickService;
        private readonly StartupService _startupService;
        private readonly UpdateService _updateService;

        private HintOverlayOptions _options;
        private long _lastToggleTicks;
        private const long ToggleDebounceMs = 200;
        // Using System.Threading.Timer (not System.Windows.Forms.Timer) so the
        // process does not carry a long-lived TimerNativeWindow HWND, which
        // external tools like Talon Voice can otherwise latch onto instead of
        // the real overlay window.
        private readonly System.Threading.Timer _autoHideTimer;
        private IntPtr _activeHintWindowHwnd;
        private bool _disposed;

        public HintController(
            OverlayManager overlay,
            HotkeyWindow hotkeyWindow,
            IUIAutomationService uiaService,
            IKeyboardHookService keyboardService,
            IForegroundWindowHookService foregroundWindowHookService,
            IPreferencesService preferencesService,
            IWindowManager windowManager,
            ILogger logger,
            TrayIconManager trayIcon,
            WindowRuleRegistry ruleRegistry,
            HintStateManager stateManager,
            ScrollModeStateManager scrollModeStateManager,
            ScrollController scrollController,
            HintInputHandler inputHandler,
            ElementActivatorChain activatorChain,
            //NamedPipeService namedPipeService,
            MouseClickService mouseClickService,
            StartupService startupService,
            UpdateService updateService)
        {
            using (PerformanceMetrics.Start("HintController.Constructor", logger, LogLevel.Info))
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
                _logger.Info("Initializing HintController");

                _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
                _hotkeyWindow = hotkeyWindow ?? throw new ArgumentNullException(nameof(hotkeyWindow));
                _uiaService = uiaService ?? throw new ArgumentNullException(nameof(uiaService));
                _keyboardService = keyboardService ?? throw new ArgumentNullException(nameof(keyboardService));
                _foregroundWindowHookService = foregroundWindowHookService ?? throw new ArgumentNullException(nameof(foregroundWindowHookService));
                _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
                _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
                _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
                _ruleRegistry = ruleRegistry ?? throw new ArgumentNullException(nameof(ruleRegistry));

                _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
                _scrollModeStateManager = scrollModeStateManager ?? throw new ArgumentNullException(nameof(scrollModeStateManager));
                _scrollController = scrollController ?? throw new ArgumentNullException(nameof(scrollController));
                _inputHandler = inputHandler ?? throw new ArgumentNullException(nameof(inputHandler));
                _activatorChain = activatorChain ?? throw new ArgumentNullException(nameof(activatorChain));
                //_namedPipeService = namedPipeService ?? throw new ArgumentNullException(nameof(namedPipeService));
                _mouseClickService = mouseClickService ?? throw new ArgumentNullException(nameof(mouseClickService));
                _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
                _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));

                // Auto-hide timer (threadpool-based; see field comment).
                _autoHideTimer = new System.Threading.Timer(OnAutoHideTick, null, Timeout.Infinite, Timeout.Infinite);

                // Load preferences
                _logger.Debug("Loading preferences");
                _options = PerformanceMetricsExtensions.MeasureExecution(
                    "LoadPreferences",
                    () => _preferencesService.Load(),
                    _logger,
                    LogLevel.Debug);

                // Generate preferences.json on first run for discoverability
                if (!_preferencesService.Exists())
                {
                    _logger.Info("Preferences file not found; generating with defaults");
                    _preferencesService.Save(_options);
                }

                ApplyOptions();

                // Wire up events
                _logger.Debug("Wiring up event handlers");
                _hotkeyWindow.ToggleRequested += (s, e) => Toggle();
                _hotkeyWindow.TaskbarToggleRequested += (s, e) => ToggleTaskbar();
                _hotkeyWindow.ScrollToggleRequested += (s, e) => ToggleScrollMode();
                _hotkeyWindow.DisplaySettingsChanged += OnDisplaySettingsChanged;
                _trayIcon.ToggleRequested += (s, e) => Toggle();
                _trayIcon.PreferencesRequested += OnPreferencesRequested;
                _trayIcon.ExitRequested += (s, e) => Application.Exit();
                _trayIcon.CheckForUpdatesRequested += async (s, e) =>
                    await _updateService.CheckForUpdatesManuallyAsync().ConfigureAwait(true);

                _stateManager.ModeChanged += OnModeChanged;
                _stateManager.FeatureModeChanged += OnFeatureModeChanged;
                _stateManager.HintsChanged += OnHintsChanged;
                _stateManager.FilterChanged += OnFilterChanged;
                _stateManager.ClickActionChanged += OnClickActionChanged;
                _scrollModeStateManager.SelectedTargetChanged += OnScrollSelectedTargetChanged;

                _inputHandler.SelectionCommitted += OnSelectionCommitted;

                _keyboardService.KeyPressed += OnKeyPressed;
                _keyboardService.KeyReleased += OnKeyReleased;

                _foregroundWindowHookService.ForegroundWindowChanged += OnForegroundWindowChanged;

                //_namedPipeService.CommandReceived += OnNamedPipeCommandReceived;

                // Start named pipe service
                // _logger.Debug("Starting named pipe service");
                //_namedPipeService.Start();

                // Kick off the auto-update background loop (no-op if disabled in prefs).
                _updateService.Initialize();

                _logger.Info("HintController initialized successfully");
            }
        }

        private void ApplyOptions()
        {
            _logger.Debug($"Applying options - ShowRectangles: {_options.ShowRectangles}, HintPosition: {_options.HintPosition}, Hotkey: {_options.Hotkey.Modifiers}+{_options.Hotkey.VirtualKey}");
            _overlay.ApplyShowRectangles(_options.ShowRectangles);
            _overlay.ApplyHintPosition(_options.HintPosition);

            if (_options.Hotkey.Enabled)
                _hotkeyWindow.RegisterGlobalHotkey(_options.Hotkey.Modifiers, _options.Hotkey.VirtualKey);
            else
                _hotkeyWindow.UnregisterGlobalHotkey();

            if (_options.TaskbarHotkey.Enabled)
                _hotkeyWindow.RegisterTaskbarHotkey(_options.TaskbarHotkey.Modifiers, _options.TaskbarHotkey.VirtualKey);
            else
                _hotkeyWindow.UnregisterTaskbarHotkey();

            if (_options.ScrollModeHotkey.Enabled)
                _hotkeyWindow.RegisterScrollHotkey(_options.ScrollModeHotkey.Modifiers, _options.ScrollModeHotkey.VirtualKey);
            else
                _hotkeyWindow.UnregisterScrollHotkey();

            _inputHandler.ApplyOptions(_options.ClickActionShortcuts);

            var rules = WindowRuleRegistry.MergeWithDefaults(_options.WindowRules);
            _ruleRegistry.SetRules(rules);
            _logger.Debug($"Window rules applied: {rules.Count} rule(s)");
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            _logger.Info("Display settings changed - deactivating hints and rebuilding overlays");
            _stateManager.Deactivate();
            _overlay.RebuildOverlays();
        }

        private void OnNamedPipeCommandReceived(object? sender, NamedPipeCommand command)
        {
            _logger.Debug($"Processing named pipe command: {command.CommandType}");

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
                if (IsToggleDebounced("Toggle"))
                    return;

                if (_stateManager.CurrentMode != HintMode.Inactive && _stateManager.CurrentFeatureMode == FeatureMode.RegularHinting)
                {
                    _logger.Info("Deactivating hint mode");
                    DeactivateCurrentMode();
                    return;
                }

                if (_stateManager.CurrentMode != HintMode.Inactive)
                {
                    _logger.Info($"Switching from {_stateManager.CurrentFeatureMode} to foreground window hints");
                    DeactivateCurrentMode();
                }

                _logger.Info("Activating hint mode (foreground window)");
                _stateManager.Activate(HintSource.ForegroundWindow, FeatureMode.RegularHinting);
                ScanForHints();
            }
        }

        public void ToggleTaskbar()
        {
            using (PerformanceMetrics.Start("ToggleTaskbar", _logger, LogLevel.Debug))
            {
                if (IsToggleDebounced("ToggleTaskbar"))
                    return;

                if (_stateManager.CurrentMode != HintMode.Inactive && _stateManager.CurrentFeatureMode == FeatureMode.TaskbarHinting)
                {
                    _logger.Info("Deactivating taskbar hints");
                    DeactivateCurrentMode();
                    return;
                }

                if (_stateManager.CurrentMode != HintMode.Inactive)
                {
                    _logger.Info($"Switching from {_stateManager.CurrentFeatureMode} to taskbar hints");
                    DeactivateCurrentMode();
                }

                _logger.Info("Activating taskbar hint mode");
                _stateManager.Activate(HintSource.Taskbar, FeatureMode.TaskbarHinting);
                ScanTaskbarForHints();
            }
        }

        public void ToggleScrollMode()
        {
            using (PerformanceMetrics.Start("ToggleScrollMode", _logger, LogLevel.Debug))
            {
                if (IsToggleDebounced("ToggleScrollMode"))
                    return;

                if (_stateManager.CurrentMode != HintMode.Inactive && _stateManager.CurrentFeatureMode == FeatureMode.Scrolling)
                {
                    _logger.Info("Deactivating scroll mode");
                    DeactivateCurrentMode();
                    return;
                }

                if (_stateManager.CurrentMode != HintMode.Inactive)
                {
                    _logger.Info($"Switching from {_stateManager.CurrentFeatureMode} to scroll mode");
                    DeactivateCurrentMode();
                }

                _logger.Info("Activating scroll mode");
                _scrollModeStateManager.Reset();
                _stateManager.Activate(HintSource.ForegroundWindow, FeatureMode.Scrolling);
                ScanForScrollableElements();
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

                // Store the window we're showing hints for (used for auto-hide on focus change)
                _activeHintWindowHwnd = hwnd;

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

                if (timedOut)
                {
                    _logger.Warning($"Hint population timed out after {timeoutMs}ms");
                    _stateManager.Deactivate();
                    _trayIcon.ShowNotification("Hint Timeout", $"Hint population timed out after {timeoutMs}ms. Try increasing the timeout in preferences.");
                    return;
                }
                else if (elements.Count == 0)
                {

                    _logger.Info("No clickable elements found, deactivating");
                    _stateManager.Deactivate();
                    return;
                }


                // Drop hints that fall outside the active window (with a 10% margin).
                // Popup-style elements (combo dropdowns, menus) are exempted by HWND.
                //elements = PerformanceMetricsExtensions.MeasureExecution(
                //    "ClampHintsToWindow",
                //    () => HintBoundsFilter.ClampToWindow(elements, hwnd, _logger),
                //    _logger,
                //    LogLevel.Debug);

                if (elements.Count == 0)
                {
                    _logger.Info("No clickable elements remain after window-bounds clamp, deactivating");
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
                        DisplayName = GetDisplayName(e.Element),
                        CurrentOpacity = 1.0f,
                        TargetOpacity = 1.0f
                    }).ToList(),
                    _logger,
                    LogLevel.Debug);

                _logger.Debug($"Created {hints.Count} hint items");
                _stateManager.SetHints(hints);
            }
        }

        private async void ScanForScrollableElements()
        {
            using (PerformanceMetrics.Start("ScanForScrollableElements", _logger, LogLevel.Info))
            {
                var hwnd = _windowManager.GetForegroundWindow();
                if (!_windowManager.IsWindowValid(hwnd))
                {
                    _logger.Warning("No valid foreground window found for scroll mode");
                    DeactivateCurrentMode();
                    return;
                }

                _logger.Debug($"Scanning window for scrollable elements: {hwnd}");
                _activeHintWindowHwnd = hwnd;
                _overlay.EnsureTopmost();

                IReadOnlyList<ScrollableElement> elements;
                var timeoutMs = _options.ScanTimeoutMs;
                var timedOut = false;
                if (timeoutMs > 0)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    elements = await _uiaService.FindScrollableElementsAsync(hwnd, timeoutMs);
                    sw.Stop();
                    timedOut = sw.ElapsedMilliseconds >= timeoutMs;
                    _logger.Info($"FindScrollableElements completed in {sw.ElapsedMilliseconds}ms (timeout={timeoutMs}ms)");
                }
                else
                {
                    elements = PerformanceMetricsExtensions.MeasureExecution(
                        "FindScrollableElements",
                        () => _uiaService.FindScrollableElements(hwnd),
                        _logger,
                        LogLevel.Info);
                }

                _logger.Info($"Found {elements.Count} scrollable elements");

                if (timedOut)
                {
                    _logger.Warning($"Scroll target discovery timed out after {timeoutMs}ms");
                    DeactivateCurrentMode();
                    _trayIcon.ShowNotification("Scroll Timeout", $"Scroll target discovery timed out after {timeoutMs}ms. Try increasing the timeout in preferences.");
                    return;
                }

                if (elements.Count == 0)
                {
                    _logger.Info("No scrollable elements found, deactivating scroll mode");
                    DeactivateCurrentMode();
                    return;
                }

                var labels = PerformanceMetricsExtensions.MeasureExecution(
                    "GenerateLabels(Scroll)",
                    () => LabelGenerator.Generate(elements.Count),
                    _logger,
                    LogLevel.Debug);

                var hints = PerformanceMetricsExtensions.MeasureExecution(
                    "CreateHintItems(Scroll)",
                    () => elements.Select((e, i) => new HintItem
                    {
                        Rect = e.Bounds,
                        Element = e.Element,
                        Label = labels[i],
                        DisplayName = e.Name,
                        CurrentOpacity = 1.0f,
                        TargetOpacity = 1.0f
                    }).ToList(),
                    _logger,
                    LogLevel.Debug);

                _logger.Debug($"Created {hints.Count} scroll target hint items");
                _stateManager.SetHints(hints);
            }
        }

        private async void ScanTaskbarForHints()
        {
            using (PerformanceMetrics.Start("ScanTaskbarForHints", _logger, LogLevel.Info))
            {
                var taskbarWindows = _windowManager.GetTaskbarWindows();
                if (taskbarWindows.Count == 0)
                {
                    _logger.Warning("No taskbar windows found");
                    _stateManager.Deactivate();
                    return;
                }

                _logger.Debug($"Scanning {taskbarWindows.Count} taskbar window(s)");

                // Ensure overlay is topmost before scanning
                _overlay.EnsureTopmost();

                var allElements = new List<Services.ClickableElement>();
                var timeoutMs = _options.ScanTimeoutMs;
                var anyTimedOut = false;

                foreach (var hwnd in taskbarWindows)
                {
                    if (!_windowManager.IsWindowValid(hwnd))
                    {
                        _logger.Warning($"Invalid taskbar window handle: {hwnd}");
                        continue;
                    }

                    _logger.Debug($"Scanning taskbar window: {hwnd}");

                    IReadOnlyList<Services.ClickableElement> elements;
                    var timedOut = false;
                    if (timeoutMs > 0)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        elements = await _uiaService.FindClickableElementsAsync(hwnd, timeoutMs);
                        sw.Stop();
                        timedOut = sw.ElapsedMilliseconds >= timeoutMs;
                        if (timedOut) anyTimedOut = true;
                        _logger.Debug($"FindClickableElements(Taskbar {hwnd}) completed in {sw.ElapsedMilliseconds}ms (timeout={timeoutMs}ms)");
                    }
                    else
                    {
                        elements = PerformanceMetricsExtensions.MeasureExecution(
                            $"FindClickableElements(Taskbar {hwnd})",
                            () => _uiaService.FindClickableElements(hwnd),
                            _logger,
                            LogLevel.Debug);
                    }

                    // Diagnostic: per-HWND element count + sample bounds. A secondary
                    // taskbar returning 0 here points at the UIA-empty root cause;
                    // returning elements points at a render-time filter/coordinate issue.
                    _logger.Debug($"Taskbar {hwnd}: FindClickableElements returned {elements.Count} element(s)");

                    int sampleCount = Math.Min(elements.Count, 5);
                    for (int i = 0; i < sampleCount; i++)
                    {
                        var b = elements[i].Bounds;
                        _logger.Debug($"  Taskbar {hwnd} element[{i}] bounds=({b.Left},{b.Top}) {b.Width}x{b.Height}");
                    }

                    allElements.AddRange(elements);
                }

                _logger.Info($"Found {allElements.Count} total taskbar clickable elements across {taskbarWindows.Count} taskbar(s)");

                if (allElements.Count == 0)
                {
                    if (anyTimedOut)
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
                    () => ElementDeduplicator.Deduplicate(allElements, _logger, _options.OverlapThreshold),
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
                        DisplayName = GetDisplayName(e.Element),
                        CurrentOpacity = 1.0f,
                        TargetOpacity = 1.0f
                    }).ToList(),
                    _logger,
                    LogLevel.Debug);

                _logger.Debug($"Created {hints.Count} taskbar hint items");
                _stateManager.SetHints(hints);
            }
        }

        private void OnAutoHideTick(object? state)
        {
            // Timer callback runs on a threadpool thread; marshal to the UI thread.
            if (_disposed || !_overlay.IsHandleCreated) return;
            try
            {
                _overlay.BeginInvoke(() =>
                {
                    if (_disposed) return;
                    if (_stateManager.CurrentMode == HintMode.Inactive) return;
                    _logger.Info("Auto-hide timeout reached, deactivating hints");
                    DeactivateCurrentMode();
                });
            }
            catch (ObjectDisposedException)
            {
                // Shutdown race; ignore.
            }
            catch (InvalidOperationException)
            {
                // Handle was destroyed between the check and BeginInvoke.
            }
        }

        private void OnModeChanged(object? sender, HintMode mode)
        {
            _logger.Info($"Mode changed: {mode}");

            bool enabled = mode != HintMode.Inactive;
            _overlay.SetEnabled(enabled);
            _overlay.SetActiveState(enabled);
            _overlay.SetFeatureMode(_stateManager.CurrentFeatureMode);
            _trayIcon.SetStatus(mode);

            if (enabled)
            {
                _logger.Debug("Starting keyboard service");
                _keyboardService.Start();

                // Start monitoring foreground window changes (only for foreground window hints)
                if (_stateManager.CurrentSource == HintSource.ForegroundWindow)
                {
                    _foregroundWindowHookService.Start();
                    _logger.Debug("Started foreground window hook for auto-hide");
                }

                if (mode == HintMode.Active && _stateManager.CurrentFeatureMode != FeatureMode.Scrolling && _options.AutoHideTimeoutSeconds > 0)
                {
                    _autoHideTimer.Change(_options.AutoHideTimeoutSeconds * 1000, Timeout.Infinite);
                    _logger.Debug($"Auto-hide timer started ({_options.AutoHideTimeoutSeconds}s)");
                }
            }
            else
            {
                _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _foregroundWindowHookService.Stop();
                _activeHintWindowHwnd = IntPtr.Zero;
                _scrollModeStateManager.Reset();
                _logger.Debug("Stopping keyboard service");
                _keyboardService.Stop();
                _inputHandler.Reset();
                _overlay.SetScrollControlState(false, null);
                _trayIcon.ResetIcon();
            }
        }

        private void OnFeatureModeChanged(object? sender, FeatureMode featureMode)
        {
            _logger.Debug($"Feature mode changed: {featureMode}");
            _overlay.SetFeatureMode(featureMode);
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

        private void OnScrollSelectedTargetChanged(object? sender, HintItem? selectedTarget)
        {
            bool isControlling = selectedTarget != null;
            string? targetName = selectedTarget?.DisplayName;
            _overlay.SetScrollControlState(isControlling, targetName);

            foreach (var hint in _stateManager.CurrentHints)
            {
                hint.IsSelected = selectedTarget != null && ReferenceEquals(hint, selectedTarget);

                if (_stateManager.CurrentFeatureMode == FeatureMode.Scrolling)
                {
                    hint.TargetOpacity = selectedTarget == null
                        ? 1.0f
                        : hint.IsSelected ? 1.0f : 0.3f;
                }
            }

            _overlay.SetHints(_stateManager.CurrentHints.ToList());
        }

        private void OnForegroundWindowChanged(object? sender, NativeInterop.ForegroundWindowChangedEventArgs e)
        {
            // Only auto-hide if hints are showing for a foreground window
            if (_stateManager.CurrentMode == HintMode.Inactive)
                return;

            if (_stateManager.CurrentSource != HintSource.ForegroundWindow)
                return;

            if (_stateManager.CurrentFeatureMode == FeatureMode.Scrolling)
                return;

            // Check if the new foreground window is different from the one we're showing hints for
            IntPtr newHwnd = e.NewForegroundWindow;
            if (newHwnd != _activeHintWindowHwnd && _activeHintWindowHwnd != IntPtr.Zero)
            {
                _logger.Info($"Foreground window changed from 0x{_activeHintWindowHwnd.ToInt64():X} to 0x{newHwnd.ToInt64():X}, auto-hiding hints");
                DeactivateCurrentMode();
            }
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

            bool scrollHotkeyMatches = _options.ScrollModeHotkey.Enabled &&
                                       e.VirtualKeyCode == _options.ScrollModeHotkey.VirtualKey &&
                                       CheckModifiersMatch(_options.ScrollModeHotkey.Modifiers, actualMods);

            if (scrollHotkeyMatches)
            {
                _logger.Debug("Scroll hotkey pressed, not consuming");
                return;
            }

            bool handled;
            if (_stateManager.CurrentFeatureMode == FeatureMode.Scrolling)
            {
                handled = ProcessScrollModeKeyDown(e.VirtualKeyCode, e.Modifiers);
            }
            else
            {
                handled = _inputHandler.ProcessKeyDown(e.VirtualKeyCode, e.Modifiers);
            }

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

                // Capture values for the closure before deactivating hints
                var element = match.Element;
                var rect = match.Rect;
                var action = e.Action;

                // Hide hints immediately so the overlay is gone before activation
                _logger.Debug("Deactivating hints before element activation");
                _stateManager.Deactivate();

                // Defer activation to the message loop so it does not run inside
                // the low-level keyboard hook callback.  InvokePattern.Invoke() on
                // same-process UI elements requires the UI thread message pump,
                // which is blocked while the hook callback is executing — causing a
                // deadlock.  BeginInvoke posts the work to the message queue and
                // returns immediately, letting the hook callback complete first.
                _overlay.BeginInvoke(() =>
                {
                    try
                    {
                        if (action == ClickAction.Default)
                        {
                            _activatorChain.TryActivate(element);
                        }
                        else
                        {
                            _mouseClickService.PerformClick(rect, action);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("Error activating element", ex);
                    }
                });
            }
        }

        private void OnPreferencesRequested(object? sender, EventArgs e)
        {
            using (PerformanceMetrics.Start("ShowPreferencesDialog", _logger, LogLevel.Info))
            {
                _logger.Info("Opening preferences dialog");
                var dialog = new PreferencesDialog(_options, _startupService);
                dialog.HotkeyRecordingStarted += (_, _) =>
                {
                    _logger.Debug("Hotkey recording started, unregistering global hotkeys");
                    _hotkeyWindow.UnregisterGlobalHotkey();
                    _hotkeyWindow.UnregisterTaskbarHotkey();
                    _hotkeyWindow.UnregisterScrollHotkey();
                };
                dialog.HotkeyRecordingStopped += (_, _) =>
                {
                    _logger.Debug("Hotkey recording stopped, re-registering global hotkeys");
                    if (_options.Hotkey.Enabled)
                        _hotkeyWindow.RegisterGlobalHotkey(_options.Hotkey.Modifiers, _options.Hotkey.VirtualKey);
                    if (_options.TaskbarHotkey.Enabled)
                        _hotkeyWindow.RegisterTaskbarHotkey(_options.TaskbarHotkey.Modifiers, _options.TaskbarHotkey.VirtualKey);
                    if (_options.ScrollModeHotkey.Enabled)
                        _hotkeyWindow.RegisterScrollHotkey(_options.ScrollModeHotkey.Modifiers, _options.ScrollModeHotkey.VirtualKey);
                };
                var previousPosition = _overlay.HintPosition;
                dialog.HintPositionChanged += (_, newPos) =>
                {
                    _logger.Debug($"Live preview: hint position changed to {newPos}");
                    _overlay.ApplyHintPosition(newPos);
                };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _logger.Info("Preferences saved, reloading and applying");
                    // Reload and apply
                    _options = _preferencesService.Load();
                    ApplyOptions();
                }
                else
                {
                    _logger.Debug("Preferences dialog cancelled, reverting hint position");
                    _overlay.ApplyHintPosition(previousPosition);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _logger.Info("Disposing HintController");
            _autoHideTimer.Dispose();
            //_namedPipeService.Dispose();
            _keyboardService.Stop();
            _updateService.Dispose();
            _trayIcon.Dispose();
            _overlay.Dispose();
            _hotkeyWindow.Dispose();
            _uiaService.Dispose();
            _logger.Info("HintController disposed");

            _disposed = true;
        }

        private bool IsToggleDebounced(string operationName)
        {
            long now = Stopwatch.GetTimestamp();
            long elapsedMs = (now - _lastToggleTicks) * 1000 / Stopwatch.Frequency;

            if (elapsedMs < ToggleDebounceMs)
            {
                _logger.Debug($"{operationName} debounced - only {elapsedMs}ms since last toggle");
                return true;
            }

            _lastToggleTicks = now;
            return false;
        }

        private void DeactivateCurrentMode()
        {
            if (_stateManager.CurrentFeatureMode == FeatureMode.Scrolling)
            {
                _scrollModeStateManager.Reset();
            }

            _stateManager.Deactivate();
        }

        private bool ProcessScrollModeKeyDown(int vkCode, KeyModifiers modifiers)
        {
            bool shiftHeld = (modifiers & KeyModifiers.Shift) != 0;
            bool ctrlHeld = (modifiers & KeyModifiers.Control) != 0;
            bool altHeld = (modifiers & KeyModifiers.Alt) != 0;

            if (shiftHeld || ctrlHeld || altHeld)
                return false;

            if (_scrollModeStateManager.CurrentPhase == ScrollPhase.Selecting)
            {
                return ProcessScrollSelectionKeyDown(vkCode);
            }

            return ProcessScrollControlKeyDown(vkCode);
        }

        private bool ProcessScrollSelectionKeyDown(int vkCode)
        {
            if (vkCode >= 0x41 && vkCode <= 0x5A)
            {
                char c = (char)vkCode;
                var candidate = _stateManager.FilterText + c;

                if (!_stateManager.HasMatchingHint(candidate))
                {
                    System.Media.SystemSounds.Beep.Play();
                    return true;
                }

                _stateManager.AppendToFilter(c);
                return true;
            }

            if (vkCode == 0x08)
            {
                _stateManager.RemoveLastFilterChar();
                return true;
            }

            if (vkCode == 0x1B)
            {
                if (!string.IsNullOrEmpty(_stateManager.FilterText))
                {
                    _stateManager.ClearFilter();
                }
                else
                {
                    DeactivateCurrentMode();
                }

                return true;
            }

            if (vkCode == 0x20 || vkCode == 0x0D)
            {
                var match = _stateManager.GetExactMatch();
                if (match == null)
                {
                    System.Media.SystemSounds.Beep.Play();
                    return true;
                }

                _scrollModeStateManager.SelectTarget(match);
                _scrollModeStateManager.ClearPercentBuffer();
                _stateManager.ClearFilter();
                return true;
            }

            return false;
        }

        private bool ProcessScrollControlKeyDown(int vkCode)
        {
            switch (vkCode)
            {
                case 0x1B: // Escape
                    _scrollModeStateManager.DeselectTarget();
                    _stateManager.ClearFilter();
                    return true;

                case 0x08: // Backspace
                    _scrollModeStateManager.RemoveLastPercentChar();
                    return true;

                case 0x26: // Up
                    QueueScrollCommand(ScrollCommand.LineUp);
                    return true;

                case 0x28: // Down
                    QueueScrollCommand(ScrollCommand.LineDown);
                    return true;

                case 0x25: // Left
                    QueueScrollCommand(ScrollCommand.LineLeft);
                    return true;

                case 0x27: // Right
                    QueueScrollCommand(ScrollCommand.LineRight);
                    return true;

                case 0x21: // Page Up
                    QueueScrollCommand(ScrollCommand.PageUp);
                    return true;

                case 0x22: // Page Down
                    QueueScrollCommand(ScrollCommand.PageDown);
                    return true;

                case 0x24: // Home
                    QueueAbsoluteScroll(isStart: true);
                    return true;

                case 0x23: // End
                    QueueAbsoluteScroll(isStart: false);
                    return true;

                case 0x4D: // M
                    QueueScrollCommand(ScrollCommand.Middle);
                    return true;

                case 0x20: // Space
                case 0x0D: // Enter
                    return ExecuteBufferedPercentScroll();
            }

            if (vkCode >= 0x30 && vkCode <= 0x39)
            {
                _scrollModeStateManager.AppendToPercentBuffer((char)vkCode);
                return true;
            }

            if (vkCode >= 0x60 && vkCode <= 0x69)
            {
                _scrollModeStateManager.AppendToPercentBuffer((char)('0' + (vkCode - 0x60)));
                return true;
            }

            return false;
        }

        private bool ExecuteBufferedPercentScroll()
        {
            var percent = _scrollModeStateManager.GetPercentValue();
            if (!percent.HasValue)
            {
                if (!string.IsNullOrEmpty(_scrollModeStateManager.PercentBuffer))
                {
                    System.Media.SystemSounds.Beep.Play();
                }

                return !string.IsNullOrEmpty(_scrollModeStateManager.PercentBuffer);
            }

            var command = GetPercentScrollCommand();
            _scrollModeStateManager.ClearPercentBuffer();
            QueueScrollCommand(command, percent.Value);
            return true;
        }

        private void QueueAbsoluteScroll(bool isStart)
        {
            var target = TryBuildSelectedScrollableElement();
            if (target != null && !target.IsVerticallyScrollable && target.IsHorizontallyScrollable)
            {
                QueueScrollCommand(ScrollCommand.PercentHorizontal, isStart ? 0 : 100);
                return;
            }

            QueueScrollCommand(isStart ? ScrollCommand.Top : ScrollCommand.Bottom);
        }

        private ScrollCommand GetPercentScrollCommand()
        {
            var target = TryBuildSelectedScrollableElement();
            if (target != null && !target.IsVerticallyScrollable && target.IsHorizontallyScrollable)
            {
                return ScrollCommand.PercentHorizontal;
            }

            return ScrollCommand.PercentVertical;
        }

        private void QueueScrollCommand(ScrollCommand command, int? percentValue = null)
        {
            var target = TryBuildSelectedScrollableElement();
            if (target == null)
            {
                _logger.Warning("No selected scroll target available for scroll command");
                System.Media.SystemSounds.Beep.Play();
                _scrollModeStateManager.DeselectTarget();
                return;
            }

            _overlay.BeginInvoke(() =>
            {
                bool success = _scrollController.ExecuteScrollCommand(target, command, percentValue);
                if (!success)
                {
                    _logger.Warning($"Scroll command {command} failed; returning to target selection");
                    System.Media.SystemSounds.Beep.Play();
                    _scrollModeStateManager.DeselectTarget();
                }
            });
        }

        private ScrollableElement? TryBuildSelectedScrollableElement()
        {
            var selected = _scrollModeStateManager.SelectedTarget;
            if (selected?.Element == null)
            {
                return null;
            }

            try
            {
                return new ScrollableElement
                {
                    Element = selected.Element,
                    Bounds = selected.Rect,
                    Name = GetCachedStringProperty(selected.Element, UIA_PropertyIds.UIA_NamePropertyId),
                    ControlType = GetCachedIntProperty(selected.Element, UIA_PropertyIds.UIA_ControlTypePropertyId),
                    HasScrollPattern = GetCachedBoolProperty(selected.Element, UIA_PropertyIds.UIA_IsScrollPatternAvailablePropertyId),
                    HasRangeValuePattern = GetCachedBoolProperty(selected.Element, UIA_PropertyIds.UIA_IsRangeValuePatternAvailablePropertyId),
                    IsHorizontallyScrollable = GetCachedBoolProperty(selected.Element, UIA_PropertyIds.UIA_ScrollHorizontallyScrollablePropertyId),
                    IsVerticallyScrollable = GetCachedBoolProperty(selected.Element, UIA_PropertyIds.UIA_ScrollVerticallyScrollablePropertyId)
                };
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to rebuild selected scroll target: {ex.Message}");
                return null;
            }
        }

        private static bool GetCachedBoolProperty(IUIAutomationElement element, int propertyId)
        {
            return element.GetCachedPropertyValue(propertyId) is bool value && value;
        }

        private static int GetCachedIntProperty(IUIAutomationElement element, int propertyId)
        {
            return element.GetCachedPropertyValue(propertyId) is int value ? value : 0;
        }

        private static string GetCachedStringProperty(IUIAutomationElement element, int propertyId)
        {
            return element.GetCachedPropertyValue(propertyId) as string ?? string.Empty;
        }

        private static string GetDisplayName(IUIAutomationElement element)
        {
            return GetCachedStringProperty(element, UIA_PropertyIds.UIA_NamePropertyId);
        }
    }
}
