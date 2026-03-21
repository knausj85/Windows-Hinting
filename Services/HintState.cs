using System;
using System.Collections.Generic;
using System.Linq;

namespace HintOverlay.Services
{
    internal enum HintMode
    {
        Inactive,
        Scanning,
        Active,
        Selecting
    }

    internal enum HintSource
    {
        None,
        ForegroundWindow,
        Taskbar
    }

    internal sealed class HintStateManager
    {
        private HintMode _mode = HintMode.Inactive;
        private List<HintItem> _currentHints = new();
        private string _filterText = "";

        public HintMode CurrentMode => _mode;
        public HintSource CurrentSource { get; private set; } = HintSource.None;
        public IReadOnlyList<HintItem> CurrentHints => _currentHints;
        public string FilterText => _filterText;
        
        public event EventHandler<HintMode>? ModeChanged;
        public event EventHandler<IReadOnlyList<HintItem>>? HintsChanged;
        public event EventHandler<string>? FilterChanged;
        
        public void Activate(HintSource source = HintSource.ForegroundWindow)
        {
            if (_mode == HintMode.Inactive)
            {
                CurrentSource = source;
                SetMode(HintMode.Scanning);
            }
        }

        public void Deactivate()
        {
            if (_mode != HintMode.Inactive)
            {
                _filterText = "";
                _currentHints.Clear();
                CurrentSource = HintSource.None;
                SetMode(HintMode.Inactive);
                FilterChanged?.Invoke(this, _filterText);
                HintsChanged?.Invoke(this, _currentHints);
            }
        }
        
        public void SetHints(IReadOnlyList<HintItem> hints)
        {
            _currentHints = hints.ToList();
            if (_currentHints.Count > 0)
            {
                SetMode(HintMode.Active);
            }
            HintsChanged?.Invoke(this, _currentHints);
        }
        
        public void AppendToFilter(char c)
        {
            _filterText += c;
            UpdateHintOpacity();
            FilterChanged?.Invoke(this, _filterText);
        }
        
        public void RemoveLastFilterChar()
        {
            if (_filterText.Length > 0)
            {
                _filterText = _filterText[..^1];
                UpdateHintOpacity();
                FilterChanged?.Invoke(this, _filterText);
            }
        }
        
        public void ClearFilter()
        {
            if (_filterText.Length > 0)
            {
                _filterText = "";
                UpdateHintOpacity();
                FilterChanged?.Invoke(this, _filterText);
            }
        }
        
        public HintItem? GetExactMatch()
        {
            if (string.IsNullOrEmpty(_filterText))
                return null;
                
            return _currentHints.FirstOrDefault(h =>
                h.Label.Equals(_filterText, StringComparison.OrdinalIgnoreCase));
        }
        
        public bool HasMatchingHint(string text)
        {
            return _currentHints.Any(h =>
                h.Label.StartsWith(text, StringComparison.OrdinalIgnoreCase));
        }
        
        private void UpdateHintOpacity()
        {
            foreach (var hint in _currentHints)
            {
                bool matches = string.IsNullOrEmpty(_filterText) ||
                              hint.Label.StartsWith(_filterText, StringComparison.OrdinalIgnoreCase);
                hint.TargetOpacity = matches ? 1.0f : 0.0f;
            }
            HintsChanged?.Invoke(this, _currentHints);
        }
        
        private void SetMode(HintMode mode)
        {
            if (_mode != mode)
            {
                _mode = mode;
                ModeChanged?.Invoke(this, _mode);
            }
        }
    }
}