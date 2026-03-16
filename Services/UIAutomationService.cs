using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HintOverlay.Logging;
using HintOverlay.Services.Native;
using UIAutomationClient;

namespace HintOverlay.Services
{
    internal sealed class UIAutomationService : IUIAutomationService
    {
        private readonly IUIAutomation _automation;
        private readonly ILogger _logger;
        private bool _disposed;

        public UIAutomationService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _automation = new CUIAutomation();
        }

        public IReadOnlyList<ClickableElement> FindClickableElements(IntPtr windowHandle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            
            using (PerformanceMetrics.Start("UIAutomationService.FindClickableElements", _logger, LogLevel.Info))
            {
                try
                {
                    return FindClickableElementsCore(windowHandle);
                }
                catch (COMException ex)
                {
                    _logger.Error($"UIA COM exception: {ex.Message}");
                    return Array.Empty<ClickableElement>();
                }
                catch (Exception ex)
                {
                    _logger.Error("Unexpected error in FindClickableElements", ex);
                    return Array.Empty<ClickableElement>();
                }
            }
        }

        public async Task<IReadOnlyList<ClickableElement>> FindClickableElementsAsync(IntPtr windowHandle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            
            return await Task.Run(() => FindClickableElements(windowHandle));
        }

        private IReadOnlyList<ClickableElement> FindClickableElementsCore(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                _logger.Debug("Window handle is zero");
                return Array.Empty<ClickableElement>();
            }

            IUIAutomationElement? root = null;
            IUIAutomationCondition? combinedCondition = null;
            IUIAutomationCondition? statusAndCondition = null;
            IUIAutomationCondition? controlTypeOrCondition = null;
            IUIAutomationCacheRequest? cache = null;
            IUIAutomationElementArray? foundElements = null;
            var conditionsToRelease = new List<IUIAutomationCondition>();

            try
            {
                using (PerformanceMetrics.Start("ElementFromHandle", _logger, LogLevel.Debug))
                {
                    root = _automation.ElementFromHandle(windowHandle);
                    if (root == null)
                    {
                        _logger.Warning("Failed to get root element from window handle");
                        return Array.Empty<ClickableElement>();
                    }
                }

                // Special case: Start Menu (CoreWindow class with "Search" element)
                // For Start Menu, search the parent element instead of descendants
                root = HandleStartMenuSpecialCase(root);

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
                    UIA_ControlTypeIds.UIA_GroupControlTypeId,
                    UIA_ControlTypeIds.UIA_ListControlTypeId,
                    UIA_ControlTypeIds.UIA_TreeControlTypeId,
                };

                using (PerformanceMetrics.Start("BuildSearchConditions", _logger, LogLevel.Debug))
                {
                    var enabledCondition = _automation.CreatePropertyCondition(UIA_PropertyIds.UIA_IsEnabledPropertyId, true);
                    var onscreenCondition = _automation.CreatePropertyCondition(UIA_PropertyIds.UIA_IsOffscreenPropertyId, false);
                    conditionsToRelease.Add(enabledCondition);
                    conditionsToRelease.Add(onscreenCondition);

                    statusAndCondition = _automation.CreateAndCondition(enabledCondition, onscreenCondition);

                    var controlTypeConditions = clickableControlTypes
                        .Select(t => _automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ControlTypePropertyId, t))
                        .ToArray();
                    conditionsToRelease.AddRange(controlTypeConditions);

