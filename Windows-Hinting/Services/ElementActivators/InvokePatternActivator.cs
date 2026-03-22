using System;
using System.Runtime.InteropServices;
using UIAutomationClient;
using WindowsHinting.Logging;
using WindowsHinting.Services;

namespace WindowsHinting.Services.ElementActivators
{
    internal sealed class InvokePatternActivator : IElementActivator
    {
        private readonly ILogger _logger;

        public InvokePatternActivator(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool TryActivate(IUIAutomationElement element)
        {
            IUIAutomationInvokePattern? pattern = null;
            bool isAvailable = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsInvokePatternAvailablePropertyId);

            if (isAvailable) 
            {
                try
                {
                    pattern = element.GetCachedPattern(UIA_PatternIds.UIA_InvokePatternId) as IUIAutomationInvokePattern;
                    if (pattern != null)
                    {
                        pattern.Invoke();
                        _logger.Info($"Successfully invoked element {element.CachedName}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"InvokePattern failed: {ex.Message}");
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