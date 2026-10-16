using System;
using System.Runtime.InteropServices;
using Windows.Win32.UI.Accessibility;
using WindowsHinting.Logging;
using WindowsHinting.NativeInterop;
using WindowsHinting.Services;

namespace WindowsHinting.Services.ElementActivators
{
    internal sealed class ExpandCollapsePatternActivator : IElementActivator
    {
        private readonly ILogger _logger;

        public ExpandCollapsePatternActivator(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool TryActivate(IUIAutomationElement element)
        {
            IUIAutomationExpandCollapsePattern? pattern = null;

            bool isAvailable = element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsExpandCollapsePatternAvailablePropertyId) is true;

            if (isAvailable)
            {
                try
                {
                    pattern = element.GetCachedPattern(UIA_PATTERN_ID.UIA_ExpandCollapsePatternId) as IUIAutomationExpandCollapsePattern;
                    if (pattern != null)
                    {
                        pattern.Expand();
                        _logger.Info($"Successfully expanded/collapsed element {element.GetCachedName()}");
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
