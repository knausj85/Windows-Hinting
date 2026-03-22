using System;
using System.Collections.Generic;
using UIAutomationClient;
using WindowsHinting.Logging;
using WindowsHinting.Services.ElementActivators;

namespace WindowsHinting.Services
{
    internal sealed class ElementActivatorChain
    {
        private readonly IReadOnlyList<IElementActivator> _activators;
        private readonly ILogger _logger;

        public ElementActivatorChain(ILogger logger, MouseClickService mouseClickService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _activators = new IElementActivator[]
            {
                new InvokePatternActivator(logger),
                new ExpandCollapsePatternActivator(logger),
                new SelectionItemPatternActivator(logger),
                new TogglePatternActivator(logger),
                new MouseClickActivator(mouseClickService, logger) // Fallback: simulate a left click
            };
        }

        public bool TryActivate(IUIAutomationElement element)
        {
            LogCachedPatterns(element);

            foreach (var activator in _activators)
            {
                if (activator.TryActivate(element))
                {
                    return true;
                }
            }
            
            _logger.Warning("No interaction pattern succeeded for element");
            return false;
        }

        private void LogCachedPatterns(IUIAutomationElement element)
        {
            try
            {
                var name = element.CachedName ?? "(unnamed)";
                bool isInvoke = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsInvokePatternAvailablePropertyId) is true;
                bool isExpandCollapse = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsExpandCollapsePatternAvailablePropertyId) is true;
                bool isSelectionItem = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsSelectionItemPatternAvailablePropertyId) is true;
                bool isToggle = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsTogglePatternAvailablePropertyId) is true;
                bool isKeyboardFocusable = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsKeyboardFocusablePropertyId) is true;

                _logger.Info($"Cached patterns for '{name}': " +
                    $"Invoke={isInvoke}, " +
                    $"ExpandCollapse={isExpandCollapse}, " +
                    $"SelectionItem={isSelectionItem}, " +
                    $"Toggle={isToggle}, " +
                    $"KeyboardFocusable={isKeyboardFocusable}");
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to read cached patterns: {ex.Message}");
            }
        }
    }
}