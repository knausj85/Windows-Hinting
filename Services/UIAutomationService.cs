using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
            var conditionsToRelease = new List<IUIAutomationCondition>();
            var elementArraysToRelease = new List<IUIAutomationElementArray>();
            var roots = new List<IUIAutomationElement>();

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

                // Resolve the root element(s) strategy based on window rules
                roots = ResolveRootElements(windowHandle, root);

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

                var results = new List<ClickableElement>();

                using (PerformanceMetrics.Start("FindAllBuildCache", _logger, LogLevel.Info))
                {
                    foreach (var scanRoot in roots)
                    {
                        IUIAutomationElementArray? found = null;
                        try
                        {
                            found = scanRoot.FindAllBuildCache(TreeScope.TreeScope_Descendants, combinedCondition, cache);
                        }
                        catch (COMException ex)
                        {
                            _logger.Debug($"FindAllBuildCache failed for a root: {ex.Message}");
                        }

                        if (found != null)
                            elementArraysToRelease.Add(found);
                    }

                    if (elementArraysToRelease.Count == 0)
                    {
                        _logger.Debug("FindAllBuildCache returned no results");
                        return Array.Empty<ClickableElement>();
                    }
                }

                int totalElements = elementArraysToRelease.Sum(a => a.Length);
                _logger.Debug($"Processing {totalElements} found elements across {elementArraysToRelease.Count} root(s)");

                using (PerformanceMetrics.Start($"ProcessElements({totalElements})", _logger, LogLevel.Debug))
                {
                    foreach (var elemArray in elementArraysToRelease)
                    {
                        int elementCount = elemArray.Length;
                        for (int i = 0; i < elementCount; i++)
                        {
                            IUIAutomationElement? element = null;
                            try
                            {
                                element = elemArray.GetElement(i);
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
                }

                _logger.Info($"Found {results.Count} valid clickable elements");
                return results;
            }
            finally
            {
                // Release all COM objects
                foreach (var elemArray in elementArraysToRelease)
                {
                    if (elemArray != null && Marshal.IsComObject(elemArray))
                        Marshal.ReleaseComObject(elemArray);
                }

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

                foreach (var r in roots)
                {
                    if (r != null && Marshal.IsComObject(r))
                    {
                        try { Marshal.ReleaseComObject(r); } catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Resolves the root element(s) to search based on the configured <see cref="WindowRuleRegistry"/> rules.
        /// Most strategies return a single root; <see cref="RootStrategy.FileExplorerCustomStrategy"/> may return multiple.
        /// </summary>
        private List<IUIAutomationElement> ResolveRootElements(IntPtr windowHandle, IUIAutomationElement root)
        {
            if (root == null)
                return [root];

            try
            {
                var className = root.CurrentClassName;
                var executableName = GetExecutableName(windowHandle);
                var windowTitle = GetWindowTitle(windowHandle);
                var strategy = _ruleRegistry.ResolveStrategy(executableName, className, windowTitle);

                _logger.Info($"Window rule resolved: exe={executableName}, class={className}, title={windowTitle}, strategy={strategy}");

                switch (strategy)
                {
                    case RootStrategy.ActiveWindow:
                        return [root];

                    case RootStrategy.FileExplorerCustomStrategy:
                        return ResolveFileExplorerActiveTab(root, windowTitle);

                    default:
                    {
                        var resolved = ApplyStrategy(strategy, root);
                        if (resolved != null && resolved != root)
                        {
                            if (Marshal.IsComObject(root))
                            {
                                try { Marshal.ReleaseComObject(root); } catch { }
                            }
                            return [resolved];
                        }
                        break;
                    }
                }
            }
            catch (COMException ex)
            {
                _logger.Debug($"COM exception in ResolveRootElements: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Debug($"Exception in ResolveRootElements: {ex.Message}");
            }

            return [root];
        }

        private static readonly Regex MoreTabsPattern = new(@"\s+and\s+\d+\s+more\s+tab.*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// For File Explorer with tabs, returns only the children that belong to the active tab
        /// (matched by window title) plus any unnamed children (chrome elements like toolbars/address bar).
        /// This avoids scanning the UIA subtrees of inactive tabs.
        /// </summary>
        private List<IUIAutomationElement> ResolveFileExplorerActiveTab(IUIAutomationElement root, string? windowTitle)
        {
            var targets = new List<IUIAutomationElement>();
            var walker = _automation.ControlViewWalker;

            string activeTabName = "";
            if (!string.IsNullOrEmpty(windowTitle))
            {
                activeTabName = windowTitle.Replace("- File Explorer", "").Trim();
                activeTabName = MoreTabsPattern.Replace(activeTabName, "").Trim();
            }

            _logger.Debug($"FileExplorerActiveTab: active tab name = \"{activeTabName}\"");

            bool matchFound = false;
            var child = walker.GetFirstChildElement(root);
            while (child != null)
            {
                // Get next sibling before we potentially release child
                IUIAutomationElement? next = null;
                try
                {
                    next = walker.GetNextSiblingElement(child);
                }
                catch (COMException) 
                { 

                }

                //int controlType = 0;
                //try
                //{
                //    controlType = child.CurrentControlType;
                //}
                //catch (COMException) { }

                //bool isTabItem = controlType == UIA_ControlTypeIds.UIA_TabItemControlTypeId;

                //if (!isTabItem)
                //{

                //    targets.Add(child);
                //}
                //else
                {
                    string childName;
                    try { childName = child.CurrentName ?? ""; } catch (COMException) { childName = ""; }

                    if (!matchFound
                        && !string.IsNullOrEmpty(activeTabName)
                        && childName.Trim().Equals(activeTabName, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Info($"FileExplorerActiveTab: matched active tab \"{childName}\"");
                        targets.Add(child);
                        matchFound = true;
                    }
                    else
                    {
                        _logger.Info($"FileExplorerActiveTab: skipping inactive tab \"{childName}\"");
                        if (Marshal.IsComObject(child))
                        {
                            try { Marshal.ReleaseComObject(child); } catch { }
                        }
                    }
                }

                child = next;
            }

            if (Marshal.IsComObject(root))
            {
                try { Marshal.ReleaseComObject(root); } catch { }
            }

            _logger.Debug($"FileExplorerActiveTab: resolved {targets.Count} root(s) (matchFound={matchFound})");

            if (targets.Count == 0)
            {
                _logger.Debug("FileExplorerActiveTab: no targets found, re-acquiring root");
                // Can't reuse released root — return empty; caller handles gracefully
                return targets;
            }

            return targets;
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