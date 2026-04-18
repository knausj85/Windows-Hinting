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

        /// <summary>
        /// Raised on the UI thread when a menu or a supported "menu-like"
        /// window (for example the taskbar Jump List) has opened. The
        /// argument is the root element that should be scanned for items.
        /// </summary>
        public event EventHandler<IUIAutomationElement>? MenuOpened;

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

                _logger.Info("AutoMenuHintService: subscribed to UIA menu/window events");
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

                if (_rootElement != null && Marshal.IsComObject(_rootElement))
                {
                    try { Marshal.ReleaseComObject(_rootElement); } catch { }
                }
                _rootElement = null;

                _logger.Info("AutoMenuHintService: unsubscribed from UIA events");
            }
            catch (Exception ex)
            {
                _logger.Error("AutoMenuHintService: error during unsubscribe", ex);
            }
        }

        private void HandleEvent(IUIAutomationElement sender, int eventId)
        {
            // This runs on a UIA worker thread. Capture the information we
            // need and marshal to the UI thread for any app-level work.
            try
            {
                if (IsOwnProcess(sender))
                {
                    // Never auto-hint our own UI (tray context menu, overlay,
                    // preferences dialog). Handling these would re-enter the
                    // app's UI thread while it is pumping a modal menu loop
                    // and deadlock.
                    return;
                }

                switch (eventId)
                {
                    case UIA_EventIds.UIA_MenuOpenedEventId:
                        _logger.Debug("AutoMenuHintService: MenuOpened");
                        PostMenuOpened(sender);
                        break;

                    case UIA_EventIds.UIA_Window_WindowOpenedEventId:
                        if (IsSupportedWindow(sender))
                        {
                            _logger.Debug("AutoMenuHintService: Supported WindowOpened (e.g. Jump List)");
                            PostMenuOpened(sender);
                        }
                        break;

                    case UIA_EventIds.UIA_MenuClosedEventId:
                    case UIA_EventIds.UIA_MenuModeEndEventId:
                        _logger.Debug($"AutoMenuHintService: MenuClosed/MenuModeEnd ({eventId})");
                        PostMenuClosed();
                        break;

                    case UIA_EventIds.UIA_Window_WindowClosedEventId:
                        if (IsSupportedWindow(sender))
                        {
                            _logger.Debug("AutoMenuHintService: Supported WindowClosed");
                            PostMenuClosed();
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

        private void PostMenuOpened(IUIAutomationElement sender)
        {
            var captured = sender;
            if (_uiContext != null)
            {
                _uiContext.Post(_ => MenuOpened?.Invoke(this, captured), null);
            }
            else
            {
                MenuOpened?.Invoke(this, captured);
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

        private bool IsSupportedWindow(IUIAutomationElement element)
        {
            try
            {
                string className;
                string name;
                try { className = element.CurrentClassName ?? ""; } catch { className = ""; }
                try { name = element.CurrentName ?? ""; } catch { name = ""; }

                // Taskbar Jump List: class "Windows.UI.Core.CoreWindow" and
                // "Jump List" substring in the name/title.
                if (string.Equals(className, "Windows.UI.Core.CoreWindow", StringComparison.Ordinal)
                    && name.IndexOf("Jump List", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            catch
            {
                // fall through
            }

            return false;
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
