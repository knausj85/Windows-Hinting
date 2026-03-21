using System;
using System.Collections.Generic;
using HintOverlay.Models;

namespace HintOverlay.Services
{
    internal sealed class SelectionCommittedEventArgs : EventArgs
    {
        public ClickAction Action { get; }

        public SelectionCommittedEventArgs(ClickAction action)
        {
            Action = action;
        }
    }

    internal sealed class HintInputHandler
    {
        private readonly HashSet<int> _pressedKeys = new();
        private readonly HintStateManager _stateManager;

        private bool _clickActionShortcutsEnabled = true;
        private int _leftClickKey = 0x4C;   // L
        private int _rightClickKey = 0x52;  // R
        private int _doubleClickKey = 0x44; // D

        public event EventHandler<SelectionCommittedEventArgs>? SelectionCommitted;

        public HintInputHandler(HintStateManager stateManager)
        {
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        }

        public void ApplyOptions(ClickActionShortcutOptions options)
        {
            _clickActionShortcutsEnabled = options.Enabled;
            _leftClickKey = options.LeftClickKey;
            _rightClickKey = options.RightClickKey;
            _doubleClickKey = options.DoubleClickKey;
        }

        public bool ProcessKeyDown(int vkCode, KeyModifiers modifiers)
        {
            // Suppress auto-repeat
            if (_pressedKeys.Contains(vkCode))
                return false;

            _pressedKeys.Add(vkCode);

            bool shiftHeld = (modifiers & KeyModifiers.Shift) != 0;
            bool ctrlHeld = (modifiers & KeyModifiers.Control) != 0;
            bool altHeld = (modifiers & KeyModifiers.Alt) != 0;

            // Shift+key toggles click action (no Ctrl/Alt)
            if (_clickActionShortcutsEnabled && shiftHeld && !ctrlHeld && !altHeld)
            {
                if (vkCode == _leftClickKey)
                {
                    ToggleAction(ClickAction.LeftClick);
                    return true;
                }
                if (vkCode == _rightClickKey)
                {
                    ToggleAction(ClickAction.RightClick);
                    return true;
                }
                if (vkCode == _doubleClickKey)
                {
                    ToggleAction(ClickAction.DoubleClick);
                    return true;
                }
            }

            // Handle A-Z keys (only when no modifiers held)
            if (vkCode >= 0x41 && vkCode <= 0x5A && !shiftHeld && !ctrlHeld && !altHeld)
            {
                char c = (char)vkCode;
                var candidate = _stateManager.FilterText + c;

                if (!_stateManager.HasMatchingHint(candidate))
                {
                    System.Media.SystemSounds.Beep.Play();
                    return true; // Consume invalid input
                }

                _stateManager.AppendToFilter(c);
                return true;
            }

            // Handle Backspace
            if (vkCode == 0x08)
            {
                _stateManager.RemoveLastFilterChar();
                return true;
            }

            // Handle Escape - clear filter
            if (vkCode == 0x1B)
            {
                _stateManager.ClearFilter();
                return true;
            }

            // Handle Space - commit selection with the pending action
            if (vkCode == 0x20)
            {
                var match = _stateManager.GetExactMatch();
                if (match != null)
                {
                    var action = _stateManager.PendingAction;
                    SelectionCommitted?.Invoke(this, new SelectionCommittedEventArgs(action));
                }
                return true;
            }

            return false;
        }

        public void ProcessKeyUp(int vkCode)
        {
            _pressedKeys.Remove(vkCode);
        }

        public void Reset()
        {
            _pressedKeys.Clear();
        }

        private void ToggleAction(ClickAction action)
        {
            if (_stateManager.PendingAction == action)
                _stateManager.SetPendingAction(ClickAction.Default);
            else
                _stateManager.SetPendingAction(action);
        }
    }
}