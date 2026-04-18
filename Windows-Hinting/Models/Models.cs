using System.Drawing;
using UIAutomationClient;

namespace WindowsHinting.Models
{
    internal sealed class HintItem
    {
        public Rectangle Rect { get; init; }
        public string Label { get; init; } = "";
        public IUIAutomationElement Element { get; init; } = null!;

        /// <summary>
        /// True when this hint represents a top-level item of an application
        /// menu bar (File / Edit / View ...). Activation should move the
        /// mouse rather than click so that menu tracking transitions the
        /// drop-down instead of dismissing it.
        /// </summary>
        public bool IsMenuBarRootItem { get; init; }

        public float CurrentOpacity { get; set; } = 1f;
        public float TargetOpacity { get; set; } = 1f;
    }
}
