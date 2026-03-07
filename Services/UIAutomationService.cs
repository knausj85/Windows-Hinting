using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using UIAutomationClient;

namespace HintOverlay.Services
{   
    internal sealed class UIAutomationService : IUIAutomationService
    {
        private readonly IUIAutomation _automation;
        
        public UIAutomationService()
        {
            _automation = new CUIAutomation();
        }

        public IReadOnlyList<ClickableElement> FindClickableElements(IntPtr windowHandle)
        {
            try
            {
                return FindClickableElementsCore(windowHandle);
            }
            catch (COMException ex)
            {
                Debug.WriteLine($"UIA COM exception: {ex.Message}");
                return Array.Empty<ClickableElement>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error: {ex.Message}");
                return Array.Empty<ClickableElement>();
            }
        }

        private IReadOnlyList<ClickableElement> FindClickableElementsCore(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return Array.Empty<ClickableElement>();

            var root = _automation.ElementFromHandle(windowHandle);
            if (root == null)
                return Array.Empty<ClickableElement>();

            var clickableControlTypes = new int[]
            {
                50000, // Button
                50002, // CheckBox
                50003, // ComboBox
                50004, // Edit
                50005, // Hyperlink
                50007, // ListItem
                50009, // Menu
                50011, // MenuItem
                50013, // RadioButton
                50019, // TabItem
                50024, // TreeItem
                50031, // SplitButton
            };

            var statusConditions = new List<IUIAutomationCondition>
            {
                _automation.CreatePropertyCondition(30010, true),  // UIA_IsEnabledPropertyId
                _automation.CreatePropertyCondition(30022, false), // UIA_IsOffscreenPropertyId
            };

            var statusAndCondition = _automation.CreateAndConditionFromArray(statusConditions.ToArray());

            var controlTypeConditions = clickableControlTypes
                .Select(t => _automation.CreatePropertyCondition(30003, t))
                .ToArray();

            var controlTypeOrCondition = _automation.CreateOrConditionFromArray(controlTypeConditions);
            var combinedCondition = _automation.CreateAndCondition(statusAndCondition, controlTypeOrCondition);

            var cache = _automation.CreateCacheRequest();
            cache.TreeScope = TreeScope.TreeScope_Element;
            cache.AddProperty(30001); // UIA_BoundingRectanglePropertyId
            cache.AddProperty(30003); // UIA_ControlTypePropertyId
            cache.AddProperty(30041); // UIA_IsTogglePatternAvailablePropertyId
            cache.AddProperty(30031); // UIA_IsInvokePatternAvailablePropertyId
            cache.AddProperty(30028); // UIA_IsExpandCollapsePatternAvailablePropertyId
            cache.AddProperty(30036); // UIA_IsSelectionItemPatternAvailablePropertyId
            cache.AddPattern(10000);  // Toggle
            cache.AddPattern(10005);  // ExpandCollapse
            cache.AddPattern(10010);  // SelectionItem
            cache.AddPattern(10015);  // Invoke

            var elements = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, combinedCondition, cache);
            if (elements == null)
                return Array.Empty<ClickableElement>();

            var result = new List<ClickableElement>();

            for (int i = 0; i < elements.Length; i++)
            {
                var element = elements.GetElement(i);
                if (element == null)
                    continue;

                tagRECT rect = element.CachedBoundingRectangle;

                if (rect.right > rect.left && rect.bottom > rect.top)
                {
                    result.Add(new ClickableElement(
                        new Rectangle(
                            (int)rect.left,
                            (int)rect.top,
                            (int)(rect.right - rect.left),
                            (int)(rect.bottom - rect.top)),
                        element));
                }
            }

            return result;
        }
    }
}