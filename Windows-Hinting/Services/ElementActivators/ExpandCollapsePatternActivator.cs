using System;
using System.Runtime.InteropServices;
using UIAutomationClient;
using WindowsHinting.Logging;
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

            bool isAvailable = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsExpandCollapsePatternAvailablePropertyId);

            if (isAvailable)
            {
                try
                {
                    pattern = element.GetCachedPattern(UIA_PatternIds.UIA_ExpandCollapsePatternId) as IUIAutomationExpandCollapsePattern;
                    if (pattern != null)
                    {
                        pattern.Expand();
                        _logger.Info($"Successfully expanded/collapsed element {element.CachedName}");
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