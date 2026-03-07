using System;
using System.Collections.Generic;
using HintOverlay.Logging;
using HintOverlay.Services.ElementActivators;
using UIAutomationClient;

namespace HintOverlay.Services
{
    internal sealed class ElementActivatorChain
    {
        private readonly IReadOnlyList<IElementActivator> _activators;
        private readonly ILogger _logger;

        public ElementActivatorChain(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _activators = new IElementActivator[]
            {
                new InvokePatternActivator(logger),
                new ExpandCollapsePatternActivator(logger),
                new SelectionItemPatternActivator(logger),
                new TogglePatternActivator(logger)
            };
        }

        public bool TryActivate(IUIAutomationElement element)
        {
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
    }
}