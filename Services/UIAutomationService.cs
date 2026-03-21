using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HintOverlay.Configuration;
using HintOverlay.Logging;
using HintOverlay.Services.Native;
using UIAutomationClient;

namespace HintOverlay.Services
{
    internal sealed class UIAutomationService : IUIAutomationService
    {
        private readonly IUIAutomation _automation;
        private readonly ILogger _logger;
        private readonly WindowRuleRegistry _ruleRegistry;
        private bool _disposed;

        public UIAutomationService(ILogger logger, WindowRuleRegistry ruleRegistry)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ruleRegistry = ruleRegistry ?? throw new ArgumentNullException(nameof(ruleRegistry));
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

                // Resolve the root element strategy based on window rules
                root = ResolveRootElement(windowHandle, root);

                var clickableControlTypes = new int[]
                {
                    UIA_ControlTypeIds.UIA_ButtonControlTypeId,
                    UIA_ControlTypeIds.UIA_CheckBoxControlTypeId,
                    UIA_ControlTypeIds.UIA_ComboBoxControlTypeId,
                    UIA_ControlTypeIds.UIA_DataGridControlTypeId,
                    UIA_ControlTypeIds.UIA_DataItemControlTypeId,
                    UIA_ControlTypeIds.UIA_EditControlTypeId,
                    UIA_ControlTypeIds.UIA_GroupControlTypeId,
                    UIA_ControlTypeIds.UIA_HyperlinkControlTypeId,
                    UIA_ControlTypeIds.UIA_ListControlTypeId,
                    UIA_ControlTypeIds.UIA_ListItemControlTypeId,
                    UIA_ControlTypeIds.UIA_MenuControlTypeId,
                    UIA_ControlTypeIds.UIA_MenuItemControlTypeId,
                    UIA_ControlTypeIds.UIA_RadioButtonControlTypeId,
                    UIA_ControlTypeIds.UIA_SplitButtonControlTypeId,
                    UIA_ControlTypeIds.UIA_TabItemControlTypeId,
                    UIA_ControlTypeIds.UIA_TreeControlTypeId,
                    UIA_ControlTypeIds.UIA_TreeItemControlTypeId
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
        /// Resolves the root element to search based on the configured <see cref="WindowRuleRegistry"/> rules.
        /// </summary>
        private IUIAutomationElement ResolveRootElement(IntPtr windowHandle, IUIAutomationElement root)
        {
            if (root == null)
                return root;

            try
            {
                var className = root.CurrentClassName;
                var executableName = GetExecutableName(windowHandle);
                var windowTitle = GetWindowTitle(windowHandle);
                var strategy = _ruleRegistry.ResolveStrategy(executableName, className, windowTitle);

                _logger.Debug($"Window rule resolved: exe={executableName}, class={className}, title={windowTitle}, strategy={strategy}");

                if (strategy == RootStrategy.ActiveWindow)
                    return root;

                var resolved = ApplyStrategy(strategy, root);
                if (resolved != null && resolved != root)
                {
                    if (Marshal.IsComObject(root))
                    {
                        try { Marshal.ReleaseComObject(root); } catch { }
                    }
                    return resolved;
                }
            }
            catch (COMException ex)
            {
                _logger.Debug($"COM exception in ResolveRootElement: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Debug($"Exception in ResolveRootElement: {ex.Message}");
            }

            return root;
        }

        private IUIAutomationElement? ApplyStrategy(RootStrategy strategy, IUIAutomationElement root)
        {
            var walker = _automation.ControlViewWalker;

            switch (strategy)
            {
                case RootStrategy.ActiveWindowParent:
                {
                    var parent = walker.GetParentElement(root);
                    _logger.Debug(parent != null
                        ? "ActiveWindowParent: navigated to parent element"
                        : "ActiveWindowParent: no parent found, falling back to root");
                    return parent;
                }

                case RootStrategy.FocusedElement:
                {
                    var focused = _automation.GetFocusedElement();
                    _logger.Debug(focused != null
                        ? "FocusedElement: using focused element as root"
                        : "FocusedElement: no focused element, falling back to root");
                    return focused;
                }

                case RootStrategy.FocusedElementParent:
                {
                    var focused = _automation.GetFocusedElement();
                    if (focused == null)
                    {
                        _logger.Debug("FocusedElementParent: no focused element, falling back to root");
                        return null;
                    }
                    var parent = walker.GetParentElement(focused);
                    if (parent != null && Marshal.IsComObject(focused))
                    {
                        try { Marshal.ReleaseComObject(focused); } catch { }
                    }
                    _logger.Debug(parent != null
                        ? "FocusedElementParent: navigated to parent of focused element"
                        : "FocusedElementParent: no parent found, falling back to root");
                    return parent;
                }

                case RootStrategy.FocusedElementFirstParentWindow:
                {
                    var focused = _automation.GetFocusedElement();
                    if (focused == null)
                    {
                        _logger.Debug("FocusedElementFirstParentWindow: no focused element, falling back to root");
                        return null;
                    }
                    var current = focused;
                    IUIAutomationElement? windowAncestor = null;
                    while (true)
                    {
                        var parent = walker.GetParentElement(current);
                        if (parent == null)
                            break;

                        int controlType = parent.CurrentControlType;
                        if (controlType == UIA_ControlTypeIds.UIA_WindowControlTypeId)
                        {
                            windowAncestor = parent;
                            break;
                        }

                        if (current != focused && Marshal.IsComObject(current))
                        {
                            try { Marshal.ReleaseComObject(current); } catch { }
                        }
                        current = parent;
                    }
                    if (current != focused && current != windowAncestor && Marshal.IsComObject(current))
                    {
                        try { Marshal.ReleaseComObject(current); } catch { }
                    }
                    if (Marshal.IsComObject(focused) && focused != windowAncestor)
                    {
                        try { Marshal.ReleaseComObject(focused); } catch { }
                    }
                    _logger.Debug(windowAncestor != null
                        ? "FocusedElementFirstParentWindow: found Window ancestor"
                        : "FocusedElementFirstParentWindow: no Window ancestor found, falling back to root");
                    return windowAncestor;
                }

                default:
                    return null;
            }
        }

        private static string? GetExecutableName(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return null;

            try
            {
                NativeMethods.GetWindowThreadProcessId(windowHandle, out uint processId);
                if (processId == 0)
                    return null;

                using var process = Process.GetProcessById((int)processId);
                return process.ProcessName;
            }
            catch
            {
                return null;
            }
        }

        private static string? GetWindowTitle(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return null;

            const int maxLength = 256;
            var title = new System.Text.StringBuilder(maxLength);
            NativeMethods.GetWindowText(windowHandle, title, maxLength);
            var text = title.ToString();
            return string.IsNullOrEmpty(text) ? null : text;
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