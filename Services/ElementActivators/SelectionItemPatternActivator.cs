using System;
using System.Runtime.InteropServices;
using HintOverlay.Logging;
using UIAutomationClient;

namespace HintOverlay.Services.ElementActivators
{
    internal sealed class SelectionItemPatternActivator : IElementActivator
    {
        private readonly ILogger _logger;

        public SelectionItemPatternActivator(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool TryActivate(IUIAutomationElement element)
        {
            IUIAutomationSelectionItemPattern? pattern = null;
            bool isAvailable = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsSelectionItemPatternAvailablePropertyId);

            if (isAvailable)
            {
                try
                {
                    pattern = element.GetCachedPattern(UIA_PatternIds.UIA_SelectionItemPatternId) as IUIAutomationSelectionItemPattern;
                    if (pattern != null)
                    {
                        pattern.Select();
                        _logger.Info($"Successfully selected element {element.CachedName}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"SelectionItemPattern failed: {ex.Message}");
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