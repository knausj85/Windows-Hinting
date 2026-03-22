namespace WindowsHinting.Configuration
{
    internal sealed class WindowRule
    {
        /// <summary>
        /// Display name for this rule. Built-in rules have a non-null name that
        /// is used to identify them; user-created rules may leave this null.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Process executable name to match (without extension), or null to match any.
        /// </summary>
        public string? ExecutableName { get; set; }

        /// <summary>
        /// UIA ClassName to match, or null to match any.
        /// </summary>
        public string? ClassName { get; set; }

        /// <summary>
        /// Window title to match, or null to match any.
        /// </summary>
        public string? WindowTitle { get; set; }

        /// <summary>
        /// How <see cref="WindowTitle"/> is matched against the actual window title.
        /// </summary>
        public TitleMatchMode TitleMatchMode { get; set; } = TitleMatchMode.Exact;

        /// <summary>
        /// The root element strategy to use when this rule matches.
        /// </summary>
        public RootStrategy Strategy { get; set; }

        /// <summary>
        /// Returns true if this is a built-in default rule (identified by having a non-null Name
        /// that matches one of the <see cref="WindowRuleRegistry.GetDefaultRules"/> entries).
        /// </summary>
        public bool IsDefault => Name != null && WindowRuleRegistry.IsDefaultRuleName(Name);
    }
}