                    controlTypeOrCondition = _automation.CreateOrConditionFromArray(controlTypeConditions);
                    combinedCondition = _automation.CreateAndCondition(statusAndCondition, controlTypeOrCondition);
                }

                using (PerformanceMetrics.Start("CreateCacheRequest", _logger, LogLevel.Debug))
                {
                    cache = _automation.CreateCacheRequest();
                    cache.TreeScope = TreeScope.TreeScope_Element;
                    cache.AddProperty(UIA_PropertyIds.UIA_BoundingRectanglePropertyId);
                    cache.AddProperty(UIA_PropertyIds.UIA_ControlTypePropertyId);
                    cache.AddProperty(UIA_PropertyIds.UIA_IsTogglePatternAvailablePropertyId);
                    cache.AddProperty(UIA_PropertyIds.UIA_IsInvokePatternAvailablePropertyId);
                    cache.AddProperty(UIA_PropertyIds.UIA_IsExpandCollapsePatternAvailablePropertyId);
                    cache.AddProperty(UIA_PropertyIds.UIA_IsKeyboardFocusablePropertyId);
                    cache.AddProperty(UIA_PropertyIds.UIA_IsSelectionItemPatternAvailablePropertyId);
                    cache.AddProperty(UIA_PropertyIds.UIA_NamePropertyId);
                    cache.AddProperty(UIA_PropertyIds.UIA_ClassNamePropertyId);
                    cache.AddPattern(UIA_PatternIds.UIA_InvokePatternId);
                    cache.AddPattern(UIA_PatternIds.UIA_ExpandCollapsePatternId);
                    cache.AddPattern(UIA_PatternIds.UIA_SelectionPatternId);
                    cache.AddPattern(UIA_PatternIds.UIA_SelectionItemPatternId);
                    cache.AddPattern(UIA_PatternIds.UIA_TogglePatternId);
                }

                using (PerformanceMetrics.Start("FindAllBuildCache", _logger, LogLevel.Info))
                {
                    foundElements = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, combinedCondition, cache);
                    if (foundElements == null)
                    {
                        _logger.Debug("FindAllBuildCache returned null");
                        return Array.Empty<ClickableElement>();
                    }
                }

                var results = new List<ClickableElement>();
                int elementCount = foundElements.Length;
                _logger.Debug($"Processing {elementCount} found elements");

                using (PerformanceMetrics.Start($"ProcessElements({elementCount})", _logger, LogLevel.Debug))
                {
                    for (int i = 0; i < elementCount; i++)
                    {
                        IUIAutomationElement? element = null;
                        try
                        {
                            element = foundElements.GetElement(i);
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

                                if (rect.Width > 0 && rect.Height > 0
                                    && HasActivatablePattern(element))
                                {
                                    results.Add(new ClickableElement
                                    {
                                        Element = element,
                                        Bounds = rect
                                    });
                                    element = null; // Don't release - ownership transferred to ClickableElement
                                }
                            }
                        }
                        catch (COMException ex)
                        {
                            _logger.Debug($"COM exception processing element {i}: {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Exception processing element {i}: {ex.Message}");
                        }
                        finally
                        {
                            // Only release if we didn't transfer ownership
                            if (element != null && Marshal.IsComObject(element))
                            {
                                Marshal.ReleaseComObject(element);
                            }
                        }
                    }
                }

                _logger.Info($"Found {results.Count} valid clickable elements");
                return results;
            }
            finally
            {
                // Release all COM objects
                if (foundElements != null && Marshal.IsComObject(foundElements))
                    Marshal.ReleaseComObject(foundElements);
                
                if (cache != null && Marshal.IsComObject(cache))
                    Marshal.ReleaseComObject(cache);
                
                if (combinedCondition != null && Marshal.IsComObject(combinedCondition))
                    Marshal.ReleaseComObject(combinedCondition);
                
                if (controlTypeOrCondition != null && Marshal.IsComObject(controlTypeOrCondition))
                    Marshal.ReleaseComObject(controlTypeOrCondition);
                
                if (statusAndCondition != null && Marshal.IsComObject(statusAndCondition))
                    Marshal.ReleaseComObject(statusAndCondition);
                
                foreach (var condition in conditionsToRelease)
                {
                    if (condition != null && Marshal.IsComObject(condition))
                        Marshal.ReleaseComObject(condition);
                }
                
                if (root != null && Marshal.IsComObject(root))
                    Marshal.ReleaseComObject(root);
            }
        }

        /// <summary>
        /// Special handling for Start Menu (CoreWindow class).
        /// When the root element is a CoreWindow with "Search" as name or the foreground window title contains "Search",
        /// search from the parent element instead.
        /// </summary>
        private IUIAutomationElement HandleStartMenuSpecialCase(IUIAutomationElement root)
        {
            if (root == null)
                return root;

            try
            {
                // Check if this is a CoreWindow

                // Get the foreground window title for additional matching
                var foregroundWindowHandle = NativeMethods.GetForegroundWindow();
                var className = root.CurrentClassName;
                var windowTitle = GetWindowTitle(foregroundWindowHandle);
                _logger.Debug($"Detected CoreWindow (likely Start Menu). class = {className} title = {windowTitle}");

                bool isStartMenuWindow = root.CurrentClassName == "Windows.UI.Core.CoreWindow" && (GetWindowTitle(foregroundWindowHandle) == ("Search"));

                if (isStartMenuWindow)
                {

                    // Try to get the parent element
                    var treeWalker = _automation.ControlViewWalker;
                    var parentElement = treeWalker.GetParentElement(root);

                    if (parentElement != null)
                    {
                        _logger.Debug("Found parent of CoreWindow, will search from parent");
                        // Release the old root since we're replacing it
                        if (root != null && Marshal.IsComObject(root))
                        {
                            try { Marshal.ReleaseComObject(root); } catch { }
                        }
                        return parentElement;
                    }
                }
            }
            catch (COMException ex)
            {
                _logger.Debug($"COM exception in HandleStartMenuSpecialCase: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Debug($"Exception in HandleStartMenuSpecialCase: {ex.Message}");
            }

            return root;
        }

        /// <summary>
        /// Get the window title for a given window handle.
        /// </summary>
        private string GetWindowTitle(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return string.Empty;

            const int maxLength = 256;
            var title = new System.Text.StringBuilder(maxLength);
            NativeMethods.GetWindowText(windowHandle, title, maxLength);
            return title.ToString();
        }

        private bool HasActivatablePattern(IUIAutomationElement element)
        {
            return element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsInvokePatternAvailablePropertyId) is true
                || element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsExpandCollapsePatternAvailablePropertyId) is true
                || element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsSelectionItemPatternAvailablePropertyId) is true
                || element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsTogglePatternAvailablePropertyId) is true
                || element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsKeyboardFocusablePropertyId) is true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            if (_automation != null && Marshal.IsComObject(_automation))
            {
                Marshal.ReleaseComObject(_automation);
            }

            _disposed = true;
        }
    }
}