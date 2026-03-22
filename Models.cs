using System.Drawing;
using HintOverlay.Models;
using UIAutomationClient;

namespace HintOverlay
{
    internal sealed class HintItem
    {
        public Rectangle Rect { get; init; }
        public string Label { get; init; } = "";
        public IUIAutomationElement Element { get; init; } = null!;
        /// <summary>
        /// Per-element label position resolved by the deduplicator.
        /// May differ from the global preference when nudged to avoid overlap.
        /// </summary>
        public HintPosition LabelPosition { get; init; } = HintPosition.UpperLeft;

        public float CurrentOpacity { get; set; } = 1f;
        public float TargetOpacity { get; set; } = 1f;
    }

    internal sealed record ClickableElement(Rectangle Bounds, IUIAutomationElement Element);
}
