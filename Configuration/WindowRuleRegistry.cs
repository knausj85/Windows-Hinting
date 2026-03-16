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
        /// Replaces all rules with the supplied collection.
        /// </summary>
        public void SetRules(IEnumerable<WindowRule> rules)
        {
            _rules.Clear();
            _rules.AddRange(rules);
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
        /// Returns the built-in default rules.
        /// </summary>
        public static List<WindowRule> GetDefaultRules() =>
        [
            new WindowRule
            {
                ExecutableName = "SearchHost",
                ClassName = "Windows.UI.Core.CoreWindow",
                Strategy = RootStrategy.ActiveWindowParent
            }
        ];
    }
}
