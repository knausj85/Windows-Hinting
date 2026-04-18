using System;
using System.Runtime.InteropServices;
using System.Threading;
using UIAutomationClient;
using WindowsHinting.Logging;
using WindowsHinting.Services.Native;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Experimental service that listens for UI Automation menu / window
    /// open and close events and raises application-level events so the
    /// hint controller can auto-populate hints for the opened menu.
    /// </summary>
    internal sealed class AutoMenuHintService : IDisposable
    {
        private readonly IUIAutomation _automation;
        private readonly ILogger _logger;
        private readonly SynchronizationContext? _uiContext;
        private readonly int _ownProcessId;
        private UiaEventHandler? _handler;
        private IUIAutomationElement? _rootElement;
        private bool _enabled;
        private bool _disposed;

        // EVENT_SYSTEM_FOREGROUND hook: used to detect activation of
        // surfaces that don't emit a UIA WindowOpened event (notably the
        // taskbar Jump List, which is a pre-existing
        // Windows.UI.Core.CoreWindow that is merely shown/activated on
        // demand).
        private IntPtr _winEventHook;
        private NativeMethods.WinEventDelegate? _winEventProc;

        // Tracks the HWND of the currently "open" foreground-tracked
        // surface so we can raise a single MenuOpened when it becomes
        // foreground and a MenuClosed when focus leaves it.
        private IntPtr _focusTrackedHwnd;

        /// <summary>
        /// Kind of auto-hinted menu surface.
        /// </summary>
        internal enum AutoMenuKind
        {
            /// <summary>Popup / context menu (right-click menu, jump list, submenu of a context menu).</summary>
            ContextMenu,
            /// <summary>Drop-down from a classic application menu bar (File/Edit/View ...).</summary>
            MenuBar
        }

        internal sealed class AutoMenuOpenedEventArgs : EventArgs
        {
            public IUIAutomationElement MenuRoot { get; }
            public IUIAutomationElement? MenuBarRoot { get; }
            public AutoMenuKind Kind { get; }

            public AutoMenuOpenedEventArgs(IUIAutomationElement menuRoot, IUIAutomationElement? menuBarRoot, AutoMenuKind kind)
            {
                MenuRoot = menuRoot;
                MenuBarRoot = menuBarRoot;
                Kind = kind;
            }
        }

        /// <summary>
        /// Raised on the UI thread when a menu or a supported "menu-like"
        /// window (for example the taskbar Jump List) has opened.
        /// </summary>
        public event EventHandler<AutoMenuOpenedEventArgs>? MenuOpened;

        /// <summary>
        /// Raised on the UI thread when the most-recently-opened menu has
        /// closed or when menu mode ends.
        /// </summary>
        public event EventHandler? MenuClosed;

        public AutoMenuHintService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _automation = new CUIAutomation();
            _uiContext = SynchronizationContext.Current;
            _ownProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
        }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_disposed)
                    return;

                if (_enabled == value)
                    return;

                _enabled = value;
                if (_enabled)
                    Subscribe();
                else
                    Unsubscribe();
            }
        }

        private void Subscribe()
        {
            try
            {
                _rootElement = _automation.GetRootElement();
                if (_rootElement == null)
                {
                    _logger.Warning("AutoMenuHintService: failed to get UIA root element");
                    return;
                }

                _handler = new UiaEventHandler(this);

                _automation.AddAutomationEventHandler(
                    UIA_EventIds.UIA_MenuOpenedEventId,
                    _rootElement, TreeScope.TreeScope_Subtree, null, _handler);
                _automation.AddAutomationEventHandler(
                    UIA_EventIds.UIA_MenuClosedEventId,
                    _rootElement, TreeScope.TreeScope_Subtree, null, _handler);
                _automation.AddAutomationEventHandler(
                    UIA_EventIds.UIA_MenuModeStartEventId,
                    _rootElement, TreeScope.TreeScope_Subtree, null, _handler);
                _automation.AddAutomationEventHandler(
                    UIA_EventIds.UIA_MenuModeEndEventId,
                    _rootElement, TreeScope.TreeScope_Subtree, null, _handler);
                _automation.AddAutomationEventHandler(
                    UIA_EventIds.UIA_Window_WindowOpenedEventId,
                    _rootElement, TreeScope.TreeScope_Subtree, null, _handler);
                _automation.AddAutomationEventHandler(
                    UIA_EventIds.UIA_Window_WindowClosedEventId,
                    _rootElement, TreeScope.TreeScope_Subtree, null, _handler);

                // EVENT_SYSTEM_FOREGROUND hook — required for surfaces like
                // the taskbar Jump List that are pre-existing windows
                // activated on demand (no WindowOpened event fires). Using a
                // WinEvent hook instead of UIA focus change avoids the
                // high-volume focus events fired for every caret move and is
                // guaranteed to deliver the top-level foreground HWND.
                SubscribeForegroundHook();

                _logger.Info("AutoMenuHintService: subscribed to UIA menu/window events and EVENT_SYSTEM_FOREGROUND");
            }
            catch (Exception ex)
            {
                _logger.Error("AutoMenuHintService: failed to subscribe to UIA events", ex);
                Unsubscribe();
            }
        }

        private void Unsubscribe()
        {
            try
            {
                if (_handler != null)
                {
                    try { _automation.RemoveAllEventHandlers(); } catch { }
                    _handler = null;
                }

                UnsubscribeForegroundHook();

                if (_rootElement != null && Marshal.IsComObject(_rootElement))
                {
                    try { Marshal.ReleaseComObject(_rootElement); } catch { }
                }
                _rootElement = null;
                _focusTrackedHwnd = IntPtr.Zero;

                _logger.Info("AutoMenuHintService: unsubscribed from UIA events and foreground hook");
            }
            catch (Exception ex)
            {
                _logger.Error("AutoMenuHintService: error during unsubscribe", ex);
            }
        }

        private static string EventIdName(int eventId) => eventId switch
        {
            UIA_EventIds.UIA_MenuOpenedEventId => "MenuOpened",
            UIA_EventIds.UIA_MenuClosedEventId => "MenuClosed",
            UIA_EventIds.UIA_MenuModeStartEventId => "MenuModeStart",
            UIA_EventIds.UIA_MenuModeEndEventId => "MenuModeEnd",
            UIA_EventIds.UIA_Window_WindowOpenedEventId => "WindowOpened",
            UIA_EventIds.UIA_Window_WindowClosedEventId => "WindowClosed",
            _ => "Other"
        };

        private void LogIncomingEvent(IUIAutomationElement sender, int eventId)
        {
            try
            {
                string className = "";
                string name = "";
                int controlType = 0;
                int processId = 0;
                IntPtr hwnd = IntPtr.Zero;
                long style = 0;
                long exStyle = 0;

                try { className = sender.CurrentClassName ?? ""; } catch { }
                try { name = sender.CurrentName ?? ""; } catch { }
                try { controlType = sender.CurrentControlType; } catch { }
                try { processId = sender.CurrentProcessId; } catch { }
                try { hwnd = sender.CurrentNativeWindowHandle; } catch { }

                if (hwnd != IntPtr.Zero)
                {
                    try { style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE); } catch { }
                    try { exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE); } catch { }
                }

                string windowTitle = "";
                if (hwnd != IntPtr.Zero)
                {
                    try
                    {
                        var sb = new System.Text.StringBuilder(256);
                        if (NativeMethods.GetWindowText(hwnd, sb, sb.Capacity) > 0)
                            windowTitle = sb.ToString();
                    }
                    catch { }
                }

                string procName = "";
                if (processId != 0)
                {
                    try { using var p = System.Diagnostics.Process.GetProcessById(processId); procName = p.ProcessName; } catch { }
                }

                _logger.Debug(
                    $"AutoMenuHintService.Event: id={eventId}({EventIdName(eventId)}) " +
                    $"pid={processId}({procName}) ct={controlType} hwnd=0x{hwnd.ToInt64():X} " +
                    $"style=0x{style:X} exStyle=0x{exStyle:X} class='{className}' name='{name}' title='{windowTitle}'");
            }
            catch (Exception ex)
            {
                _logger.Debug($"AutoMenuHintService.LogIncomingEvent failed: {ex.Message}");
            }
        }

        private void HandleEvent(IUIAutomationElement sender, int eventId)
        {
            // This runs on a UIA worker thread. Capture the information we
            // need and marshal to the UI thread for any app-level work.
            try
            {
                // DIAGNOSTIC: log every UIA event before any filtering so we can
                // see what Edge / Chromium / etc. actually emits.
                LogIncomingEvent(sender, eventId);

                if (IsOwnProcess(sender))
                {
                    _logger.Debug($"AutoMenuHintService: ignoring event {eventId} from own process");
                    // Never auto-hint our own UI (tray context menu, overlay,
                    // preferences dialog). Handling these would re-enter the
                    // app's UI thread while it is pumping a modal menu loop
                    // and deadlock.
                    return;
                }

                switch (eventId)
                {
                    case UIA_EventIds.UIA_MenuOpenedEventId:
                        {
                            var (kind, menuBar) = ClassifyMenu(sender);
                            _logger.Debug($"AutoMenuHintService: MenuOpened (kind={kind})");
                            PostMenuOpened(sender, menuBar, kind);
                        }
                        break;

                    case UIA_EventIds.UIA_Window_WindowOpenedEventId:
                        {
                            IntPtr hwnd = IntPtr.Zero;
                            try { hwnd = sender.CurrentNativeWindowHandle; } catch { }
                            if (IsSupportedWindow(sender, hwnd))
                            {
                                // Chromium/Electron menus (VS Code, Slack, Discord,
                                // ...) come through as plain WindowOpened events
                                // on Chrome_WidgetWin popups — there is no
                                // MenuOpened event. Classify so menu-bar
                                // dropdowns still get MenuBar treatment.
                                var (kind, menuBar) = ClassifyMenu(sender);
                                _logger.Debug($"AutoMenuHintService: Supported WindowOpened (kind={kind})");
                                PostMenuOpened(sender, menuBar, kind);
                            }
                            else
                            {
                                _logger.Debug("AutoMenuHintService: WindowOpened rejected by IsSupportedWindow");
                            }
                        }
                        break;

                    case UIA_EventIds.UIA_MenuClosedEventId:
                    case UIA_EventIds.UIA_MenuModeEndEventId:
                        _logger.Debug($"AutoMenuHintService: MenuClosed/MenuModeEnd ({eventId})");
                        PostMenuClosed();
                        break;

                    case UIA_EventIds.UIA_Window_WindowClosedEventId:
                        {
                            IntPtr hwnd = IntPtr.Zero;
                            try { hwnd = sender.CurrentNativeWindowHandle; } catch { }
                            if (IsSupportedWindow(sender, hwnd))
                            {
                                _logger.Debug("AutoMenuHintService: Supported WindowClosed");
                                PostMenuClosed();
                            }
                        }
                        break;

                    case UIA_EventIds.UIA_MenuModeStartEventId:
                        // No-op: MenuOpened will follow.
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("AutoMenuHintService: error handling UIA event", ex);
            }
        }

        private void PostMenuOpened(IUIAutomationElement sender, IUIAutomationElement? menuBarRoot, AutoMenuKind kind)
        {
            var args = new AutoMenuOpenedEventArgs(sender, menuBarRoot, kind);
            if (_uiContext != null)
            {
                _uiContext.Post(_ => MenuOpened?.Invoke(this, args), null);
            }
            else
            {
                MenuOpened?.Invoke(this, args);
            }
        }

        private void PostMenuClosed()
        {
            if (_uiContext != null)
            {
                _uiContext.Post(_ => MenuClosed?.Invoke(this, EventArgs.Empty), null);
            }
            else
            {
                MenuClosed?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool IsSupportedWindow(IUIAutomationElement element, IntPtr hwnd)
        {
            string className = "";
            string title = "";
            long style = 0;
            string reason = "no match";
            bool supported = false;

            try
            {
                if (hwnd == IntPtr.Zero)
                {
                    reason = "hwnd is zero";
                    return false;
                }

                // Use the native Win32 class name rather than UIA's
                // CurrentClassName: the UIA class can lag or be overridden by
                // the provider, while the Win32 class is authoritative and
                // cheap to read.
                className = GetWin32ClassName(hwnd);
                title = GetWin32WindowText(hwnd);

                // Taskbar Jump List: class "Windows.UI.Core.CoreWindow" and
                // "Jump List" substring in the window title.
                if (string.Equals(className, "Windows.UI.Core.CoreWindow", StringComparison.Ordinal)
                    && title.IndexOf("Jump List", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    reason = "taskbar Jump List (CoreWindow + 'Jump List' title)";
                    supported = true;
                    return true;
                }

                // Chromium / Electron popup windows (VS Code, Slack, Discord,
                // Chrome, Edge, ...). These host both the context menu and the
                // menu-bar drop-downs as a lightweight top-level popup with
                // class "Chrome_WidgetWin_1" (or "_0") and WS_POPUP style
                // (no caption, not a child). The main application window
                // uses the same class but has a caption and is not a popup,
                // so we filter it out via the window style.
                //if (className.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal))
                //{
                //    style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
                //    bool isPopup = (style & NativeMethods.WS_POPUP) != 0;
                //    bool hasCaption = (style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION;
                //    bool isChild = (style & NativeMethods.WS_CHILD) != 0;

                //    if (/*isPopup &&*/ hasCaption && !isChild)
                //    {
                //        reason = "Chromium popup (WS_POPUP, no caption, not child)";
                //        supported = true;
                //        return true;
                //    }

                //    reason = $"Chromium class but style rejected (popup={isPopup}, caption={hasCaption}, child={isChild})";
                //    return false;
                //}

                reason = "class not in supported list";
                return false;
            }
            catch (Exception ex)
            {
                reason = $"exception: {ex.Message}";
                return false;
            }
            finally
            {
                _logger.Debug(
                    $"IsSupportedWindow: supported={supported} hwnd=0x{hwnd.ToInt64():X} " +
                    $"class='{className}' title='{title}' style=0x{style:X} reason='{reason}'");
            }
        }

        private static string GetWin32ClassName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return "";

            var sb = new System.Text.StringBuilder(256);
            int len = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
            return len > 0 ? sb.ToString(0, len) : "";
        }

        private static string GetWin32WindowText(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return "";

            var sb = new System.Text.StringBuilder(512);
            int len = NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return len > 0 ? sb.ToString(0, len) : "";
        }

        private bool IsOwnProcess(IUIAutomationElement element)
        {
            try
            {
                return element.CurrentProcessId == _ownProcessId;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Classifies an opened menu element. If the menu (or one of its
        /// nearby ancestors) belongs to a classic application <c>MenuBar</c>
        /// — i.e. the popup is a drop-down from a top-level File / Edit /
        /// View entry — returns <see cref="AutoMenuKind.MenuBar"/> and the
        /// owning <c>MenuBar</c> element. Otherwise returns
        /// <see cref="AutoMenuKind.ContextMenu"/>.
        /// </summary>
        private (AutoMenuKind Kind, IUIAutomationElement? MenuBar) ClassifyMenu(IUIAutomationElement menuRoot)
        {
            try
            {
                var walker = _automation.ControlViewWalker;

                // 1) The focused element is normally the MenuItem that
                //    opened the popup; if its ancestor chain contains a
                //    MenuBar within a few levels, treat this as a menu-bar
                //    drop-down.
                IUIAutomationElement? focused = null;
                try { focused = _automation.GetFocusedElement(); } catch { }

                if (focused != null)
                {
                    var menuBar = FindMenuBarAncestor(walker, focused, maxDepth: 5);
                    if (menuBar != null)
                    {
                        return (AutoMenuKind.MenuBar, menuBar);
                    }
                }

                // 2) Some frameworks parent the popup under (or near) a
                //    MenuBar — walk a few ancestors of the menu root too.
                var menuBar2 = FindMenuBarAncestor(walker, menuRoot, maxDepth: 5);
                if (menuBar2 != null)
                {
                    return (AutoMenuKind.MenuBar, menuBar2);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"ClassifyMenu failed: {ex.Message}");
            }

            return (AutoMenuKind.ContextMenu, null);
        }

        private IUIAutomationElement? FindMenuBarAncestor(
            IUIAutomationTreeWalker walker,
            IUIAutomationElement start,
            int maxDepth)
        {
            IUIAutomationElement? current = start;
            int depth = 0;
            try
            {
                while (current != null && depth < maxDepth)
                {
                    int controlType = 0;
                    try { controlType = current.CurrentControlType; } catch { }

                    if (controlType == UIA_ControlTypeIds.UIA_MenuBarControlTypeId)
                    {
                        return current;
                    }

                    IUIAutomationElement? parent = null;
                    try { parent = walker.GetParentElement(current); } catch { }

                    if (current != start && Marshal.IsComObject(current))
                    {
                        try { Marshal.ReleaseComObject(current); } catch { }
                    }

                    current = parent;
                    depth++;
                }
            }
            catch
            {
                // fall through
            }

            if (current != null && current != start && Marshal.IsComObject(current))
            {
                try { Marshal.ReleaseComObject(current); } catch { }
            }

            return null;
        }

        /// <summary>
        /// Subscribes to <c>EVENT_SYSTEM_FOREGROUND</c> to detect surfaces
        /// that don't emit a UIA <c>WindowOpened</c> (notably the taskbar
        /// Jump List). The callback runs on the UI thread because we pass
        /// <c>WINEVENT_OUTOFCONTEXT</c> and installed on the thread that
        /// pumps messages (the owning UI thread).
        /// </summary>
        private void SubscribeForegroundHook()
        {
            try
            {
                _winEventProc = OnForegroundChanged;
                _winEventHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _winEventProc,
                    0, 0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);

                if (_winEventHook == IntPtr.Zero)
                {
                    _logger.Warning("AutoMenuHintService: SetWinEventHook(EVENT_SYSTEM_FOREGROUND) failed");
                    _winEventProc = null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("AutoMenuHintService: failed to install foreground hook", ex);
                _winEventProc = null;
                _winEventHook = IntPtr.Zero;
            }
        }

        private void UnsubscribeForegroundHook()
        {
            try
            {
                if (_winEventHook != IntPtr.Zero)
                {
                    NativeMethods.UnhookWinEvent(_winEventHook);
                    _winEventHook = IntPtr.Zero;
                }
                _winEventProc = null;
            }
            catch (Exception ex)
            {
                _logger.Debug($"AutoMenuHintService: error removing foreground hook: {ex.Message}");
            }
        }

        private void OnForegroundChanged(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // EVENT_SYSTEM_FOREGROUND fires with OBJID_WINDOW for the new
            // foreground top-level HWND. Filter out OBJID_CURSOR etc.
            if (hwnd == IntPtr.Zero
                || idObject != NativeMethods.OBJID_WINDOW
                || idChild != NativeMethods.CHILDID_SELF)
            {
                return;
            }

            try
            {
                // Skip our own UI.
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == (uint)_ownProcessId)
                    return;

                bool supported = IsSupportedWindow(null!, hwnd);

                if (supported)
                {
                    if (hwnd == _focusTrackedHwnd)
                        return;

                    if (_focusTrackedHwnd != IntPtr.Zero)
                    {
                        _logger.Debug("AutoMenuHintService: foreground left previously tracked surface");
                        PostMenuClosed();
                    }

                    _focusTrackedHwnd = hwnd;

                    // Get a UIA element for the HWND so downstream code
                    // (ClassifyMenu, scan) has something to work with.
                    IUIAutomationElement? topLevel = null;
                    try { topLevel = _automation.ElementFromHandle(hwnd); } catch { }
                    if (topLevel == null)
                    {
                        _logger.Debug("AutoMenuHintService: foreground supported but ElementFromHandle returned null");
                        return;
                    }

                    var (kind, menuBar) = ClassifyMenu(topLevel);
                    _logger.Debug($"AutoMenuHintService: foreground supported (kind={kind}, hwnd=0x{hwnd.ToInt64():X})");
                    PostMenuOpened(topLevel, menuBar, kind);
                }
                else if (_focusTrackedHwnd != IntPtr.Zero)
                {
                    _logger.Debug("AutoMenuHintService: foreground left tracked surface, closing");
                    _focusTrackedHwnd = IntPtr.Zero;
                    PostMenuClosed();
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"AutoMenuHintService: error handling foreground change: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unsubscribe();

            if (_automation != null && Marshal.IsComObject(_automation))
            {
                try { Marshal.ReleaseComObject(_automation); } catch { }
            }
        }

        [ComVisible(true)]
        [Guid("5F92A2B5-9E6A-4F4C-8D0B-B6E9D6F2E711")]
        private sealed class UiaEventHandler : IUIAutomationEventHandler
        {
            private readonly AutoMenuHintService _owner;
            public UiaEventHandler(AutoMenuHintService owner) { _owner = owner; }

            public void HandleAutomationEvent(IUIAutomationElement sender, int eventId)
            {
                _owner.HandleEvent(sender, eventId);
            }
        }
    }
}
