using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Preferences;
using UIAutomationClient;
using Windows.Win32;
using Windows.Win32.Foundation;
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
        private readonly IPreferencesService _preferencesService;
        private readonly IWindowManager _windowManager;
        private readonly ILogger _logger;
        private readonly HintStateManager _stateManager;
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
        private bool _disposed;

        // ─── PROTOTYPE #73: chained hinting ─────────────────────────────────
        // Throwaway spike (branch prototype/chained-hinting). After an activation,
        // re-scan the foreground window and re-show hints instead of just
        // deactivating — looping until Esc / foreground-leaves-app / zero elements.
        //
        // Settle-detection journey (all confirmed live on Settings):
        //  A. Fixed delay — brittle: the scan raced the pane's settle → partial tree.
        //  B. UIA StructureChanged gate (debounce to a quiet window) — ALSO missed
        //     elements: on Settings' XAML the SC stream goes silent BEFORE the tree
        //     finishes materializing (a fresh scan ~1-2s later sees everything).
        //     "SC quiet" != "tree complete" — the #48/#49 virtualization warning.
        //  C. POLL-UNTIL-STABLE — a build *pause* looked identical to *done* → false-
        //     stabled on a partial tree (highly timing-dependent on big list pages).
        //  D (this): PROGRESSIVE-RECONCILE (per #76) — stop guessing "done"; show hints
        //     immediately (even partial) and ADD late-arriving elements as the tree
        //     fills, append-only so nothing shifts. Mirrors macOS Voice Control's live
        //     "show numbers continuously" overlay. Also confirmed dead: AsyncContentLoaded
        //     never fires on WinUI pane swaps (#76 + the in-app probe).
        // Remove for production.
        private const bool ChainingEnabled = true;
        private const int ChainPollIntervalMs = 150;      // gap between reconcile scans
        private const int ChainReconcileMaxAttempts = 20; // ~up to 3s ceiling of reconciling
        private const int ChainReconcileStableStreak = 4; // stop after this many scans add nothing (~600ms)
        private const int ChainReconcileLabelPool = 700;  // stable append-only label sequence per round
        private const int MenuSettleMaxAttempts = 8;      // PROTOTYPE #77 change #3: ~1.2s at 150ms
        private const int MenuSettlePollMs = 150;
        private const int MenuSettleStableStreak = 2;     // 2 stable polls (~300ms) after items appear
        private const int MenuWatchPollMs = 200;          // PROTOTYPE #77 change #4: watch cadence after settle
        private const int MenuWatchMaxEmpties = 3;        // consecutive empty scans → menu gone (missing-MenuClosed backstop)
        private const int MenuEventDebounceMs = 120;      // PROTOTYPE #77 change #5: coalesce event bursts before a rescan
        private const int MenuCommitGraceMs = 1500;       // honor a MenuOpened within this window after ANY commit
        private int _chainGeneration;                  // guards against stale / superseded rounds
        private uint _chainOriginPid;                  // foreground process when the chain round began

        // PROTOTYPE #77: menu-aware chaining. A submenu/popup opens in a SEPARATE HWND, so the
        // foreground-window reconcile misses it (looks like a terminal click). Subscribe globally
        // to UIA MenuOpened/MenuClosed; while chaining is active, hint the popup on open and
        // resume the foreground reconcile on close. Probe-instrumented (logs what actually fires
        // for the Word ribbon — #45 warns UIA menu events are unreliable for some surfaces).
        private const int UIA_MenuOpenedEventId = 20003;
        private const int UIA_MenuClosedEventId = 20007;
        private readonly IUIAutomation _menuUia;        // dedicated client for global menu events
        private ChainMenuHandler? _menuHandler;
        private bool _menuActive;                        // a menu popup is currently hinted
        // PROTOTYPE #77 (change #2): a chained-hinting session is live. Stays true across the
        // transient commit→reactivate gap (when mode blips Inactive), so a menu opened by
        // activating a hint isn't dropped by the old CurrentMode==Inactive guard. Cleared on a
        // TERMINAL exit (Esc / manual deactivate / foreground-left / zero-scannable) — never on
        // the commit gap, which sets _suppressChainClearOnce for the next Inactive transition.
        private bool _chainActive;
        private bool _suppressChainClearOnce;
        // PROTOTYPE #77 (change #3): menu-scoped progressive settle state.
        private int _menuGeneration;                                       // supersedes prior settle loops (multi-fire)
        private readonly Dictionary<string, string> _menuLabels = new();   // RuntimeId → label, stable across a menu session
        private int _menuNextLabel;
        private string[] _menuLabelPool = System.Array.Empty<string>();
        // PROTOTYPE #77 (change #7): WINDOW-SCOPE event-driven menu watch. Subscribe on the whole
        // foreground window (where VS actually raises StructureChanged/ExpandCollapse — change #6
        // measured element-scope as silent) and drive debounced rescans of the menu root + the event
        // sender. Per-second counts logged for visibility; Bounds counted only (firehose-prone).
        private PropertyChangedHandler? _menuPropHandler;
        private IUIAutomationElement? _watchedMenu;                         // subscription target (the window; for removal)
        private IUIAutomationElement? _menuRoot;                            // menu element — primary scan root
        private IUIAutomationElement? _menuAuxRoot;                         // last expanded event sender — aux scan root
        private bool _menuSettled;                                         // initial settle done → events may drive rescans
        private string _menuLastSig = "";                                  // last shown RuntimeId signature (skip redundant SetHints)
        private DateTime _lastCommitUtc = DateTime.MinValue;               // robust menu-gate: a commit was just made
        private System.Threading.Timer? _menuEvtTimer;                     // ~120ms coalescing tick (rescan-if-dirty + count flush)
        private long _mecExpand;                                           // per-second ExpandCollapse(MenuItem) counter (Interlocked)
        private int _menuEvtDetailBudget;                                  // remaining detailed sample logs
        private int _menuDirty;                                            // set by events (UIA thread), consumed by the tick
        private int _menuEvtTick;                                          // tick counter for the 1s count-flush cadence
        private IUIAutomationElement? _pendingExpandRoot;                  // last expanded MenuItem (UIA thread → tick)
        private readonly object _menuEvtLock = new object();
        // ────────────────────────────────────────────────────────────────────

        public HintController(
            OverlayManager overlay,
            HotkeyWindow hotkeyWindow,
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
                _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
                _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
                _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
                _ruleRegistry = ruleRegistry ?? throw new ArgumentNullException(nameof(ruleRegistry));

                _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
                _inputHandler = inputHandler ?? throw new ArgumentNullException(nameof(inputHandler));
                _activatorChain = activatorChain ?? throw new ArgumentNullException(nameof(activatorChain));
                //_namedPipeService = namedPipeService ?? throw new ArgumentNullException(nameof(namedPipeService));
                _mouseClickService = mouseClickService ?? throw new ArgumentNullException(nameof(mouseClickService));
                _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
                _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));

                // Auto-hide timer (threadpool-based; see field comment).
                _autoHideTimer = new System.Threading.Timer(OnAutoHideTick, null, Timeout.Infinite, Timeout.Infinite);

                // PROTOTYPE #77: global UIA menu-event client for menu-aware chaining.
                _menuUia = new CUIAutomation();


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
                _hotkeyWindow.DisplaySettingsChanged += OnDisplaySettingsChanged;
                _trayIcon.ToggleRequested += (s, e) => Toggle();
                _trayIcon.PreferencesRequested += OnPreferencesRequested;
                _trayIcon.ExitRequested += (s, e) => Application.Exit();
                _trayIcon.CheckForUpdatesRequested += async (s, e) =>
                    await _updateService.CheckForUpdatesManuallyAsync().ConfigureAwait(true);

                _stateManager.ModeChanged += OnModeChanged;
                _stateManager.HintsChanged += OnHintsChanged;
                _stateManager.FilterChanged += OnFilterChanged;
                _stateManager.ClickActionChanged += OnClickActionChanged;

                _inputHandler.SelectionCommitted += OnSelectionCommitted;

                _keyboardService.KeyPressed += OnKeyPressed;
                _keyboardService.KeyReleased += OnKeyReleased;

                //_namedPipeService.CommandReceived += OnNamedPipeCommandReceived;

                // Start named pipe service
                // _logger.Debug("Starting named pipe service");
                //_namedPipeService.Start();

                // Kick off the auto-update background loop (no-op if disabled in prefs).
                _updateService.Initialize();

                // PROTOTYPE #77: start listening for menu open/close globally.
                SubscribeMenuEvents();

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
                    _stateManager.Deactivate();
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
            _trayIcon.SetStatus(mode);

            if (enabled)
            {
                _logger.Debug("Starting keyboard service");
                _keyboardService.Start();

                // PROTOTYPE #77 (change #2): a foreground-hint session is live from the moment
                // hints show (Toggle or reconcile) — so a menu opened before the first commit is
                // caught too. Taskbar hints don't chain, so they don't arm it.
                if (mode == HintMode.Active && _stateManager.CurrentSource == HintSource.ForegroundWindow)
                {
                    _chainActive = true;
                    // PROTOTYPE #77 (change #7): subscribe the window event watch once per session, on
                    // the current (main) foreground window — where VS raises menu ExpandCollapse/Structure.
                    SubscribeWindowWatch();
                }

                // PROTOTYPE #77 (change #4): auto-hide contradicts always-on-until-Esc chained mode
                // — it was firing mid-menu-watch and killing the hints. Suppress it while a chain
                // session is live; keep it for plain (e.g. taskbar) hints.
                if (mode == HintMode.Active && _options.AutoHideTimeoutSeconds > 0 && !_chainActive && !_menuActive)
                {
                    _autoHideTimer.Change(_options.AutoHideTimeoutSeconds * 1000, Timeout.Infinite);
                    _logger.Debug($"Auto-hide timer started ({_options.AutoHideTimeoutSeconds}s)");
                }
                else if (_chainActive || _menuActive)
                {
                    _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite); // cancel any pending auto-hide
                }
            }
            else
            {
                // PROTOTYPE #77 diagnostic: a menu is being watched but something deactivated us.
                // Capture the caller so we can identify the silent Deactivate path.
                if (_menuActive)
                    _logger.Warning($"[MENU] UNEXPECTED deactivate while watching a menu — stack:\n{Environment.StackTrace}");

                // PROTOTYPE #77 (change #2): a terminal deactivate ends the chain session; the
                // transient commit→reactivate gap does not (it set _suppressChainClearOnce).
                if (_suppressChainClearOnce)
                {
                    _suppressChainClearOnce = false;
                }
                else
                {
                    _chainActive = false;
                    _menuActive = false;
                    UnsubscribeWindowWatch(); // PROTOTYPE #77 change #7: stop the event watch on terminal exit
                }

                _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
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

                // PROTOTYPE #77 (change #5): robust menu gate — record that a commit just happened,
                // so a MenuOpened arriving in the brief commit→reactivate gap is honored even if the
                // mode/source flags read inconsistently. Replaces the fragile _chainActive gate.
                _lastCommitUtc = DateTime.UtcNow;

                // Capture values for the closure before deactivating hints
                var element = match.Element;
                var rect = match.Rect;
                var action = e.Action;

                // PROTOTYPE #73: decide whether to chain BEFORE Deactivate() wipes the
                // source. Chaining only applies to foreground-window hints (not taskbar).
                bool chainThisRound = ChainingEnabled && _stateManager.CurrentSource == HintSource.ForegroundWindow;
                if (chainThisRound)
                {
                    _chainOriginPid = GetForegroundPid();
                    int gen = ++_chainGeneration;
                    _chainActive = true;             // PROTOTYPE #77: session live across the commit gap
                    _menuActive = false;             // stale menu (if any) is gone once we commit again
                    _logger.Info($"[CHAIN] commit → poll-until-stable will start after activation (gen={gen}, originPid={_chainOriginPid})");
                }

                // PROTOTYPE #77 (change #2 fix): a commit is NEVER terminal in always-on chained
                // mode — the chain continues via the reconcile OR the next MenuOpened. So the
                // imminent Deactivate must not clear _chainActive, EVEN when chainThisRound is false
                // (e.g. a menu-item commit whose source read as non-foreground). Without this, such
                // a commit cleared the session and the very next menu-open was silently ignored.
                // Only genuine terminals (Esc / foreground-left / zero-scannable) clear _chainActive.
                if (_chainActive)
                    _suppressChainClearOnce = true;

                // Hide hints immediately so the overlay is gone before activation
                // (also prevents a stale-hint flash before the next round paints).
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
                    finally
                    {
                        // PROTOTYPE #73/D: start progressive-reconcile AFTER activation is posted.
                        if (chainThisRound)
                            ChainReScanReconcile(_chainGeneration);
                    }
                });
            }
        }

        // PROTOTYPE #73: foreground window's process id (0 if none/invalid).
        private uint GetForegroundPid()
        {
            var hwnd = _windowManager.GetForegroundWindow();
            if (!_windowManager.IsWindowValid(hwnd))
                return 0;
            PInvoke.GetWindowThreadProcessId((HWND)hwnd, out uint pid);
            return pid;
        }

        // PROTOTYPE #77 diagnostic: foreground process name, for tagging menu-watch logs per app.
        private string ForegroundExe()
        {
            try
            {
                var pid = GetForegroundPid();
                if (pid == 0) return "?";
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                return p.ProcessName;
            }
            catch { return "?"; }
        }

        // PROTOTYPE #73/D: PROGRESSIVE-RECONCILE settle (per #76). Runs on the UI thread
        // (async void from the activation closure; awaits resume on the WinForms sync context).
        //
        // We stop trying to detect a single "done" moment — Settings' progressive XAML has no
        // reliable local signal for it (fixed-delay / StructureChanged-quiet / poll-until-stable
        // and AsyncContentLoaded all failed; #76). Instead: scan on a short interval, SHOW hints
        // as soon as any appear (even partial), and ADDITIVELY add late-arriving elements as the
        // tree fills — append-only labels (keyed by bounds) so existing hints never shift.
        // Keep going until the element set stops growing for a stability streak, or a budget cap.
        // A late element is never permanently missing; it just gets its hint a beat later.
        //
        // Exit conditions:
        //   • Esc / superseded → only a commit starts a round; Esc's Deactivate + the generation
        //                        guard end it. (Esc during the first ~150ms gap is a known gap.)
        //   • user typing      → stop reconciling once a filter is entered, so labels don't shift
        //                        under an in-progress selection.
        //   • foreground left  → origin-PID check each iteration.
        //   • zero elements    → persistently empty (terminal click).
        private async void ChainReScanReconcile(int gen)
        {
            var labelByRid = new Dictionary<string, string>();      // RuntimeId → stable label (persists this round)
            var labelPool = LabelGenerator.Generate(ChainReconcileLabelPool);
            int nextLabel = 0;
            string prevSig = "";                                    // last shown RuntimeId-set signature
            int stableStreak = 0;
            bool activated = false;
            try
            {
                for (int attempt = 1; attempt <= ChainReconcileMaxAttempts; attempt++)
                {
                    await System.Threading.Tasks.Task.Delay(ChainPollIntervalMs).ConfigureAwait(true);
                    if (_disposed || gen != _chainGeneration) return;

                    // Don't shift labels under an in-progress selection.
                    if (!string.IsNullOrEmpty(_stateManager.FilterText))
                    {
                        _logger.Info($"[CHAIN] reconcile stop — user is typing a filter (gen={gen})");
                        return;
                    }

                    uint nowPid = GetForegroundPid();
                    if (nowPid == 0 || nowPid != _chainOriginPid)
                    {
                        _logger.Info($"[CHAIN] exit — foreground left app (originPid={_chainOriginPid}, nowPid={nowPid})");
                        _chainActive = false; _menuActive = false; // PROTOTYPE #77: terminal
                        return;
                    }

                    var hwnd = _windowManager.GetForegroundWindow();
                    if (!_windowManager.IsWindowValid(hwnd)) continue;

                    IReadOnlyList<Services.ClickableElement> elements;
                    try
                    {
                        var timeoutMs = _options.ScanTimeoutMs;
                        elements = timeoutMs > 0
                            ? await _uiaService.FindClickableElementsAsync(hwnd, timeoutMs).ConfigureAwait(true)
                            : _uiaService.FindClickableElements(hwnd);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"[CHAIN] reconcile poll #{attempt} scan failed: {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }
                    if (_disposed || gen != _chainGeneration) return;

                    // Reconcile to THIS scan: identity = UIA RuntimeId (not bounds — bounds
                    // jitter created duplicates and bounds collisions dropped distinct elements).
                    // Keep a survivor's label stable; assign new labels to new RuntimeIds; drop
                    // whatever isn't in the current scan (so old-pane hints don't linger); use the
                    // fresh element ref from this scan (early refs on a virtualized tree go stale).
                    var hints = new List<HintItem>(elements.Count);
                    var seen = new HashSet<string>();
                    foreach (var e in elements)
                    {
                        string? rid = RuntimeIdKey(e.Element);
                        if (rid == null || !seen.Add(rid)) continue; // unidentifiable or dup within this scan
                        if (!labelByRid.TryGetValue(rid, out var label))
                        {
                            if (nextLabel >= labelPool.Length) continue; // pool exhausted (very large page)
                            label = labelPool[nextLabel++];
                            labelByRid[rid] = label;
                        }
                        hints.Add(new HintItem
                        {
                            Rect = e.Bounds,
                            Element = e.Element,
                            Label = label,
                            CurrentOpacity = 1.0f,
                            TargetOpacity = 1.0f
                        });
                    }

                    if (hints.Count == 0)
                    {
                        if (attempt >= 3)
                        {
                            _logger.Info($"[CHAIN] exit — zero scannable elements (terminal click, gen={gen})");
                            _chainActive = false; _menuActive = false; // PROTOTYPE #77: terminal
                            return;
                        }
                        continue;
                    }

                    string sig = string.Join("|", seen.OrderBy(x => x, StringComparer.Ordinal));
                    bool changed = sig != prevSig;
                    prevSig = sig;

                    // PROTOTYPE #77 (change #2): a menu is showing — yield, don't clobber it with
                    // a foreground scan. The menu path superseded us via ++_chainGeneration, but
                    // this guards the narrow window where a poll already passed the gen check.
                    if (_menuActive)
                    {
                        _logger.Info($"[CHAIN] reconcile yielding — menu is open (gen={gen})");
                        return;
                    }

                    if (!activated) { _stateManager.Activate(HintSource.ForegroundWindow); activated = true; }
                    _overlay.EnsureTopmost();
                    _stateManager.SetHints(hints); // always the latest clean set, stable labels

                    _logger.Debug($"[CHAIN] reconcile poll #{attempt} (gen={gen}) — scan={elements.Count}, shown={hints.Count}, changed={changed}");

                    if (!changed)
                    {
                        if (++stableStreak >= ChainReconcileStableStreak)
                        {
                            _logger.Info($"[CHAIN] reconcile settled at {hints.Count} elements after {attempt} polls (gen={gen})");
                            return;
                        }
                    }
                    else
                    {
                        stableStreak = 0;
                    }
                }
                _logger.Info($"[CHAIN] reconcile budget exhausted (gen={gen})");
            }
            catch (Exception ex)
            {
                _logger.Error("[CHAIN] reconcile loop error", ex);
            }
        }

        // PROTOTYPE #73/D: stable identity for an element across reconcile scans.
        private static string? RuntimeIdKey(IUIAutomationElement element)
        {
            try
            {
                var rid = element.GetRuntimeId();
                return rid == null ? null : string.Join(".", rid.Cast<int>());
            }
            catch { return null; }
        }

        // ── PROTOTYPE #77: menu-aware chaining ───────────────────────────────
        private sealed class ChainMenuHandler : IUIAutomationEventHandler
        {
            private readonly Action<IUIAutomationElement, int> _cb;
            public ChainMenuHandler(Action<IUIAutomationElement, int> cb) { _cb = cb; }
            public void HandleAutomationEvent(IUIAutomationElement sender, int eventId) => _cb(sender, eventId);
        }

        // PROTOTYPE #77 (change #5): UIA event handlers for the event-driven menu watch.
        private sealed class StructureChangedHandler : IUIAutomationStructureChangedEventHandler
        {
            private readonly Action<IUIAutomationElement, StructureChangeType, Array> _cb;
            public StructureChangedHandler(Action<IUIAutomationElement, StructureChangeType, Array> cb) { _cb = cb; }
            public void HandleStructureChangedEvent(IUIAutomationElement sender, StructureChangeType changeType, Array runtimeId) => _cb(sender, changeType, runtimeId);
        }

        private sealed class PropertyChangedHandler : IUIAutomationPropertyChangedEventHandler
        {
            private readonly Action<IUIAutomationElement, int, object> _cb;
            public PropertyChangedHandler(Action<IUIAutomationElement, int, object> cb) { _cb = cb; }
            public void HandlePropertyChangedEvent(IUIAutomationElement sender, int propertyId, object newValue) => _cb(sender, propertyId, newValue);
        }

        private void SubscribeMenuEvents()
        {
            try
            {
                var root = _menuUia.GetRootElement();
                _menuHandler = new ChainMenuHandler(OnMenuEvent);
                _menuUia.AddAutomationEventHandler(UIA_MenuOpenedEventId, root, TreeScope.TreeScope_Subtree, null, _menuHandler);
                _menuUia.AddAutomationEventHandler(UIA_MenuClosedEventId, root, TreeScope.TreeScope_Subtree, null, _menuHandler);
                _logger.Info("[MENU] subscribed to UIA MenuOpened/MenuClosed (global subtree)");
            }
            catch (Exception ex)
            {
                _logger.Warning($"[MENU] subscribe failed: {ex.GetType().Name}: {ex.Message}");
                _menuHandler = null;
            }
        }

        // COM worker thread — marshal to UI, then handle.
        private void OnMenuEvent(IUIAutomationElement sender, int eventId)
        {
            if (_disposed || !ChainingEnabled) return;
            try { _overlay.BeginInvoke(() => HandleMenuOnUi(sender, eventId)); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void HandleMenuOnUi(IUIAutomationElement sender, int eventId)
        {
            if (_disposed) return;
            bool opened = eventId == UIA_MenuOpenedEventId;

            // PROTOTYPE #77 (diagnostic): log EVERY menu event with the state that gates it,
            // BEFORE any guard — so at Info level we can see whether MenuOpened fires during an
            // active chain and what blocks the menu path (mode / live reconcile generation).
            _logger.Info($"[MENU] event={(opened ? "opened" : "closed")} mode={_stateManager.CurrentMode} chainGen={_chainGeneration} menuActive={_menuActive} filtering={!string.IsNullOrEmpty(_stateManager.FilterText)}");

            // PROTOTYPE #77 (change #5): robust gate — honor the menu if hints are showing OR a
            // commit happened very recently (the commit→reactivate gap). Timestamp-based, so it
            // cannot get "stuck cleared" the way the _chainActive flag did (intermittent ignores).
            bool sessionLive = _stateManager.CurrentMode != HintMode.Inactive
                               || (DateTime.UtcNow - _lastCommitUtc).TotalMilliseconds < MenuCommitGraceMs;
            if (!sessionLive)
            {
                _logger.Debug($"[MENU] {(opened ? "opened" : "closed")} ignored — no active session");
                return;
            }

            if (opened)
            {
                if (sender == null) { _logger.Warning("[MENU] opened with null sender — ignoring"); return; }

                string name = "?", ct = "?";
                try { name = sender.CurrentName ?? ""; } catch { }
                try { ct = sender.CurrentControlType.ToString(); } catch { }
                int menuHwnd = NativeWindowHandle(sender);

                // PROTOTYPE #77 (change #3): menu-scoped PROGRESSIVE settle. hwnd-less WinUI/XAML
                // flyouts fire MenuOpened BEFORE their item peers are in the UIA tree, so a single
                // scan races population (viaElement=0). Instead, poll the sender's OWN subtree
                // (change-#1 element overload) on a short interval, showing items as they appear and
                // settling when stable — the #73/D reconcile pattern scoped to the menu element.
                // Classic Win32 menus (Explorer, real HWND) settle on poll #1. Each MenuOpened
                // supersedes the prior via ++_menuGeneration, so multi-fire re-entrancy chases the
                // latest sender rather than churning; labels persist across fires via _menuLabels.
                // The WINDOW event watch is already subscribed for the session (change #7); a menu
                // session just sets its scan roots + flags. Its events drive rescans while active.
                _chainGeneration++;         // supersede any in-flight foreground reconcile
                _menuRoot = sender;         // primary scan root for the event-driven rescans
                _menuAuxRoot = null;
                _menuActive = true;
                _menuSettled = false;
                _menuLastSig = "";
                System.Threading.Interlocked.Exchange(ref _menuDirty, 0);
                int menuGen = ++_menuGeneration;
                _logger.Info($"[MENU] opened — exe={ForegroundExe()} name='{name}' ct={ct} menuHwnd={menuHwnd} → settling (menuGen={menuGen}, chainGen→{_chainGeneration})");
                MenuSettleReconcile(sender, menuGen);
            }
            else // MenuClosed
            {
                _logger.Info($"[MENU] closed (menuActive={_menuActive})");
                if (_menuActive)
                {
                    _menuActive = false;
                    _menuSettled = false;
                    _menuGeneration++;          // stop any running settle loop / supersede events
                    _menuRoot = null;
                    _menuAuxRoot = null;
                    _menuLabels.Clear();        // fresh labels for the next menu session
                    _menuNextLabel = 0;
                    _menuLastSig = "";
                    // Resume: re-hint the foreground app (the menu is gone). The window watch stays.
                    _chainOriginPid = GetForegroundPid();
                    ChainReScanReconcile(++_chainGeneration);
                }
            }
        }

        // PROTOTYPE #77 (change #3): INITIAL settle only — poll the menu's OWN subtree until items
        // appear and stabilize (fixes the hwnd-less-flyout race where MenuOpened fires before item
        // peers populate). Once settled, hands off to the EVENT-DRIVEN watch (change #5): no more
        // polling — StructureChanged + PropertyChanged drive subsequent re-hints. Bails when
        // superseded (menuGen), disposed, the menu closed, or the user filters.
        private async void MenuSettleReconcile(IUIAutomationElement menu, int menuGen)
        {
            int stableStreak = 0;
            try
            {
                for (int attempt = 1; attempt <= MenuSettleMaxAttempts; attempt++)
                {
                    if (_disposed || menuGen != _menuGeneration || !_menuActive) return;
                    if (!string.IsNullOrEmpty(_stateManager.FilterText))
                    {
                        _logger.Info($"[MENU] settle stop — user is typing a filter (menuGen={menuGen})");
                        return;
                    }

                    var hints = BuildMenuHintsViaContainers(out int count, out string sig, out _);
                    if (_disposed || menuGen != _menuGeneration || !_menuActive) return;

                    if (hints.Count > 0)
                    {
                        bool changed = sig != _menuLastSig;
                        if (changed)
                        {
                            if (_stateManager.CurrentMode == HintMode.Inactive)
                                _stateManager.Activate(HintSource.ForegroundWindow);
                            _overlay.EnsureTopmost();
                            _stateManager.SetHints(hints);
                            _menuLastSig = sig;
                        }
                        _logger.Debug($"[MENU] settle poll #{attempt} — containers→{count}, shown={hints.Count}, changed={changed}");

                        if (!changed)
                        {
                            if (++stableStreak >= MenuSettleStableStreak)
                            {
                                _menuSettled = true;
                                _logger.Info($"[MENU] settled at {hints.Count} items after {attempt} polls — handing off to EVENT watch (menuGen={menuGen})");
                                return;
                            }
                        }
                        else { stableStreak = 0; }
                    }
                    else
                    {
                        _logger.Debug($"[MENU] settle poll #{attempt} — empty, waiting for items to populate");
                    }

                    await System.Threading.Tasks.Task.Delay(MenuSettlePollMs).ConfigureAwait(true);
                }
                // Never fully stabilized within the budget — hand off to events anyway (they may
                // still fire as the menu changes). Mark settled so event rescans are honored.
                _menuSettled = true;
                _logger.Info($"[MENU] settle budget reached — handing off to EVENT watch (menuGen={menuGen})");
            }
            catch (Exception ex)
            {
                _logger.Error("[MENU] settle loop error", ex);
            }
        }

        // PROTOTYPE #77 (change #7): scan each root's subtree (+ any expanded item's subtree) and
        // reconcile into the session-labeled hint list. Roots = the menu element and, when a submenu
        // opened elsewhere, the event sender. Shared by settle and the event-driven watch.
        private List<HintItem> BuildMenuHints(IReadOnlyList<IUIAutomationElement> roots, out int baseCount, out int subCount, out string sig, out HashSet<string> seen)
        {
            baseCount = 0; subCount = 0;
            var hints = new List<HintItem>();
            seen = new HashSet<string>();

            foreach (var root in roots)
            {
                if (root == null) continue;
                IReadOnlyList<Services.ClickableElement> baseEls;
                try { baseEls = _uiaService.FindClickableElements(root); }
                catch (Exception ex) { _logger.Warning($"[MENU] scan failed: {ex.GetType().Name}: {ex.Message}"); baseEls = System.Array.Empty<Services.ClickableElement>(); }
                baseCount += baseEls.Count;
                ReconcileMenuInto(baseEls, hints, seen);

                // If any item is Expanded, also merge its own subtree (submenu items may attach there).
                foreach (var e in baseEls)
                {
                    if (!IsExpanded(e.Element)) continue;
                    IReadOnlyList<Services.ClickableElement> subEls;
                    try { subEls = _uiaService.FindClickableElements(e.Element); }
                    catch { subEls = System.Array.Empty<Services.ClickableElement>(); }
                    subCount += subEls.Count;
                    ReconcileMenuInto(subEls, hints, seen);
                }
            }

            sig = string.Join("|", seen.OrderBy(x => x, StringComparer.Ordinal));
            return hints;
        }

        private void ReconcileMenuInto(IReadOnlyList<Services.ClickableElement> els, List<HintItem> hints, HashSet<string> seen)
        {
            foreach (var e in els)
            {
                string? rid = RuntimeIdKey(e.Element);
                if (rid == null || !seen.Add(rid)) continue;
                if (!_menuLabels.TryGetValue(rid, out var label))
                {
                    if (_menuLabelPool.Length == 0) _menuLabelPool = LabelGenerator.Generate(ChainReconcileLabelPool);
                    if (_menuNextLabel >= _menuLabelPool.Length) continue;
                    label = _menuLabelPool[_menuNextLabel++];
                    _menuLabels[rid] = label;
                }
                hints.Add(new HintItem { Rect = e.Bounds, Element = e.Element, Label = label, CurrentOpacity = 1.0f, TargetOpacity = 1.0f });
            }
        }

        // ── PROTOTYPE #77 (change #8): ExpandCollapse-on-MenuItem menu watch ───
        // Distilled to the one clean signal: a MenuItem's ExpandCollapse → Expanded means a menu or
        // submenu just opened (fires for BOTH the menubar item AND submenu parents — the case
        // MenuOpened misses). Subscribe ONLY PropertyChanged[ExpandCollapse] on the foreground window
        // subtree (dropped StructureChanged — firehose/pollution — and IsEnabled). MenuOpened+settle
        // still handles the INITIAL open (populates + gives the menu root); ExpandCollapse catches
        // in-place submenus. Events just flag dirty (cheap, UIA thread); the tick does the rescan.
        private void SubscribeWindowWatch()
        {
            try
            {
                if (_watchedMenu != null) return; // already subscribed this session
                var hwnd = _windowManager.GetForegroundWindow();
                if (!_windowManager.IsWindowValid(hwnd)) { _logger.Debug("[MENU][evt] no valid foreground window to watch"); return; }
                IUIAutomationElement? win = null;
                try { win = _menuUia.ElementFromHandle(hwnd); }
                catch (Exception ex) { _logger.Debug($"[MENU][evt] ElementFromHandle: {ex.Message}"); }
                if (win == null) return;
                _watchedMenu = win;

                _mecExpand = 0;
                _menuEvtDetailBudget = 200; // generous for the Notepad/VSCode diagnostic drive
                _menuEvtTick = 0;
                System.Threading.Interlocked.Exchange(ref _menuDirty, 0);
                _menuPropHandler = new PropertyChangedHandler((s, pid, val) => OnMenuWatchProperty(s, pid, val));
                _menuUia.AddPropertyChangedEventHandler(win, TreeScope.TreeScope_Subtree, null, _menuPropHandler,
                    new int[] { UIA_PropertyIds.UIA_ExpandCollapseExpandCollapseStatePropertyId });
                // Single coalescing tick — the ONLY thing that touches the UI thread. Events just flag
                // dirty (cheap, on the UIA thread); this drives at most one rescan per ~120ms.
                _menuEvtTimer = new System.Threading.Timer(_ => MenuWatchTick(), null, MenuEventDebounceMs, MenuEventDebounceMs);
                _logger.Info($"[MENU][evt] window watch subscribed (exe={ForegroundExe()}, hwnd={hwnd}) — PropertyChanged[ExpandCollapse] on MenuItems, TreeScope_Subtree");
            }
            catch (Exception ex)
            {
                _logger.Warning($"[MENU][evt] window watch subscribe failed: {ex.GetType().Name}: {ex.Message}");
                UnsubscribeWindowWatch();
            }
        }

        private void UnsubscribeWindowWatch()
        {
            try { _menuEvtTimer?.Dispose(); } catch { }
            _menuEvtTimer = null;
            var target = _watchedMenu;
            if (target != null)
            {
                try { if (_menuPropHandler != null) _menuUia.RemovePropertyChangedEventHandler(target, _menuPropHandler); }
                catch (Exception ex) { _logger.Debug($"[MENU][evt] remove property handler: {ex.Message}"); }
            }
            _menuPropHandler = null; _watchedMenu = null; _menuRoot = null; _menuAuxRoot = null;
        }

        // A chain session is live (hints showing, or a commit within the grace window). Menus/submenus
        // are hinted only during a session — but NOT gated on _menuActive, so ExpandCollapse can START
        // the menu hinting even when no MenuOpened fired (the VS-submenu-via-commit case).
        private bool IsSessionLive()
        {
            try
            {
                return _stateManager.CurrentMode != HintMode.Inactive
                    || (DateTime.UtcNow - _lastCommitUtc).TotalMilliseconds < MenuCommitGraceMs;
            }
            catch { return false; }
        }

        // COM worker thread — ONLY cheap, non-blocking work: filter to a MenuItem, stash the sender,
        // flag dirty. No BeginInvoke here (that per-event flood hung the UI thread); the tick rescans.
        private void OnMenuWatchProperty(IUIAutomationElement sender, int propertyId, object newValue)
        {
            if (_disposed) return;
            if (propertyId != UIA_PropertyIds.UIA_ExpandCollapseExpandCollapseStatePropertyId) return;
            if (!IsSessionLive()) return; // only during a chain session (NOT gated on _menuActive)

            // Log EVERY ExpandCollapse (with control type) BEFORE filtering — so we can see what
            // Notepad/VSCode senders look like even when they aren't UIA MenuItems.
            int ctRaw = -1; try { ctRaw = sender.CurrentControlType; } catch { }
            int ctLog = ctRaw;
            MenuEvtDetail(() => { string n = ""; try { n = sender?.CurrentName ?? ""; } catch { } return $"[MENU][evt] ExpandCollapse={FormatEventValue(newValue)} ct={ctLog} on '{n}'"; });

            // Filter to MENU-related elements: the watch also hears ExpandCollapse from non-menu
            // widgets (e.g. VS toolbar dropdowns). Native menu bars use Buttons for File/Edit, so we
            // accept a Menu/MenuBar/MenuItem OR anything with a Menu/MenuBar ancestor (change #9).
            if (!IsMenuRelated(sender)) return;
            System.Threading.Interlocked.Increment(ref _mecExpand);
            System.Threading.Interlocked.Exchange(ref _menuDirty, 1);
        }

        private void MenuEvtDetail(Func<string> msg)
        {
            if (System.Threading.Interlocked.Decrement(ref _menuEvtDetailBudget) < 0) return;
            try { lock (_menuEvtLock) { _logger.Info(msg()); } } catch { }
        }

        // Threadpool tick (~120ms): flush counts each ~1s, and — the ONLY UI-thread hop — do at most
        // ONE coalesced rescan when events have flagged dirty. This is what fixes the AppHang.
        private void MenuWatchTick()
        {
            if (_disposed) return;
            if ((++_menuEvtTick % 8) == 0)
            {
                long ex = System.Threading.Interlocked.Exchange(ref _mecExpand, 0);
                if (ex != 0) _logger.Info($"[MENU][evt] ~1s — ExpandCollapse(MenuItem)={ex}");
            }

            if (_menuDirty != 1) return;
            if (!IsSessionLive()) { System.Threading.Interlocked.Exchange(ref _menuDirty, 0); return; } // stale; drop
            System.Threading.Interlocked.Exchange(ref _menuDirty, 0);
            try { _overlay.BeginInvoke(() => EnsureMenuAndRescan("expand")); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        // UI thread: an ExpandCollapse flagged a menu/submenu open. Establish the menu session if
        // needed (superseding the foreground reconcile so it can't hint the whole window), then
        // rescan via the menu CONTAINERS in the window (change #9 — framework-agnostic).
        private void EnsureMenuAndRescan(string reason)
        {
            if (_disposed || !IsSessionLive()) return;
            if (!_menuActive)
            {
                _menuActive = true;
                _chainGeneration++;   // supersede any in-flight foreground reconcile (whole-window scan)
                _menuLabels.Clear(); _menuNextLabel = 0;
                _menuLastSig = "";
                _logger.Info($"[MENU][evt] menu session started from ExpandCollapse (exe={ForegroundExe()}, {reason})");
            }
            _menuSettled = true;      // the menu is already open — no poll-settle needed
            DoMenuRescan(_menuGeneration, reason);
        }

        // Walk up to the outermost Menu/MenuBar ancestor so a scan gets the whole open menu chain.
        private IUIAutomationElement FindMenuRoot(IUIAutomationElement el)
        {
            try
            {
                var walker = _menuUia.ControlViewWalker;
                var cur = el;
                var best = el;
                for (int i = 0; i < 8 && cur != null; i++)
                {
                    int ct; try { ct = cur.CurrentControlType; } catch { break; }
                    if (ct == 50009 || ct == 50010) best = cur; // Menu / MenuBar → candidate root
                    if (ct == 50032) break;                     // Window → stop
                    var parent = walker.GetParentElement(cur);
                    if (parent == null) break;
                    cur = parent;
                }
                return best;
            }
            catch { return el; }
        }

        private static string FormatEventValue(object v)
        {
            try
            {
                if (v is double[] r && r.Length == 4) return $"[{r[0]:0},{r[1]:0},{r[2]:0},{r[3]:0}]";
                return v?.ToString() ?? "null";
            }
            catch { return "?"; }
        }

        // UI thread (posted at most once per tick). Rescan via the menu CONTAINERS in the window
        // (change #9): everything clickable inside a Menu/MenuBar, regardless of item control type.
        private void DoMenuRescan(int menuGen, string reason)
        {
            if (_disposed || menuGen != _menuGeneration || !_menuActive) return;
            if (!_menuSettled) { _logger.Debug($"[MENU][evt] rescan deferred (not settled) ({reason})"); return; }
            if (!string.IsNullOrEmpty(_stateManager.FilterText)) { _logger.Debug($"[MENU][evt] rescan skipped — filtering ({reason})"); return; }

            var hints = BuildMenuHintsViaContainers(out int count, out string sig, out _);
            if (_disposed || menuGen != _menuGeneration || !_menuActive) return;
            if (hints.Count == 0)
            {
                // No menu containers → menu is gone. End the session and resume normal chaining.
                _logger.Info($"[MENU][evt] rescan ({reason}) → 0 items — ending menu session, resuming foreground");
                _menuActive = false;
                _menuGeneration++;
                _menuLabels.Clear(); _menuNextLabel = 0; _menuLastSig = "";
                _chainOriginPid = GetForegroundPid();
                ChainReScanReconcile(++_chainGeneration);
                return;
            }
            if (sig == _menuLastSig) { _logger.Debug($"[MENU][evt] rescan ({reason}) — unchanged"); return; }

            _menuLastSig = sig;
            if (_stateManager.CurrentMode == HintMode.Inactive) _stateManager.Activate(HintSource.ForegroundWindow);
            _overlay.EnsureTopmost();
            _stateManager.SetHints(hints);
            _logger.Info($"[MENU][evt] rescan ({reason}) → {hints.Count} items (containers→{count}) (menuGen={menuGen})");
        }

        // Scan the watched window's Menu/MenuBar containers → all clickable items inside them.
        private List<HintItem> BuildMenuHintsViaContainers(out int count, out string sig, out HashSet<string> seen)
        {
            seen = new HashSet<string>();
            var hints = new List<HintItem>();
            IReadOnlyList<Services.ClickableElement> els = _watchedMenu != null
                ? _uiaService.FindMenuClickableElements(_watchedMenu)
                : System.Array.Empty<Services.ClickableElement>();
            ReconcileMenuInto(els, hints, seen);
            count = els.Count;
            sig = string.Join("|", seen.OrderBy(x => x, StringComparer.Ordinal));
            return hints;
        }

        // Is this element part of a menu — a Menu/MenuBar/MenuItem itself, OR (native menu bars use
        // Buttons for File/Edit) an element with a Menu/MenuBar ancestor within a few levels.
        private bool IsMenuRelated(IUIAutomationElement? el)
        {
            if (el == null) return false;
            if (IsMenuElement(el)) return true;
            try
            {
                var walker = _menuUia.ControlViewWalker;
                var cur = walker.GetParentElement(el);
                for (int i = 0; i < 6 && cur != null; i++)
                {
                    int ct; try { ct = cur.CurrentControlType; } catch { break; }
                    if (ct == 50009 || ct == 50010) return true; // Menu / MenuBar ancestor
                    if (ct == 50032) break;                       // Window → stop
                    cur = walker.GetParentElement(cur);
                }
            }
            catch { }
            return false;
        }

        // PROTOTYPE #77 (change #7): is this element part of a menu (a submenu parent, not a toolbar
        // dropdown)? Menu=50009, MenuBar=50010, MenuItem=50011. Fails closed (false) on any error.
        private static bool IsMenuElement(IUIAutomationElement? el)
        {
            if (el == null) return false;
            try
            {
                int ct = el.CurrentControlType;
                return ct == 50009 || ct == 50010 || ct == 50011;
            }
            catch { return false; }
        }

        // PROTOTYPE #77 (change #4): live-read the ExpandCollapse state; Expanded means this menu
        // item's submenu is currently open. Fails closed (false) on any error.
        private static bool IsExpanded(IUIAutomationElement el)
        {
            try
            {
                var v = el.GetCurrentPropertyValue(UIA_PropertyIds.UIA_ExpandCollapseExpandCollapseStatePropertyId);
                return v is int state && state == 1; // ExpandCollapseState_Expanded
            }
            catch { return false; }
        }

        private static int NativeWindowHandle(IUIAutomationElement? element)
        {
            if (element == null) return 0;
            try
            {
                var v = element.GetCurrentPropertyValue(UIA_PropertyIds.UIA_NativeWindowHandlePropertyId);
                return v is int i ? i : 0;
            }
            catch { return 0; }
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
                };
                dialog.HotkeyRecordingStopped += (_, _) =>
                {
                    _logger.Debug("Hotkey recording stopped, re-registering global hotkeys");
                    if (_options.Hotkey.Enabled)
                        _hotkeyWindow.RegisterGlobalHotkey(_options.Hotkey.Modifiers, _options.Hotkey.VirtualKey);
                    if (_options.TaskbarHotkey.Enabled)
                        _hotkeyWindow.RegisterTaskbarHotkey(_options.TaskbarHotkey.Modifiers, _options.TaskbarHotkey.VirtualKey);
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
            // PROTOTYPE #73/D: progressive-reconcile holds no timers/COM to tear down;
            // the generation guard + _disposed short-circuit any in-flight reconcile loop.
            try { UnsubscribeWindowWatch(); } catch { }            // PROTOTYPE #77 change #7
            try { _menuUia?.RemoveAllEventHandlers(); } catch { } // PROTOTYPE #77
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
    }
}
