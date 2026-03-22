using System;
using System.Runtime.InteropServices;
using UIAutomationClient;
using WindowsHinting.Logging;
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

            bool isAvailable = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsTogglePatternAvailablePropertyId);

            if (isAvailable)
            {
                try
                {
                    pattern = element.GetCachedPattern(UIA_PatternIds.UIA_TogglePatternId) as IUIAutomationTogglePattern;
                    if (pattern != null)
                    {
                        pattern.Toggle();
                        _logger.Info($"Successfully toggled element {element.CachedName}");
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