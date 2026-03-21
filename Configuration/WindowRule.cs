namespace HintOverlay.Configuration
{
    internal sealed class WindowRule
    {
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
    }
}
