using System;
using HintOverlay.NativeInterop;

namespace HintOverlay.Services
{
    internal sealed class KeyboardHookService : IKeyboardHookService, IDisposable
    {
        private readonly KeyboardHook _hook;
        private bool _isActive;

        public event EventHandler<KeyboardEventArgs>? KeyPressed;
        public event EventHandler<KeyboardEventArgs>? KeyReleased;

        public bool IsActive => _isActive;

        public KeyboardHookService()
        {
            _hook = new KeyboardHook();
            _hook.KeyPressed += OnKeyPressed;
            _hook.KeyReleased += OnKeyReleased;
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

        private void OnKeyPressed(object? sender, KeyboardEventArgs e)
        {
            KeyPressed?.Invoke(this, e);
        }

        private void OnKeyReleased(object? sender, KeyboardEventArgs e)
        {
            KeyReleased?.Invoke(this, e);
        }

        public void Dispose()
        {
            Stop();
            _hook.Dispose();
        }
    }
}
