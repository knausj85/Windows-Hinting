using System;
using WindowsHinting.NativeInterop;

namespace WindowsHinting.Services
{
    internal sealed class ForegroundWindowHookService : IForegroundWindowHookService, IDisposable
    {
        private readonly ForegroundWindowHook _hook;
        private bool _isActive;

        public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundWindowChanged;

        public bool IsActive => _isActive;

        public ForegroundWindowHookService()
        {
            _hook = new ForegroundWindowHook();
            _hook.ForegroundWindowChanged += OnForegroundWindowChanged;
        }

        public void Start()
        {
            if (!_isActive)
            {
                _hook.Install();
                _isActive = true;
            }
        }

        public void Stop()
        {
            if (_isActive)
            {
                _hook.Uninstall();
                _isActive = false;
            }
        }

        private void OnForegroundWindowChanged(object? sender, ForegroundWindowChangedEventArgs e)
        {
            ForegroundWindowChanged?.Invoke(this, e);
        }

        public void Dispose()
        {
            Stop();
            _hook.Dispose();
        }
    }
}
