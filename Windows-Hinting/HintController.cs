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

        // ── PROTOTYPE #78: dedicated menubar auto-hint path ──────────────────
        // A SEPARATE hotkey (Ctrl+Alt+M) → find the first MenuBar, hint the items under it, then
        // re-hint on ExpandCollapse events until nothing is expanded. Deliberately isolated from the
        // #77 always-on chaining (own HintSource, own flags) so the menubar mechanism can be judged on
        // its own — a viability spike modeled on the taskbar path. Uses the shared _menuUia client with
        // its own cache request (adds ExpandCollapseState) + a MenuBar/Menu container condition.
        private const int MenuBarHotkeyMods = 0x0003;       // MOD_CONTROL | MOD_ALT
        private const int MenuBarHotkeyVk = 0x4D;           // 'M'
        // ExpandCollapseState (UIAutomationClient): 0 Collapsed, 1 Expanded, 2 PartiallyExpanded, 3 LeafNode.
        private const int ExpandCollapse_Expanded = 1;
        private const int ExpandCollapse_PartiallyExpanded = 2;
        private const int MenuBarLabelPoolSize = 400;
        private IUIAutomationCacheRequest? _menuBarCache;          // bounds + ExpandCollapseState + activation patterns
        private IUIAutomationCondition? _menuBarItemCondition;     // menu-ish clickable control types, enabled + onscreen
        private IUIAutomationCondition? _menuBarContainerCondition;// MenuBar OR Menu
        private bool _menuBarActive;                              // a menubar auto-hint session is live
        private bool _menuBarSawExpansion;                       // at least one item has been expanded this session
        private bool _menuBarPressExpandable;                    // the just-pressed item supports ExpandCollapse (plan a re-hint)
        private string _menuBarActiveParentName = "";            // name of the currently-open submenu parent (for hover-to-switch)
        private uint _menuBarOriginPid;                          // foreground process when the session began
        private int _menuBarGeneration;                          // supersedes stale rescans / debounce ticks
        private readonly Dictionary<string, string> _menuBarLabels = new(); // RuntimeId → stable label this session
        private int _menuBarNextLabel;
        private string[] _menuBarLabelPool = System.Array.Empty<string>();
        private string _menuBarLastPaintedSig = "";              // set currently shown (skip redundant repaints)
        private string _menuBarSwitchAwaySig = "";               // on a hover-switch, the menu we're LEAVING — don't repaint it
        private Dictionary<string, int> _menuBarSeen = new();    // item key → consecutive-scan count (per-item debounce vs. transients)
        // PROTOTYPE #78 diagnostic: a PERSISTENT, foreground-following ExpandCollapse probe. Independent
        // of the menubar session — it follows the foreground window, (re)subscribes ExpandCollapse on
        // that window's MenuBar (errors if none), and logs EVERY event unconditionally (tagged [UIA]).
        private const int EcProbePollMs = 400;                  // foreground-change poll cadence
        private System.Threading.Timer? _ecProbeTimer;          // foreground-change poll
        private PropertyChangedHandler? _ecProbePropHandler;    // ExpandCollapse + BoundingRectangle handler on the WINDOW (kept for removal)
        private StructureChangedHandler? _ecProbeStructHandler; // StructureChanged handler on the WINDOW (kept for removal)
        private IUIAutomationElement? _ecProbeWindow;           // currently-subscribed foreground window (all three watches)
        private IntPtr _ecProbeHwnd;                            // foreground window currently watched (Zero = none)
        private int _ecProbeBusy;                               // re-entrancy guard for the poll
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

                // PROTOTYPE #78: build the menubar path's own cache request + conditions on the shared client.
                BuildMenuBarUia();


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
                _hotkeyWindow.MenuBarToggleRequested += (s, e) => ToggleMenuBar(); // PROTOTYPE #78
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

                // PROTOTYPE #78: start the persistent foreground-following ExpandCollapse probe.
                StartExpandCollapseProbe();

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

            // PROTOTYPE #78: hardcoded menubar hotkey (Ctrl+Alt+M) — kept out of preferences.json so
            // the spike is self-contained (no schema migration). Promote to options if it graduates.
            try { _hotkeyWindow.RegisterMenuBarHotkey(MenuBarHotkeyMods, MenuBarHotkeyVk); }
            catch (Exception ex) { _logger.Warning($"[MENUBAR] hotkey register failed: {ex.Message}"); }

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
                if (mode == HintMode.Active && _options.AutoHideTimeoutSeconds > 0 && !_chainActive && !_menuActive && !_menuBarActive)
                {
                    _autoHideTimer.Change(_options.AutoHideTimeoutSeconds * 1000, Timeout.Infinite);
                    _logger.Debug($"Auto-hide timer started ({_options.AutoHideTimeoutSeconds}s)");
                }
                else if (_chainActive || _menuActive || _menuBarActive)
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

                // PROTOTYPE #78: a menubar session survives the transient commit→expand gap (a
                // committed menu item deactivates hints for a beat; the ExpandCollapse event then
                // re-activates + re-hints). Only a TERMINAL deactivate (Esc / click-away, i.e. NOT
                // within the commit grace) ends it. The no-expansion terminal is handled in the rescan.
                if (_menuBarActive)
                {
                    bool transient = (DateTime.UtcNow - _lastCommitUtc).TotalMilliseconds < MenuCommitGraceMs;
                    if (transient)
                        _logger.Info("[MENUBAR] deactivate within commit grace — keeping session (awaiting ExpandCollapse)");
                    else
                    {
                        _logger.Info("[MENUBAR] terminal deactivate (Esc / click-away) — ending session");
                        TeardownMenuBar();
                    }
                }

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

            // PROTOTYPE #78: don't let the input handler eat the menubar hotkey (Ctrl+Alt+M).
            bool menuBarHotkeyMatches = e.VirtualKeyCode == MenuBarHotkeyVk &&
                                        CheckModifiersMatch(MenuBarHotkeyMods, actualMods);
            if (menuBarHotkeyMatches)
            {
                _logger.Debug("Menubar hotkey pressed, not consuming");
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

                // PROTOTYPE #78: the whole menubar auto-hint decision is LOCAL to the pressed element —
                // no events, no whole-tree poll. If the pressed item supports ExpandCollapse (read from
                // its CACHE), it opens a submenu → plan a re-hint. If not, it was a leaf action → the
                // menu chain is over → end the session. Decided here; acted on after activation.
                bool menuBarHover = false; // captured by the activation closure below
                if (_menuBarActive)
                {
                    bool ec = false;
                    try { ec = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsExpandCollapsePatternAvailablePropertyId) is true; }
                    catch (Exception ex) { _logger.Debug($"[MENUBAR] cached ExpandCollapse read failed: {ex.Message}"); }
                    _menuBarPressExpandable = ec;

                    string pressedName = "";
                    try { pressedName = (element.GetCachedPropertyValue(UIA_PropertyIds.UIA_NamePropertyId) as string) ?? ""; } catch { }

                    // Hover-to-switch: if a submenu is ALREADY open and this is a DIFFERENT expandable
                    // item, a click/Invoke on it dismisses the menu — so HOVER instead (menus follow the
                    // mouse). The first open (nothing tracked yet) and leaf presses still click.
                    bool menuOpen = !string.IsNullOrEmpty(_menuBarActiveParentName);
                    if (ec && menuOpen && !string.Equals(pressedName, _menuBarActiveParentName, StringComparison.OrdinalIgnoreCase))
                    {
                        menuBarHover = true;
                        _menuBarSwitchAwaySig = _menuBarLastPaintedSig; // suppress the menu we're leaving until the new one opens
                        _logger.Info($"[MENUBAR] press '{match.Label}' ('{pressedName}') — switch from open '{_menuBarActiveParentName}' → HOVER");
                    }
                    else
                    {
                        _menuBarSwitchAwaySig = "";
                        _logger.Info($"[MENUBAR] press '{match.Label}' ('{pressedName}') supportsExpandCollapse={ec} → {(ec ? "PLAN re-hint" : "leaf → end session")}");
                    }

                    _menuBarActiveParentName = ec ? pressedName : ""; // track the newly-open parent (leaf closes the menu)
                }

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
                        if (menuBarHover)
                        {
                            // Switch menus by hover (real move; cursor stays), NOT a click that would dismiss.
                            _logger.Info($"[MENUBAR] hover → move cursor over '{_menuBarActiveParentName}' at {rect}");
                            _mouseClickService.Hover(rect);
                        }
                        else if (action == ClickAction.Default)
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
                        // PROTOTYPE #78: act on the local press decision. Expandable → the submenu is
                        // opening, so re-hint it. Leaf → the chain is done, end the session.
                        else if (_menuBarActive)
                        {
                            if (_menuBarPressExpandable)
                                MenuBarRehintAfterExpand();
                            else
                            {
                                _logger.Info("[MENUBAR] leaf press → ending session");
                                TeardownMenuBar();
                            }
                        }
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

        // ══ PROTOTYPE #78: dedicated menubar auto-hint path ══════════════════
        // A separate hotkey path, isolated from #77 chaining. Flow:
        //   Ctrl+Alt+M → find first MenuBar (or Menu) under the foreground window → hint its items
        //   (cache request carries ExpandCollapseState) → subscribe PropertyChanged[ExpandCollapse]
        //   on the window subtree → on each event, coalesce (120ms tick) and re-scan/re-hint →
        //   once an expansion has been seen, a scan with nothing expanded ends the session.
        // Session survives the commit→expand gap via _lastCommitUtc grace (see OnModeChanged).

        private void BuildMenuBarUia()
        {
            try
            {
                _menuBarContainerCondition = _menuUia.CreateOrCondition(
                    _menuUia.CreatePropertyCondition(UIA_PropertyIds.UIA_ControlTypePropertyId, UIA_ControlTypeIds.UIA_MenuBarControlTypeId),
                    _menuUia.CreatePropertyCondition(UIA_PropertyIds.UIA_ControlTypePropertyId, UIA_ControlTypeIds.UIA_MenuControlTypeId));

                var itemTypes = new[]
                {
                    UIA_ControlTypeIds.UIA_MenuItemControlTypeId,
                    UIA_ControlTypeIds.UIA_ButtonControlTypeId,
                    UIA_ControlTypeIds.UIA_SplitButtonControlTypeId,
                    UIA_ControlTypeIds.UIA_CheckBoxControlTypeId,
                    UIA_ControlTypeIds.UIA_RadioButtonControlTypeId,
                    UIA_ControlTypeIds.UIA_MenuControlTypeId,
                };
                var typeConds = itemTypes
                    .Select(t => _menuUia.CreatePropertyCondition(UIA_PropertyIds.UIA_ControlTypePropertyId, t))
                    .ToArray();
                var typeOr = _menuUia.CreateOrConditionFromArray(typeConds);
                var enabled = _menuUia.CreatePropertyCondition(UIA_PropertyIds.UIA_IsEnabledPropertyId, true);
                var onscreen = _menuUia.CreatePropertyCondition(UIA_PropertyIds.UIA_IsOffscreenPropertyId, false);
                var statusAnd = _menuUia.CreateAndCondition(enabled, onscreen);
                _menuBarItemCondition = _menuUia.CreateAndCondition(statusAnd, typeOr);

                var cache = _menuUia.CreateCacheRequest();
                cache.TreeScope = TreeScope.TreeScope_Element;
                cache.AddProperty(UIA_PropertyIds.UIA_BoundingRectanglePropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_ClickablePointPropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_ControlTypePropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_NamePropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_IsExpandCollapsePatternAvailablePropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_ExpandCollapseExpandCollapseStatePropertyId); // the key signal
                cache.AddProperty(UIA_PropertyIds.UIA_IsInvokePatternAvailablePropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_IsKeyboardFocusablePropertyId);
                cache.AddPattern(UIA_PatternIds.UIA_InvokePatternId);
                cache.AddPattern(UIA_PatternIds.UIA_ExpandCollapsePatternId);
                cache.AddPattern(UIA_PatternIds.UIA_LegacyIAccessiblePatternId);
                _menuBarCache = cache;
                _logger.Info("[MENUBAR] cache request + conditions built");
            }
            catch (Exception ex)
            {
                _logger.Warning($"[MENUBAR] BuildMenuBarUia failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public void ToggleMenuBar()
        {
            using (PerformanceMetrics.Start("ToggleMenuBar", _logger, LogLevel.Debug))
            {
                long now = Stopwatch.GetTimestamp();
                long elapsedMs = (now - _lastToggleTicks) * 1000 / Stopwatch.Frequency;
                if (elapsedMs < ToggleDebounceMs) { _logger.Debug($"[MENUBAR] toggle debounced ({elapsedMs}ms)"); return; }
                _lastToggleTicks = now;

                if (_menuBarActive)
                {
                    _logger.Info("[MENUBAR] hotkey — toggling OFF");
                    TeardownMenuBar();
                    return;
                }

                // Clear any other hints (foreground / taskbar / chain) before starting.
                if (_stateManager.CurrentMode != HintMode.Inactive)
                    _stateManager.Deactivate();

                _menuBarActive = true;
                _menuBarSawExpansion = false;
                _menuBarGeneration++;
                _menuBarOriginPid = GetForegroundPid();
                _menuBarLabels.Clear();
                _menuBarNextLabel = 0;
                _menuBarLabelPool = LabelGenerator.Generate(MenuBarLabelPoolSize);
                _menuBarPressExpandable = false;
                _menuBarActiveParentName = "";
                _menuBarLastPaintedSig = "";
                _menuBarSwitchAwaySig = "";
                _menuBarSeen = new();
                _logger.Info($"[MENUBAR] hotkey — starting session (exe={ForegroundExe()}, originPid={_menuBarOriginPid})");

                // Show the menu bar. Re-hints are then driven by presses (see OnSelectionCommitted):
                // press an expandable item → re-hint the opened submenu; press a leaf → end.
                RescanMenuBarOnUi("activate");
            }
        }

        // ── PROTOTYPE #78: persistent foreground-following ExpandCollapse probe ──
        // Runs from startup regardless of any menubar session. A poll watches the foreground window;
        // when it CHANGES, we move the ExpandCollapse watch to the new window's MenuBar (error if none).
        // Every event logs unconditionally as [UIA]; when a session is live it also flags a re-hint.
        private void StartExpandCollapseProbe()
        {
            _ecProbeTimer = new System.Threading.Timer(_ => EcProbePoll(), null, 500, EcProbePollMs);
            _logger.Info("[UIA] ExpandCollapse foreground probe started — following the foreground window's MenuBar");
        }

        // Threadpool (poll). Only UIA-client + Win32 calls here (all thread-agnostic); no WinForms.
        private void EcProbePoll()
        {
            if (_disposed) return;
            if (System.Threading.Interlocked.CompareExchange(ref _ecProbeBusy, 1, 0) != 0) return;
            try
            {
                var hwnd = _windowManager.GetForegroundWindow();
                if (!_windowManager.IsWindowValid(hwnd)) return;   // keep the current subscription
                if (hwnd == _ecProbeHwnd) return;                  // foreground unchanged → nothing to do

                // Foreground changed → move the watch. Record the hwnd up front so a window WITHOUT a
                // MenuBar is logged once, not every poll.
                UnsubscribeEcProbe();
                _ecProbeHwnd = hwnd;
                string exe = ForegroundExe();

                IUIAutomationElement? winRoot = null;
                IUIAutomationElement? menuBar = null;
                try
                {
                    try { winRoot = _menuUia.ElementFromHandle(hwnd); }
                    catch (Exception ex) { _logger.Warning($"[UIA] ElementFromHandle failed (exe={exe}, hwnd={hwnd}): {ex.Message}"); return; }
                    if (winRoot == null) return;

                    // WINDOW-level diagnostic watches on the whole foreground window subtree (regardless of
                    // whether it has a menu bar): ExpandCollapse + BoundingRectangle (PropertyChanged) and
                    // StructureChanged. The subtree covers the menu bar too, so a separate menubar-scoped
                    // ExpandCollapse watch would only double-log the same events — hence one watch here.
                    _ecProbeWindow = winRoot;
                    _ecProbePropHandler = new PropertyChangedHandler((s, pid, val) => OnEcProbeProperty(s, pid, val));
                    _menuUia.AddPropertyChangedEventHandler(winRoot, TreeScope.TreeScope_Subtree, null, _ecProbePropHandler,
                        new int[] {
                            UIA_PropertyIds.UIA_ExpandCollapseExpandCollapseStatePropertyId,
                            UIA_PropertyIds.UIA_BoundingRectanglePropertyId,
                        });
                    _ecProbeStructHandler = new StructureChangedHandler((s, ctype, rid) => OnEcProbeStructureChanged(s, ctype, rid));
                    _menuUia.AddStructureChangedEventHandler(winRoot, TreeScope.TreeScope_Subtree, null, _ecProbeStructHandler);
                    winRoot = null; // ownership → _ecProbeWindow
                    _logger.Info($"[UIA] window watch → exe={exe} (hwnd={hwnd}, TreeScope_Subtree) — ExpandCollapse + BoundingRect + StructureChanged");

                    // Diagnostic enumeration of the menu bar's top-level items (if present). Not subscribed
                    // separately — the window watch above already covers menu-bar ExpandCollapse.
                    menuBar = FindMenuBarRoot(_ecProbeWindow);
                    if (menuBar == null)
                        _logger.Info($"[UIA] no MenuBar in foreground window (exe={exe}, hwnd={hwnd})");
                    else
                        LogMenuBarChildren(menuBar, exe);
                }
                finally
                {
                    if (menuBar != null && System.Runtime.InteropServices.Marshal.IsComObject(menuBar))
                    { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(menuBar); } catch { } }
                    if (winRoot != null && System.Runtime.InteropServices.Marshal.IsComObject(winRoot))
                    { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(winRoot); } catch { } }
                }
            }
            catch (Exception ex) { _logger.Warning($"[UIA] probe poll error: {ex.GetType().Name}: {ex.Message}"); }
            finally { System.Threading.Interlocked.Exchange(ref _ecProbeBusy, 0); }
        }

        private void UnsubscribeEcProbe()
        {
            // Window watch (ExpandCollapse + BoundingRect + StructureChanged).
            var win = _ecProbeWindow;
            if (win != null && _ecProbePropHandler != null)
            {
                try { _menuUia.RemovePropertyChangedEventHandler(win, _ecProbePropHandler); }
                catch (Exception ex) { _logger.Debug($"[UIA] remove property handler: {ex.Message}"); }
            }
            if (win != null && _ecProbeStructHandler != null)
            {
                try { _menuUia.RemoveStructureChangedEventHandler(win, _ecProbeStructHandler); }
                catch (Exception ex) { _logger.Debug($"[UIA] remove structure handler: {ex.Message}"); }
            }
            if (win != null && System.Runtime.InteropServices.Marshal.IsComObject(win))
            { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(win); } catch { } }

            _ecProbeWindow = null;
            _ecProbePropHandler = null;
            _ecProbeStructHandler = null;
        }

        private void StopExpandCollapseProbe()
        {
            try { _ecProbeTimer?.Dispose(); } catch { }
            _ecProbeTimer = null;
            UnsubscribeEcProbe();
            _ecProbeHwnd = IntPtr.Zero;
        }

        private static string ExpandStateName(int state) => state switch
        {
            0 => "Collapsed",
            1 => "Expanded",
            2 => "PartiallyExpanded",
            3 => "LeafNode",
            _ => "?"
        };

        // Diagnostic: log each top-level MenuBar child + whether it supports the ExpandCollapse pattern.
        // Answers "can the ROOT menu items (Edit/View) raise ExpandCollapse, or do they need a different
        // signal?" — Win32 menu-bar items typically expose no ExpandCollapse; only cascading submenu
        // items (with a ▶) do, which is why only the nested submenus fire.
        private void LogMenuBarChildren(IUIAutomationElement menuBar, string exe)
        {
            IUIAutomationElementArray? kids = null;
            try
            {
                kids = menuBar.FindAllBuildCache(TreeScope.TreeScope_Children, _menuBarItemCondition, _menuBarCache);
                if (kids == null) { _logger.Debug($"[UIA] menubar children: none (exe={exe})"); return; }
                _logger.Info($"[UIA] menubar has {kids.Length} top-level item(s) (exe={exe}):");
                for (int i = 0; i < kids.Length; i++)
                {
                    IUIAutomationElement? k = null;
                    try
                    {
                        k = kids.GetElement(i);
                        if (k == null) continue;
                        string name = "?"; try { name = (k.GetCachedPropertyValue(UIA_PropertyIds.UIA_NamePropertyId) as string) ?? ""; } catch { }
                        int ct = 0; try { ct = k.GetCachedPropertyValue(UIA_PropertyIds.UIA_ControlTypePropertyId) is int c ? c : 0; } catch { }
                        bool hasEc = false; try { hasEc = k.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsExpandCollapsePatternAvailablePropertyId) is true; } catch { }
                        int state = -1; try { if (k.GetCachedPropertyValue(UIA_PropertyIds.UIA_ExpandCollapseExpandCollapseStatePropertyId) is int s) state = s; } catch { }
                        _logger.Info($"[UIA]   • name='{name}' ct={ct} hasExpandCollapse={hasEc} state={state} ({ExpandStateName(state)})");
                    }
                    finally { if (k != null && System.Runtime.InteropServices.Marshal.IsComObject(k)) { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(k); } catch { } } }
                }
            }
            catch (Exception ex) { _logger.Debug($"[UIA] LogMenuBarChildren failed: {ex.Message}"); }
            finally { if (kids != null && System.Runtime.InteropServices.Marshal.IsComObject(kids)) { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(kids); } catch { } } }
        }

        // COM worker thread — the persistent probe's PROPERTY handler. DIAGNOSTIC ONLY: logs every
        // ExpandCollapse-state and BoundingRectangle change under the watched MenuBar (the [UIA] trail).
        // Re-hints are press-driven, not event-driven, so these never touch the overlay.
        private void OnEcProbeProperty(IUIAutomationElement sender, int propertyId, object newValue)
        {
            if (_disposed) return;
            string name = "?", ct = "?";
            try { name = sender?.CurrentName ?? ""; } catch { }
            try { ct = sender?.CurrentControlType.ToString() ?? "?"; } catch { }

            if (propertyId == UIA_PropertyIds.UIA_ExpandCollapseExpandCollapseStatePropertyId)
            {
                int val = (newValue is int v) ? v : -1;
                _logger.Info($"[UIA] ExpandCollapse name='{name}' ct={ct} newState={val} ({ExpandStateName(val)}) menuBarActive={_menuBarActive}");
            }
            else if (propertyId == UIA_PropertyIds.UIA_BoundingRectanglePropertyId)
            {
                string rectStr = "?";
                try { if (newValue is double[] r && r.Length == 4) rectStr = $"({(int)r[0]},{(int)r[1]} {(int)r[2]}x{(int)r[3]})"; } catch { }
                _logger.Info($"[UIA] BoundingRect name='{name}' ct={ct} rect={rectStr} menuBarActive={_menuBarActive}");
            }
        }

        // COM worker thread — DIAGNOSTIC ONLY: structure changes (child add/remove/reorder) under the
        // watched MenuBar. StructureChanged can be chatty; scoped to the menu bar subtree it stays modest.
        private void OnEcProbeStructureChanged(IUIAutomationElement sender, StructureChangeType changeType, Array runtimeId)
        {
            if (_disposed) return;
            string name = "?", ct = "?";
            try { name = sender?.CurrentName ?? ""; } catch { }
            try { ct = sender?.CurrentControlType.ToString() ?? "?"; } catch { }
            int ridLen = runtimeId?.Length ?? 0;
            _logger.Info($"[UIA] StructureChanged type={changeType} name='{name}' ct={ct} ridLen={ridLen} menuBarActive={_menuBarActive}");
        }

        // PROTOTYPE #78: after pressing an EXPANDABLE menu item, the submenu opens a beat later. Poll a
        // few times to catch it (progressive population), re-hint it, then stop. If nothing hintable
        // appears within the window, the submenu is out of our scan scope → end the session.
        private async void MenuBarRehintAfterExpand()
        {
            int gen = ++_menuBarGeneration; // supersede any prior in-flight rehint loop (one loop at a time)
            bool paintedAny = false;
            // Keep reconciling across a settle window: the bar paints immediately (attempt #1), and the
            // LATE-opening submenu is ADDED as it appears (progressive reconcile keeps bar labels stable).
            // We do NOT stop at the first paint — that was the bug where VS showed only the root menu.
            for (int attempt = 1; attempt <= 8; attempt++) // ~1s of settle
            {
                await System.Threading.Tasks.Task.Delay(120).ConfigureAwait(true);
                if (_disposed || !_menuBarActive || gen != _menuBarGeneration) return;
                RescanMenuBarOnUi($"postpress#{attempt}");
                if (!_menuBarActive || gen != _menuBarGeneration) return; // rescan may have torn down (foreground left)
                if (_stateManager.CurrentMode != HintMode.Inactive) paintedAny = true;
            }
            // If nothing ever painted, the menu is out of our scan scope → end rather than hang.
            if (!paintedAny && _menuBarActive && gen == _menuBarGeneration && _stateManager.CurrentMode == HintMode.Inactive)
            {
                _logger.Info("[MENUBAR] no menu hints appeared after an expandable press — ending session (menu out of scan scope?)");
                TeardownMenuBar();
            }
        }

        // Find the first MenuBar (preferred) or Menu under the window root. Logs which matched.
        private IUIAutomationElement? FindMenuBarRoot(IUIAutomationElement windowRoot)
        {
            if (_menuBarContainerCondition == null || _menuBarCache == null) return null;
            try
            {
                var match = windowRoot.FindFirstBuildCache(TreeScope.TreeScope_Subtree, _menuBarContainerCondition, _menuBarCache);
                if (match != null)
                {
                    int ct = 0; try { ct = match.CachedControlType; } catch { }
                    _logger.Debug($"[MENUBAR] container match controlType={ct} ({(ct == UIA_ControlTypeIds.UIA_MenuBarControlTypeId ? "MenuBar" : ct == UIA_ControlTypeIds.UIA_MenuControlTypeId ? "Menu" : "?")})");
                }
                return match;
            }
            catch (System.Runtime.InteropServices.COMException ex) { _logger.Warning($"[MENUBAR] FindFirst container failed: {ex.Message}"); return null; }
        }

        // UI thread. Scan the current menu items and re-hint (stable labels by RuntimeId). Never paints
        // an empty overlay (keeps the session alive). Termination is decided by presses, not here.
        private void RescanMenuBarOnUi(string reason)
        {
            if (_disposed || !_menuBarActive) return;

            var hwnd = _windowManager.GetForegroundWindow();
            if (!_windowManager.IsWindowValid(hwnd)) { _logger.Debug($"[MENUBAR] rescan ({reason}): no valid foreground window"); return; }

            uint nowPid = GetForegroundPid();
            if (_menuBarOriginPid != 0 && nowPid != 0 && nowPid != _menuBarOriginPid)
            {
                _logger.Info($"[MENUBAR] exit — foreground left app (originPid={_menuBarOriginPid}, nowPid={nowPid})");
                TeardownMenuBar();
                return;
            }

            IUIAutomationElement? desktop = null;
            IUIAutomationElementArray? topWindows = null;
            IUIAutomationCondition? pidCond = null;
            var windowRoots = new List<IUIAutomationElement>();
            var containerRoots = new List<IUIAutomationElement>();
            var containerArrays = new List<IUIAutomationElementArray>();
            var itemArrays = new List<IUIAutomationElementArray>();
            try
            {
                uint pid = _menuBarOriginPid != 0 ? _menuBarOriginPid : nowPid;
                try { desktop = _menuUia.GetRootElement(); }
                catch (Exception ex) { _logger.Debug($"[MENUBAR] GetRootElement: {ex.Message}"); return; }
                if (desktop == null) return;

                // Gather menu containers from ALL top-level windows of the app's process — the main
                // window (menu bar) AND any open menu POPUPS. Qt/Win32/WinUI submenus open as SEPARATE
                // top-level popup windows whose items are NOT under the main window's UIA tree (e.g.
                // Beyond Compare's 'Edit' dropdown), so scanning only the foreground window misses them.
                pidCond = _menuUia.CreatePropertyCondition(UIA_PropertyIds.UIA_ProcessIdPropertyId, (int)pid);
                try { topWindows = desktop.FindAllBuildCache(TreeScope.TreeScope_Children, pidCond, _menuBarCache); }
                catch (System.Runtime.InteropServices.COMException ex) { _logger.Warning($"[MENUBAR] top-window FindAll failed: {ex.Message}"); return; }
                int winCount = topWindows?.Length ?? 0;
                for (int w = 0; w < winCount; w++)
                {
                    var win = topWindows!.GetElement(w);
                    if (win == null) continue;
                    windowRoots.Add(win);
                    IUIAutomationElementArray? cs = null;
                    try { cs = win.FindAllBuildCache(TreeScope.TreeScope_Subtree, _menuBarContainerCondition, _menuBarCache); }
                    catch (System.Runtime.InteropServices.COMException ex) { _logger.Debug($"[MENUBAR] container FindAll failed: {ex.Message}"); continue; }
                    if (cs == null) continue;
                    containerArrays.Add(cs);
                    for (int c = 0; c < cs.Length; c++) { var cont = cs.GetElement(c); if (cont != null) containerRoots.Add(cont); }
                }

                if (containerRoots.Count == 0)
                {
                    _logger.Info($"[MENUBAR] rescan ({reason}): no MenuBar/Menu in any window of exe={ForegroundExe()} (pid={pid}, windows={winCount})");
                    if (reason == "activate")
                    {
                        _trayIcon.ShowNotification("Menubar hints", "No menu bar found in the foreground window.");
                        TeardownMenuBar();
                    }
                    return;
                }

                var hints = new List<HintItem>();
                var seen = new HashSet<string>();
                var candidateKeys = new List<string>();          // key aligned with hints[i] (per-item debounce)
                var itemNames = new List<string>(); // DIAGNOSTIC (#78): names of hinted items
                int cIdx = -1;
                foreach (var container in containerRoots)
                {
                    cIdx++;

                    // DIAGNOSTIC (#78): what container is this?
                    int cCt = 0; string cName = "?";
                    try { cCt = container.GetCachedPropertyValue(UIA_PropertyIds.UIA_ControlTypePropertyId) is int cc ? cc : 0; } catch { }
                    try { cName = (container.GetCachedPropertyValue(UIA_PropertyIds.UIA_NamePropertyId) as string) ?? ""; } catch { }

                    IUIAutomationElementArray? items = null;
                    try { items = container.FindAllBuildCache(TreeScope.TreeScope_Descendants, _menuBarItemCondition, _menuBarCache); }
                    catch (System.Runtime.InteropServices.COMException ex) { _logger.Debug($"[MENUBAR] item FindAll failed: {ex.Message}"); continue; }
                    if (items == null) continue;
                    itemArrays.Add(items);
                    _logger.Debug($"[MENUBAR]   container[{cIdx}] ct={cCt} name='{cName}' descendantItems={items.Length}");

                    for (int i = 0; i < items.Length; i++)
                    {
                        IUIAutomationElement? el = null;
                        try
                        {
                            el = items.GetElement(i);
                            if (el == null) continue;
                            // DIAGNOSTIC (#78): name for drop attribution
                            string iName = "?"; try { iName = (el.GetCachedPropertyValue(UIA_PropertyIds.UIA_NamePropertyId) as string) ?? ""; } catch { }
                            if (el.GetCachedPropertyValue(UIA_PropertyIds.UIA_BoundingRectanglePropertyId) is not double[] r || r.Length != 4)
                            { _logger.Debug($"[MENUBAR]     drop '{iName}' — no bounding rect"); continue; }
                            var rect = new System.Drawing.Rectangle((int)r[0], (int)r[1], (int)r[2], (int)r[3]);
                            if (rect.Width <= 0 || rect.Height <= 0)
                            { _logger.Debug($"[MENUBAR]     drop '{iName}' — empty rect ({r[0]},{r[1]},{r[2]},{r[3]})"); continue; }

                            // Identity for stable labels + dedupe: prefer UIA RuntimeId, but some providers
                            // (e.g. Beyond Compare / Qt) return an EMPTY RuntimeId for every menu item —
                            // which collapses them all to one. Fall back to a bounds key there (menu items
                            // have distinct, stable positions).
                            string? rid = RuntimeIdKey(el);
                            string key = !string.IsNullOrEmpty(rid)
                                ? "r:" + rid
                                : $"b:{rect.Left},{rect.Top},{rect.Width},{rect.Height}";
                            if (!seen.Add(key)) { _logger.Debug($"[MENUBAR]     drop '{iName}' — duplicate key '{key}'"); continue; }
                            if (!_menuBarLabels.TryGetValue(key, out var label))
                            {
                                if (_menuBarNextLabel >= _menuBarLabelPool.Length) continue;
                                label = _menuBarLabelPool[_menuBarNextLabel++];
                                _menuBarLabels[key] = label;
                            }
                            try { itemNames.Add((el.GetCachedPropertyValue(UIA_PropertyIds.UIA_NamePropertyId) as string) ?? ""); } catch { }
                            candidateKeys.Add(key);
                            hints.Add(new HintItem { Rect = rect, Element = el, Label = label, CurrentOpacity = 1.0f, TargetOpacity = 1.0f });
                            el = null; // ownership → HintItem
                        }
                        catch (Exception ex) { _logger.Debug($"[MENUBAR] element: {ex.Message}"); }
                        finally
                        {
                            if (el != null && System.Runtime.InteropServices.Marshal.IsComObject(el))
                            { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(el); } catch { } }
                        }
                    }
                }

                // Per-item debounce (postpress only): an item must persist ≥2 consecutive scans before it
                // is shown, so single-poll TRANSIENTS (e.g. VS briefly exposing a nested submenu's items)
                // never render. 'activate' paints immediately (single scan, no loop to debounce against).
                bool debounce = reason != "activate";
                var newSeen = new Dictionary<string, int>(candidateKeys.Count);
                var shown = new List<HintItem>(hints.Count);
                var shownKeys = new List<string>(hints.Count);
                for (int i = 0; i < hints.Count; i++)
                {
                    string key = candidateKeys[i];
                    int cnt = (_menuBarSeen.TryGetValue(key, out var c) ? c : 0) + 1;
                    newSeen[key] = cnt;
                    if (!debounce || cnt >= 2) { shown.Add(hints[i]); shownKeys.Add(key); }
                }
                _menuBarSeen = newSeen;

                _logger.Info($"[MENUBAR] rescan ({reason}) exe={ForegroundExe()} containers={containerRoots.Count} scanned={hints.Count} shown={shown.Count} → [{string.Join(", ", itemNames)}]");

                if (shown.Count == 0)
                {
                    _logger.Debug($"[MENUBAR] rescan ({reason}) — nothing stable to show yet, keeping session");
                    return;
                }

                string sig = string.Join("|", shownKeys.OrderBy(x => x, StringComparer.Ordinal));

                // Don't flash the menu we're switching AWAY from: while a hover-switch is settling, the
                // OLD menu may still be open — suppress repainting it until the new menu appears.
                if (sig == _menuBarSwitchAwaySig)
                {
                    _logger.Debug($"[MENUBAR] rescan ({reason}) — still the pre-switch menu, suppress ({shown.Count})");
                    return;
                }

                if (sig == _menuBarLastPaintedSig && _stateManager.CurrentMode != HintMode.Inactive)
                {
                    _logger.Debug($"[MENUBAR] rescan ({reason}) — unchanged set ({shown.Count}), skip repaint");
                    return;
                }

                _menuBarSwitchAwaySig = ""; // a different menu is now showing; the switch completed
                _menuBarLastPaintedSig = sig;

                if (_stateManager.CurrentMode == HintMode.Inactive)
                    _stateManager.Activate(HintSource.MenuBar);
                _overlay.EnsureTopmost();
                _stateManager.SetHints(shown);
            }
            finally
            {
                foreach (var a in itemArrays)
                    if (a != null && System.Runtime.InteropServices.Marshal.IsComObject(a)) { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(a); } catch { } }
                foreach (var cr in containerRoots)
                    if (cr != null && System.Runtime.InteropServices.Marshal.IsComObject(cr)) { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(cr); } catch { } }
                foreach (var ca in containerArrays)
                    if (ca != null && System.Runtime.InteropServices.Marshal.IsComObject(ca)) { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(ca); } catch { } }
                foreach (var wr in windowRoots)
                    if (wr != null && System.Runtime.InteropServices.Marshal.IsComObject(wr)) { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(wr); } catch { } }
                if (topWindows != null && System.Runtime.InteropServices.Marshal.IsComObject(topWindows)) { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(topWindows); } catch { } }
                if (pidCond != null && System.Runtime.InteropServices.Marshal.IsComObject(pidCond)) { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(pidCond); } catch { } }
                if (desktop != null && System.Runtime.InteropServices.Marshal.IsComObject(desktop)) { try { System.Runtime.InteropServices.Marshal.ReleaseComObject(desktop); } catch { } }
            }
        }

        private void TeardownMenuBar()
        {
            bool wasActive = _menuBarActive;
            _menuBarActive = false;
            _menuBarSawExpansion = false;
            _menuBarOriginPid = 0;
            _menuBarPressExpandable = false;
            _menuBarActiveParentName = "";
            _menuBarGeneration++;             // supersede any in-flight MenuBarRehintAfterExpand loop
            // The ExpandCollapse PROBE is persistent and is NOT torn down here — it keeps following the
            // foreground window and logging.
            _menuBarLabels.Clear();
            _menuBarNextLabel = 0;
            _menuBarLastPaintedSig = "";
            _menuBarSwitchAwaySig = "";
            _menuBarSeen = new();
            if (wasActive) _logger.Info("[MENUBAR] session ended");
            // Only clear the overlay if WE own it; a terminal deactivate from OnModeChanged already did.
            if (_stateManager.CurrentMode != HintMode.Inactive && _stateManager.CurrentSource == HintSource.MenuBar)
                _stateManager.Deactivate();
        }
        // ════════════════════════════════════════════════════════════════════

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

            // PROTOTYPE #78: the dedicated menubar path owns the menu while its session is live —
            // yield so the two paths never double-hint or fight over the overlay.
            if (_menuBarActive)
            {
                _logger.Debug("[MENU] yielding — #78 menubar session is active");
                return;
            }

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
            try { _menuBarActive = false; } catch { } // PROTOTYPE #78 session (re-hint loop is guarded by _disposed)
            try { StopExpandCollapseProbe(); } catch { }          // PROTOTYPE #78 persistent probe
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
