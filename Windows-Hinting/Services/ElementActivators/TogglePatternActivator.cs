using System;
using System.Runtime.InteropServices;
using Windows.Win32.UI.Accessibility;
using WindowsHinting.Logging;
using WindowsHinting.NativeInterop;
using WindowsHinting.Services;

namespace WindowsHinting.Services.ElementActivators
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

            bool isAvailable = element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsTogglePatternAvailablePropertyId) is true;

            if (isAvailable)
            {
                try
                {
                    pattern = element.GetCachedPattern(UIA_PATTERN_ID.UIA_TogglePatternId) as IUIAutomationTogglePattern;
                    if (pattern != null)
                    {
                        pattern.Toggle();
                        _logger.Info($"Successfully toggled element {element.GetCachedName()}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"ExpandCollapsePattern failed: {ex.Message}");
                }
                finally
                {
                    if (pattern != null && Marshal.IsComObject(pattern))
                    {
                        Marshal.ReleaseComObject(pattern);
                    }
                }
            }

            return false;
        }
    }
}
