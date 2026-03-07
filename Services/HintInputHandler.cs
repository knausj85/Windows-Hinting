using System;
using System.Collections.Generic;
using HintOverlay.Models;

namespace HintOverlay.Services
{
    internal sealed class HintInputHandler
    {
        private readonly HashSet<int> _pressedKeys = new();
        private readonly HintStateManager _stateManager;
        
        public event EventHandler? SelectionCommitted;
        
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
            
            // Ignore if Alt or Ctrl is held (except for configured hotkey)
            if ((modifiers & (KeyModifiers.Alt | KeyModifiers.Control)) != 0)
                return false;
            
            // Handle A-Z keys
            if (vkCode >= 0x41 && vkCode <= 0x5A)
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
            
            // Handle Space or Enter - commit selection
            if (vkCode == 0x20 || vkCode == 0x0D)
            {
                var match = _stateManager.GetExactMatch();
                if (match != null)
                {
                    SelectionCommitted?.Invoke(this, EventArgs.Empty);
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
    }
}