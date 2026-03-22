namespace WindowsHinting.Configuration
{
    internal sealed class HintOverlaySettings
    {
        public const string SectionName = "Windows-Hinting";

        public string LogLevel { get; set; } = "Debug";
        public bool EnablePerformanceMetrics { get; set; } = true;
    }
}
