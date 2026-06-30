using System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace WindowsHinting.NativeInterop
{
    internal sealed class ForegroundWindowHook : IDisposable
    {
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        private UnhookWinEventSafeHandle? _hookHandle;
        private readonly WINEVENTPROC _eventProc;
        private bool _disposed;

        public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundWindowChanged;

        public ForegroundWindowHook()
        {
            _eventProc = WinEventCallback;
        }

        public void Install()
        {
            if (_hookHandle is { IsInvalid: false })
                return;

            _hookHandle = PInvoke.SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_FOREGROUND,
                default,
                _eventProc,
                0,
                0,
                WINEVENT_OUTOFCONTEXT);

            if (_hookHandle is null || _hookHandle.IsInvalid)
            {
                throw new InvalidOperationException("Failed to install foreground window hook");
            }
        }

        public void Uninstall()
        {
            if (_hookHandle is { IsInvalid: false })
            {
                _hookHandle.Dispose();
                _hookHandle = null;
            }
        }

        private void WinEventCallback(
            HWINEVENTHOOK hWinEventHook,
            uint @event,
            HWND hwnd,
            int idObject,
            int idChild,
            uint idEventThread,
            uint dwmsEventTime)
        {
            if (@event == EVENT_SYSTEM_FOREGROUND && hwnd != default)
            {
                var args = new ForegroundWindowChangedEventArgs
                {
                    NewForegroundWindow = hwnd
                };

                ForegroundWindowChanged?.Invoke(this, args);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Uninstall();
            _disposed = true;
        }
    }

    internal sealed class ForegroundWindowChangedEventArgs : EventArgs
    {
        public HWND NewForegroundWindow { get; init; }
    }
}
