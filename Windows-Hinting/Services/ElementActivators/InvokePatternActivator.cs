using System;
using System.Runtime.InteropServices;
using Windows.Win32.UI.Accessibility;
using WindowsHinting.Logging;
using WindowsHinting.NativeInterop;
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
            bool isAvailable = element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsInvokePatternAvailablePropertyId) is true;

            if (isAvailable)
            {
                // Skip InvokePattern for same-process elements.  Invoke() sends a
                // synchronous COM message to the target window, which deadlocks when
                // the target lives on the same UI thread (e.g., a modal dialog).
                // Let the MouseClickActivator fallback handle these instead.
                try
                {
                    int elementPid = (int)element.GetCachedPropertyValue(
                        UIA_PROPERTY_ID.UIA_ProcessIdPropertyId);
                    if (elementPid == Environment.ProcessId)
                    {
                        _logger.Debug($"Skipping InvokePattern for same-process element '{element.GetCachedName()}'");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Could not determine element PID, proceeding with Invoke: {ex.Message}");
                }

                try
                {
                    pattern = element.GetCachedPattern(UIA_PATTERN_ID.UIA_InvokePatternId) as IUIAutomationInvokePattern;
                    if (pattern != null)
                    {
                        pattern.Invoke();
                        _logger.Info($"Successfully invoked element {element.GetCachedName()}");
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
