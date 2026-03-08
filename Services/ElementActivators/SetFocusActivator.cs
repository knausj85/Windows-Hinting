using System;
using System.Runtime.InteropServices;
using HintOverlay.Logging;
using UIAutomationClient;

namespace HintOverlay.Services.ElementActivators
{
    internal sealed class SetFocusActivator : IElementActivator
    {
        private readonly ILogger _logger;

        public SetFocusActivator(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool TryActivate(IUIAutomationElement element)
        {
            try
            {
                // Check if element can receive focus
                var canFocus = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsKeyboardFocusablePropertyId);
                if (canFocus is bool focusable && focusable)
                {
                    element.SetFocus();
                    _logger.Info("Successfully set focus on element");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"SetFocus failed: {ex.Message}");
            }
            return false;
        }
    }
}