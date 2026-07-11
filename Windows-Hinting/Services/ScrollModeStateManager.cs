using System;
using UIAutomationClient;
using WindowsHinting.Models;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Manages scroll mode–specific state: selected target, selection vs control phase, numeric percent input buffer.
    /// </summary>
    internal sealed class ScrollModeStateManager
    {
        private ScrollPhase _phase = ScrollPhase.Selecting;
        private HintItem? _selectedTarget;
        private string _percentBuffer = "";

        public ScrollPhase CurrentPhase => _phase;
        public HintItem? SelectedTarget => _selectedTarget;
        public string PercentBuffer => _percentBuffer;

        public event EventHandler<ScrollPhase>? PhaseChanged;
        public event EventHandler<HintItem?>? SelectedTargetChanged;
        public event EventHandler<string>? PercentBufferChanged;

        public void SelectTarget(HintItem target)
        {
            _selectedTarget = target;
            SetPhase(ScrollPhase.Controlling);
            SelectedTargetChanged?.Invoke(this, _selectedTarget);
        }

        public void DeselectTarget()
        {
            _selectedTarget = null;
            _percentBuffer = "";
            SetPhase(ScrollPhase.Selecting);
            SelectedTargetChanged?.Invoke(this, _selectedTarget);
            PercentBufferChanged?.Invoke(this, _percentBuffer);
        }

        public void AppendToPercentBuffer(char digit)
        {
            if (char.IsDigit(digit) && _percentBuffer.Length < 3)
            {
                _percentBuffer += digit;
                PercentBufferChanged?.Invoke(this, _percentBuffer);
            }
        }

        public void ClearPercentBuffer()
        {
            if (_percentBuffer.Length > 0)
            {
                _percentBuffer = "";
                PercentBufferChanged?.Invoke(this, _percentBuffer);
            }
        }

        public void RemoveLastPercentChar()
        {
            if (_percentBuffer.Length > 0)
            {
                _percentBuffer = _percentBuffer[..^1];
                PercentBufferChanged?.Invoke(this, _percentBuffer);
            }
        }

        public int? GetPercentValue()
        {
            if (int.TryParse(_percentBuffer, out int value) && value >= 0 && value <= 100)
            {
                return value;
            }
            return null;
        }

        public void Reset()
        {
            _selectedTarget = null;
            _percentBuffer = "";
            _phase = ScrollPhase.Selecting;
        }

        private void SetPhase(ScrollPhase phase)
        {
            if (_phase != phase)
            {
                _phase = phase;
                PhaseChanged?.Invoke(this, _phase);
            }
        }
    }
}
