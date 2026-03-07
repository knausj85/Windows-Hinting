using System;
using System.Collections.Generic;
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
            using (PerformanceMetrics.Start("UIAutomationService.Constructor", LogLevel.Debug))
            {
                _automation = new CUIAutomation();
            }
        }

        public IReadOnlyList<ClickableElement> FindClickableElements(IntPtr windowHandle)
        {
            using (PerformanceMetrics.Start("UIAutomationService.FindClickableElements", LogLevel.Info))
            {
                try
                {
                    return FindClickableElementsCore(windowHandle);
                }
                catch (COMException ex)
                {
                    Logger.Error($"UIA COM exception: {ex.Message}");
                    return Array.Empty<ClickableElement>();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Unexpected error in FindClickableElements", ex);
                    return Array.Empty<ClickableElement>();
                }
            }
        }

        private IReadOnlyList<ClickableElement> FindClickableElementsCore(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                Logger.Debug("Window handle is zero");
                return Array.Empty<ClickableElement>();
            }

            IUIAutomationElement root;
            using (PerformanceMetrics.Start("ElementFromHandle", LogLevel.Debug))
            {
                root = _automation.ElementFromHandle(windowHandle);
                if (root == null)
                {
                    Logger.Warning("Failed to get root element from window handle");
                    return Array.Empty<ClickableElement>();
                }
            }

            var clickableControlTypes = new int[]
            {
                UIA_ControlTypeIds.UIA_ButtonControlTypeId,
                UIA_ControlTypeIds.UIA_CheckBoxControlTypeId,
                UIA_ControlTypeIds.UIA_ComboBoxControlTypeId,
                UIA_ControlTypeIds.UIA_EditControlTypeId,
                UIA_ControlTypeIds.UIA_HyperlinkControlTypeId,
                UIA_ControlTypeIds.UIA_ListItemControlTypeId,
                UIA_ControlTypeIds.UIA_MenuControlTypeId,
                UIA_ControlTypeIds.UIA_MenuItemControlTypeId,
                UIA_ControlTypeIds.UIA_RadioButtonControlTypeId,
                UIA_ControlTypeIds.UIA_TabItemControlTypeId,
                UIA_ControlTypeIds.UIA_TreeItemControlTypeId,
                UIA_ControlTypeIds.UIA_SplitButtonControlTypeId,
            };

            IUIAutomationCondition combinedCondition;
            using (PerformanceMetrics.Start("BuildSearchConditions", LogLevel.Debug))
            {
                var statusConditions = new List<IUIAutomationCondition>
                {
                    _automation.CreatePropertyCondition(UIA_PropertyIds.UIA_IsEnabledPropertyId, true),
                    _automation.CreatePropertyCondition(UIA_PropertyIds.UIA_IsOffscreenPropertyId, false),
                };

                var statusAndCondition = _automation.CreateAndConditionFromArray(statusConditions.ToArray());

                var controlTypeConditions = clickableControlTypes
                    .Select(t => _automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ControlTypePropertyId, t))
                    .ToArray();

                var controlTypeOrCondition = _automation.CreateOrConditionFromArray(controlTypeConditions);
                combinedCondition = _automation.CreateAndCondition(statusAndCondition, controlTypeOrCondition);
            }

            IUIAutomationCacheRequest cache;
            using (PerformanceMetrics.Start("CreateCacheRequest", LogLevel.Debug))
            {
                cache = _automation.CreateCacheRequest();
                cache.TreeScope = TreeScope.TreeScope_Element;
                cache.AddProperty(UIA_PropertyIds.UIA_BoundingRectanglePropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_ControlTypePropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_IsTogglePatternAvailablePropertyId);
                cache.AddProperty(UIA_PropertyIds.UIA_IsInvokePatternAvailablePropertyId);
                cache.AddPattern(UIA_PatternIds.UIA_InvokePatternId);
                cache.AddPattern(UIA_PatternIds.UIA_ExpandCollapsePatternId);
                cache.AddPattern(UIA_PatternIds.UIA_SelectionItemPatternId);
                cache.AddPattern(UIA_PatternIds.UIA_TogglePatternId);
            }

            IUIAutomationElementArray foundElements;
            using (PerformanceMetrics.Start("FindAllBuildCache", LogLevel.Info))
            {
                foundElements = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, combinedCondition, cache);
                if (foundElements == null)
                {
                    Logger.Debug("FindAllBuildCache returned null");
                    return Array.Empty<ClickableElement>();
                }
            }

            var results = new List<ClickableElement>();
            int elementCount = foundElements.Length;
            Logger.Debug($"Processing {elementCount} found elements");

            using (PerformanceMetrics.Start($"ProcessElements({elementCount})", LogLevel.Debug))
            {
                for (int i = 0; i < elementCount; i++)
                {
                    try
                    {
                        var element = foundElements.GetElement(i);
                        if (element == null)
                            continue;

                        var rectObj = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_BoundingRectanglePropertyId);
                        if (rectObj == null)
                            continue;

                        if (rectObj is double[] rectArray && rectArray.Length == 4)
                        {
                            var rect = new Rectangle(
                                (int)rectArray[0],
                                (int)rectArray[1],
                                (int)rectArray[2],
                                (int)rectArray[3]
                            );

                            if (rect.Width > 0 && rect.Height > 0)
                            {
                                results.Add(new ClickableElement
                                {
                                    Element = element,
                                    Bounds = rect
                                });
                            }
                        }
                    }
                    catch (COMException ex)
                    {
                        Logger.Debug($"COM exception processing element {i}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Exception processing element {i}: {ex.Message}");
                    }
                }
            }

            Logger.Info($"Found {results.Count} valid clickable elements");
            return results;
        }
    }
}