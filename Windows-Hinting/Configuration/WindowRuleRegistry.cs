using System;
using System.Collections.Generic;
using System.Linq;

namespace WindowsHinting.Configuration
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
        /// Default rules are always present; if a saved rule shares a default rule's
        /// <see cref="WindowRule.Name"/>, the saved properties are used.
        /// </summary>
        public void SetRules(IEnumerable<WindowRule> rules)
        {
            _rules.Clear();
            _rules.AddRange(MergeWithDefaults(rules));
        }

        /// <summary>
        /// Merges saved rules with the built-in defaults. Default rules always appear
        /// first in their original order. If a saved rule has the same Name as a default,
        /// its editable properties override the default. User rules follow after.
        /// </summary>
        public static List<WindowRule> MergeWithDefaults(IEnumerable<WindowRule>? savedRules)
        {
            var defaults = GetDefaultRules();
            var saved = savedRules?.ToList() ?? [];
            var merged = new List<WindowRule>();

            foreach (var def in defaults)
            {
                var override_ = saved.FirstOrDefault(r =>
                    !string.IsNullOrEmpty(r.Name) &&
                    string.Equals(r.Name, def.Name, StringComparison.OrdinalIgnoreCase));

                if (override_ != null)
                {
                    merged.Add(new WindowRule
                    {
                        Name = def.Name,
                        ExecutableName = override_.ExecutableName,
                        ClassName = override_.ClassName,
                        WindowTitle = override_.WindowTitle,
                        TitleMatchMode = override_.TitleMatchMode,
                        Strategy = override_.Strategy
                    });
                }
                else
                {
                    merged.Add(CloneRule(def));
                }
            }

            var defaultNames = new HashSet<string>(
                defaults.Select(d => d.Name!), StringComparer.OrdinalIgnoreCase);

            foreach (var rule in saved)
            {
                if (string.IsNullOrEmpty(rule.Name) || !defaultNames.Contains(rule.Name))
                    merged.Add(CloneRule(rule));
            }

            return merged;
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

                bool titleMatch;
                if (string.IsNullOrEmpty(rule.WindowTitle))
                {
                    titleMatch = true;
                }
                else if (rule.TitleMatchMode == TitleMatchMode.Contains)
                {
                    titleMatch = windowTitle != null &&
                        windowTitle.Contains(rule.WindowTitle, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    titleMatch = string.Equals(rule.WindowTitle, windowTitle, StringComparison.OrdinalIgnoreCase);
                }

                if (execMatch && classMatch && titleMatch)
                    return rule.Strategy;
            }

            return DefaultStrategy;
        }

        /// <summary>
        /// Returns true if the given name identifies a built-in default rule.
        /// </summary>
        public static bool IsDefaultRuleName(string name)
        {
            return GetDefaultRuleNames().Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the built-in default rules.
        /// </summary>
        public static List<WindowRule> GetDefaultRules() =>
        [
            new WindowRule
            {
                Name = "Windows Search",
                ExecutableName = "SearchHost",
                ClassName = "Windows.UI.Core.CoreWindow",
                Strategy = RootStrategy.ActiveWindowParent
            },
            new WindowRule
            {
                Name = "File Explorer",
                ExecutableName = "explorer",
                ClassName = "CabinetWClass",
                Strategy = RootStrategy.FileExplorerCustomStrategy,
                TitleMatchMode = TitleMatchMode.Contains
            }
        ];

        private static HashSet<string> GetDefaultRuleNames() =>
            new(GetDefaultRules().Select(r => r.Name!), StringComparer.OrdinalIgnoreCase);

        private static WindowRule CloneRule(WindowRule r) => new()
        {
            Name = r.Name,
            ExecutableName = r.ExecutableName,
            ClassName = r.ClassName,
            WindowTitle = r.WindowTitle,
            TitleMatchMode = r.TitleMatchMode,
            Strategy = r.Strategy
        };
    }
}
