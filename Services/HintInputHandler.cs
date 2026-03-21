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

        public event EventHandler<SelectionCommittedEventArgs>? SelectionCommitted;

        public HintInputHandler(HintStateManager stateManager)
        {
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        }

        public bool ProcessKeyDown(int vkCode, KeyModifiers modifiers)
        {
            // Suppress auto-repeat
            if (_pressedKeys.Contains(vkCode))
                return false;

            _pressedKeys.Add(vkCode);

            // Handle A-Z keys (only when no modifiers held)
            if (vkCode >= 0x41 && vkCode <= 0x5A && (modifiers & (KeyModifiers.Alt | KeyModifiers.Control)) == 0)
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

            // Handle Space - commit selection
            // Modifier determines click action:
            //   No modifier  = Default (UIA pattern)
            //   Shift        = Left click
            //   Ctrl         = Right click
            //   Alt          = Double click
            if (vkCode == 0x20)
            {
                var match = _stateManager.GetExactMatch();
                if (match != null)
                {
                    var action = ResolveClickAction(modifiers);
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

        private static ClickAction ResolveClickAction(KeyModifiers modifiers)
        {
            if ((modifiers & KeyModifiers.Alt) != 0)
                return ClickAction.DoubleClick;
            if ((modifiers & KeyModifiers.Control) != 0)
                return ClickAction.RightClick;
            if ((modifiers & KeyModifiers.Shift) != 0)
                return ClickAction.LeftClick;
            return ClickAction.Default;
        }
    }
}