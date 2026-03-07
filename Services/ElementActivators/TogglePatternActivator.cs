using System;
using System.Runtime.InteropServices;
using HintOverlay.Logging;
using UIAutomationClient;

namespace HintOverlay.Services.ElementActivators
{
    internal sealed class TogglePatternActivator : IElementActivator
    {
        private readonly ILogger _logger;

        public TogglePatternActivator(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool TryActivate(IUIAutomationElement element)
        {
            IUIAutomationTogglePattern? pattern = null;
            try
            {
                pattern = element.GetCachedPattern(UIA_PatternIds.UIA_TogglePatternId) as IUIAutomationTogglePattern;
                if (pattern != null)
                {
                    pattern.Toggle();
                    _logger.Info("Successfully toggled element");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"TogglePattern failed: {ex.Message}");
            }
            finally
            {
                if (pattern != null && Marshal.IsComObject(pattern))
                {
                    Marshal.ReleaseComObject(pattern);
                }
            }
            return false;
        }
    }
}