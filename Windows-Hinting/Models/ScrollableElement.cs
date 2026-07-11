using System.Drawing;
using UIAutomationClient;

namespace WindowsHinting.Models
{
    /// <summary>
    /// Represents a discovered scrollable element with UI Automation pattern capabilities.
    /// </summary>
    public sealed class ScrollableElement
    {
        public IUIAutomationElement Element { get; init; } = null!;
        public Rectangle Bounds { get; init; }
        public int ControlType { get; init; }
        public string Name { get; init; } = "";
        public bool HasScrollPattern { get; init; }
        public bool HasRangeValuePattern { get; init; }
        public bool IsHorizontallyScrollable { get; init; }
        public bool IsVerticallyScrollable { get; init; }
    }
}
