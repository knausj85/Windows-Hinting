using System;
using System.Collections.Generic;

namespace HintOverlay.Configuration
{
    internal sealed class WindowRuleRegistry
    {
        private readonly List<WindowRule> _rules = new();

        public RootStrategy DefaultStrategy { get; } = RootStrategy.ActiveWindow;

        public IReadOnlyList<WindowRule> Rules => _rules;

        public void AddRule(WindowRule rule)
        {
            ArgumentNullException.ThrowIfNull(rule);
            _rules.Add(rule);
        }

        /// <summary>
        /// Returns the strategy for the first matching rule, or <see cref="DefaultStrategy"/> if none match.
        /// A rule matches when every non-null criterion equals the supplied value (case-insensitive).
        /// </summary>
        public RootStrategy ResolveStrategy(string? executableName, string? className, string? windowTitle)
        {
            foreach (var rule in _rules)
            {
                bool execMatch = string.IsNullOrEmpty(rule.ExecutableName) ||
                    string.Equals(rule.ExecutableName, executableName, StringComparison.OrdinalIgnoreCase);

                bool classMatch = string.IsNullOrEmpty(rule.ClassName) ||
                    string.Equals(rule.ClassName, className, StringComparison.OrdinalIgnoreCase);

                bool titleMatch = string.IsNullOrEmpty(rule.WindowTitle) ||
                    string.Equals(rule.WindowTitle, windowTitle, StringComparison.OrdinalIgnoreCase);

                if (execMatch && classMatch && titleMatch)
                    return rule.Strategy;
            }

            return DefaultStrategy;
        }

        /// <summary>
        /// Creates a registry pre-populated with the built-in rules.
        /// </summary>
        public static WindowRuleRegistry CreateRegistry()
        {
            var registry = new WindowRuleRegistry();

            // Start Menu / Windows Search surfaces use a CoreWindow that only
            // exposes its full element tree from the parent.
            registry.AddRule(new WindowRule
            {
                ExecutableName = "SearchHost",
                ClassName = "Windows.UI.Core.CoreWindow",
                Strategy = RootStrategy.ActiveWindowParent
            });

            registry.AddRule(new WindowRule
            {
                Strategy = RootStrategy.ActiveWindow
            });

            return registry;
        }
    }
}
