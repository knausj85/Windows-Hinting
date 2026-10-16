using System;
using System.Runtime.InteropServices;
using Windows.Win32.UI.Accessibility;
using WindowsHinting.Logging;
using WindowsHinting.NativeInterop;
using WindowsHinting.Services;

namespace WindowsHinting.Services.ElementActivators
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
                var canFocus = element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsKeyboardFocusablePropertyId);
                if (canFocus is bool focusable && focusable)
                {
                    element.SetFocus();
                    _logger.Info($"Successfully set focus on element {element.GetCachedName()}");
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
