using System.Drawing;
using System.Runtime.InteropServices;
using UIAutomationClient;

namespace WindowsHinting.NativeInterop
{
    internal sealed class UIAutomationWrapper : IDisposable
    {
        private readonly IUIAutomation _automation;

        public UIAutomationWrapper()
        {
            _automation = new CUIAutomation();
        }

        public void Dispose()
        {
            if (_automation != null)
            {
                Marshal.ReleaseComObject(_automation);
            }
        }

        public IEnumerable<ClickableElement> FindClickableElements(IntPtr windowHandle)
        {
            var results = new List<ClickableElement>();

            try
            {
                var rootElement = _automation.ElementFromHandle(windowHandle);
                if (rootElement == null)
                {
                    return results;
                }

                var condition = _automation.CreateTrueCondition();
                var walker = _automation.CreateTreeWalker(condition);

                TraverseElements(rootElement, walker, results);

                Marshal.ReleaseComObject(rootElement);
                Marshal.ReleaseComObject(condition);
                Marshal.ReleaseComObject(walker);
            }
            catch
            {
                // Handle exceptions silently or log
            }

            return results;
        }

        private void TraverseElements(IUIAutomationElement element, IUIAutomationTreeWalker walker, List<ClickableElement> results)
        {
            if (element == null) return;

            try
            {
                var controlType = element.CurrentControlType;
                var isEnabled = element.CurrentIsEnabled;

                // Check if element is clickable (buttons, links, etc.)
                if (isEnabled != 0 && (controlType == UIA_ControlTypeIds.UIA_ButtonControlTypeId ||
                    controlType == UIA_ControlTypeIds.UIA_HyperlinkControlTypeId ||
                    controlType == UIA_ControlTypeIds.UIA_MenuItemControlTypeId))
                {
                    var rect = element.CurrentBoundingRectangle;
                    var bounds = new Rectangle(
                        (int)rect.left,
                        (int)rect.top,
                        (int)(rect.right - rect.left),
                        (int)(rect.bottom - rect.top)
                    );

                    results.Add(new ClickableElement(bounds, element));
                }

                var child = walker.GetFirstChildElement(element);
                while (child != null)
                {
                    TraverseElements(child, walker, results);
                    var next = walker.GetNextSiblingElement(child);
                    Marshal.ReleaseComObject(child);
                    child = next;
                }
            }
            catch
            {
                // Handle traversal errors
            }
        }
    }

    internal record ClickableElement(Rectangle Bounds, IUIAutomationElement Element);
}
