using System.Drawing;
// Ensure the correct UIAutomationClient reference is added to your project.
// If 'IUIAutomationElement' is not found, you may need to add a reference to 'UIAutomationClient.dll'.
// If the using directive below does not resolve the type, check your project references.
using UIAutomationClient;

namespace HintOverlay
{
    internal sealed class HintItem
    {
        public Rectangle Rect { get; init; }
        public string Label { get; init; } = "";
        public IUIAutomationElement Element { get; init; } = null!;

        public float CurrentOpacity { get; set; } = 1f;
        public float TargetOpacity { get; set; } = 1f;
    }
}
