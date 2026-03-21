namespace HintOverlay.Configuration
{
    internal sealed class HintOverlaySettings
    {
        public const string SectionName = "HintOverlay";
        
        public string LogLevel { get; set; } = "Debug";
        public bool EnablePerformanceMetrics { get; set; } = true;
    }
}