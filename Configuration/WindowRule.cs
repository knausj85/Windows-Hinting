namespace HintOverlay.Configuration
{
    internal sealed class WindowRule
    {
        /// <summary>
        /// Process executable name to match (without extension), or null to match any.
        /// </summary>
        public string? ExecutableName { get; init; }

        /// <summary>
        /// UIA ClassName to match, or null to match any.
        /// </summary>
        public string? ClassName { get; init; }

        /// <summary>
        /// Window title to match, or null to match any.
        /// </summary>
        public string? WindowTitle { get; init; }

        /// <summary>
        /// The root element strategy to use when this rule matches.
        /// </summary>
        public RootStrategy Strategy { get; init; }
    }
}
