using System.Drawing;
using UIAutomationClient;

namespace WindowsHinting.Models
{
    internal sealed class HintItem
    {
        public Rectangle Rect { get; init; }
        public string Label { get; init; } = "";
        public IUIAutomationElement Element { get; init; } = null!;
        public string DisplayName { get; init; } = "";
        public bool IsSelected { get; set; }

        public float CurrentOpacity { get; set; } = 1f;
        public float TargetOpacity { get; set; } = 1f;
    }
}
