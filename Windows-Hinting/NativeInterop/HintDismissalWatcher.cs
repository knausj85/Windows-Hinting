using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;
using WindowsHinting.Logging;

namespace WindowsHinting.NativeInterop
{
    /// <summary>
    /// Watches for the user actions that should auto-dismiss an active hint overlay, mirroring the
    /// intelligent dismissal proven in the scroll prototype:
    /// <list type="bullet">
    ///   <item>a real (non-injected) mouse-button press anywhere — observed, never swallowed, so the
    ///   click falls through to whatever the user actually clicked;</item>
    ///   <item>the foreground window changing to a window we don't own;</item>
    ///   <item>the target window (foreground-hint source only) moving or resizing.</item>
    /// </list>
    /// Esc-to-dismiss is handled elsewhere (the keyboard hook / input handler).
    ///
    /// <para>Hooks are installed from the UI thread (<see cref="HintController"/>.OnModeChanged), so
    /// their callbacks arrive on the UI thread. A callback must NOT tear the hooks down directly —
    /// it raises <see cref="DismissRequested"/>, and the consumer defers the actual deactivation to
    /// the message loop (BeginInvoke), so UnhookWindowsHookEx is never called from inside a hook
    /// callback.</para>
    /// </summary>
    internal sealed class HintDismissalWatcher : IDisposable
    {
        // MSLLHOOKSTRUCT.flags bit: the event was injected by SendInput, not real hardware.
        private const uint LLMHF_INJECTED = 0x00000001;
        // Offset of the `flags` DWORD within MSLLHOOKSTRUCT: pt.x(4) + pt.y(4) + mouseData(4).
        private const int MSLLHOOKSTRUCT_FLAGS_OFFSET = 12;

        // Mouse-button-down messages we treat as a dismissing click.
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_MBUTTONDOWN = 0x0207;
        private const uint WM_XBUTTONDOWN = 0x020B;

        // WinEvent constants + flags (not exposed as friendly CsWin32 constants, so pinned here).
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        private const int OBJID_WINDOW = 0;

        private readonly ILogger _logger;

        // Delegates are held in fields so they are not collected while the hooks are live.
        private readonly HOOKPROC _mouseProc;
        private readonly WINEVENTPROC _winEventProc;

        private UnhookWindowsHookExSafeHandle? _mouseHook;
        private UnhookWinEventSafeHandle? _foregroundHook;
        private UnhookWinEventSafeHandle? _locationHook;

        private HWND _targetWindow;
        private bool _running;
        private bool _disposed;

        /// <summary>Raised (on the UI thread) when a dismissing action is observed. The handler must
        /// defer the actual deactivation to the message loop; see the class remarks.</summary>
        public event EventHandler? DismissRequested;

        public HintDismissalWatcher(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mouseProc = MouseHookCallback;
            _winEventProc = WinEventCallback;
        }

        /// <summary>
        /// Begin watching. <paramref name="targetWindow"/> is the window the hints were built for
        /// (the foreground window). Pass <see cref="IntPtr.Zero"/> for taskbar hints — there is no
        /// single move/resize target, so the location-change hook is skipped; the click and
        /// foreground-change triggers still apply.
        /// </summary>
        public void Start(IntPtr targetWindow)
        {
            if (_disposed)
                return;
            if (_running)
                Stop();

            _targetWindow = (HWND)targetWindow;

            _mouseHook = PInvoke.SetWindowsHookEx(
                WINDOWS_HOOK_ID.WH_MOUSE_LL,
                _mouseProc,
                PInvoke.GetModuleHandle((string?)null),
                0);
            if (_mouseHook is null || _mouseHook.IsInvalid)
                _logger.Warning("HintDismissalWatcher: failed to install mouse hook");

            // Foreground-change: system-wide, but SKIPOWNPROCESS filters out our own overlay/tray
            // windows so activating the overlay never self-dismisses.
            _foregroundHook = PInvoke.SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                null, _winEventProc, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            // Move/resize: scoped to the target window's own thread to keep this (noisy) event cheap.
            if (targetWindow != IntPtr.Zero)
            {
                uint tid = PInvoke.GetWindowThreadProcessId(_targetWindow, out uint pid);
                _locationHook = PInvoke.SetWinEventHook(
                    EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
                    null, _winEventProc, pid, tid,
                    WINEVENT_OUTOFCONTEXT);
            }

            _running = true;
        }

        public void Stop()
        {
            if (!_running)
                return;
            _running = false;

            _mouseHook?.Dispose();
            _mouseHook = null;
            _foregroundHook?.Dispose();
            _foregroundHook = null;
            _locationHook?.Dispose();
            _locationHook = null;
            _targetWindow = default;
        }

        private LRESULT MouseHookCallback(int nCode, WPARAM wParam, LPARAM lParam)
        {
            if (nCode >= 0 && _running)
            {
                // Read MSLLHOOKSTRUCT.flags directly; skip our own injected clicks (e.g. the
                // MouseClickService activation click) so only real hardware clicks dismiss.
                uint flags = (uint)Marshal.ReadInt32(lParam, MSLLHOOKSTRUCT_FLAGS_OFFSET);
                if ((flags & LLMHF_INJECTED) == 0)
                {
                    uint msg = (uint)wParam.Value;
                    if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN ||
                        msg == WM_MBUTTONDOWN || msg == WM_XBUTTONDOWN)
                    {
                        // Observe only — never return 1, so the click passes through to the app.
                        RaiseDismiss("mouse click");
                    }
                }
            }

            return PInvoke.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void WinEventCallback(HWINEVENTHOOK hWinEventHook, uint eventId, HWND hwnd,
            int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
        {
            if (!_running)
                return;

            if (eventId == EVENT_SYSTEM_FOREGROUND)
            {
                // Foreground moved to a window that isn't the hint target (ours is filtered by
                // SKIPOWNPROCESS) → the hints are now stale.
                if (!hwnd.Equals(_targetWindow))
                    RaiseDismiss("foreground changed");
            }
            else if (eventId == EVENT_OBJECT_LOCATIONCHANGE)
            {
                // Only the target window frame itself (not child objects / caret): it moved/resized.
                if (hwnd.Equals(_targetWindow) && idObject == OBJID_WINDOW && idChild == 0)
                    RaiseDismiss("target moved/resized");
            }
        }

        private void RaiseDismiss(string reason)
        {
            _logger.Debug($"HintDismissalWatcher: dismiss ({reason})");
            DismissRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            Stop();
            _disposed = true;
        }
    }
}
