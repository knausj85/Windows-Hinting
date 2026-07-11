using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UIAutomationClient;
using Windows.Win32;
using Windows.Win32.Foundation;
using WindowsHinting.Configuration;
using WindowsHinting.Logging;
using WindowsHinting.Models;

namespace WindowsHinting.Services
{
    internal sealed class UIAutomationService : IUIAutomationService
    {
        private readonly IUIAutomation _automation;
        private readonly ILogger _logger;
        private readonly WindowRuleRegistry _ruleRegistry;
        private readonly IUIAutomationCondition _searchCondition;
        private readonly IUIAutomationCacheRequest _cacheRequest;
        private readonly List<IUIAutomationCondition> _ownedConditions;
        private bool _disposed;

        public UIAutomationService(ILogger logger, WindowRuleRegistry ruleRegistry)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ruleRegistry = ruleRegistry ?? throw new ArgumentNullException(nameof(ruleRegistry));
            _automation = new CUIAutomation();

            (_searchCondition, _cacheRequest, _ownedConditions) = BuildSearchConditionsAndCache();
        }

        private (IUIAutomationCondition combined, IUIAutomationCacheRequest cache, List<IUIAutomationCondition> owned) BuildSearchConditionsAndCache()
        {
            var owned = new List<IUIAutomationCondition>();

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

            var enabledCondition = _automation.CreatePropertyCondition(UIA_PropertyIds.UIA_IsEnabledPropertyId, true);
            var onscreenCondition = _automation.CreatePropertyCondition(UIA_PropertyIds.UIA_IsOffscreenPropertyId, false);
            owned.Add(enabledCondition);
            owned.Add(onscreenCondition);

            var statusAndCondition = _automation.CreateAndCondition(enabledCondition, onscreenCondition);
            owned.Add(statusAndCondition);

            var controlTypeConditions = clickableControlTypes
                .Select(t => _automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ControlTypePropertyId, t))
                .ToArray();
            owned.AddRange(controlTypeConditions);

            var controlTypeOrCondition = _automation.CreateOrConditionFromArray(controlTypeConditions);
            owned.Add(controlTypeOrCondition);

            var combined = _automation.CreateAndCondition(statusAndCondition, controlTypeOrCondition);
            owned.Add(combined);

            var cache = _automation.CreateCacheRequest();
            cache.TreeScope = TreeScope.TreeScope_Element;
            cache.AddProperty(UIA_PropertyIds.UIA_BoundingRectanglePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_ClickablePointPropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_ControlTypePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_IsTogglePatternAvailablePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_IsInvokePatternAvailablePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_IsExpandCollapsePatternAvailablePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_IsKeyboardFocusablePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_IsSelectionItemPatternAvailablePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_NamePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_ClassNamePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_ProcessIdPropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_IsLegacyIAccessiblePatternAvailablePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_LegacyIAccessibleStatePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_NativeWindowHandlePropertyId);
            cache.AddPattern(UIA_PatternIds.UIA_InvokePatternId);
            cache.AddPattern(UIA_PatternIds.UIA_ExpandCollapsePatternId);
            cache.AddPattern(UIA_PatternIds.UIA_SelectionPatternId);
            cache.AddPattern(UIA_PatternIds.UIA_SelectionItemPatternId);
            cache.AddPattern(UIA_PatternIds.UIA_TogglePatternId);
            cache.AddPattern(UIA_PatternIds.UIA_LegacyIAccessiblePatternId);

            return (combined, cache, owned);
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

        public async Task<IReadOnlyList<ClickableElement>> FindClickableElementsAsync(IntPtr windowHandle, int timeoutMs)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (timeoutMs <= 0)
                return await FindClickableElementsAsync(windowHandle);

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                var task = Task.Run(() => FindClickableElements(windowHandle), cts.Token);
                return await task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning($"UIA scan timed out after {timeoutMs}ms — returning empty results");
                return Array.Empty<ClickableElement>();
            }
        }

        private IReadOnlyList<ClickableElement> FindClickableElementsCore(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                _logger.Debug("Window handle is zero");
                return Array.Empty<ClickableElement>();
            }

            IUIAutomationElement? root = null;
            var elementArraysToRelease = new List<IUIAutomationElementArray>();
            var roots = new List<IUIAutomationElement>();

            try
            {
                root = _automation.ElementFromHandle(windowHandle);
                if (root == null)
                {
                    _logger.Warning("Failed to get root element from window handle");
                    return Array.Empty<ClickableElement>();
                }

                // Resolve the root element(s) strategy based on window rules
                roots = ResolveRootElements(windowHandle, root);

                var results = new List<ClickableElement>();

                using (PerformanceMetrics.Start("FindAllBuildCache", _logger, LogLevel.Info))
                {
                    foreach (var scanRoot in roots)
                    {
                        IUIAutomationElementArray? found = null;
                        try
                        {
                            found = scanRoot.FindAllBuildCache(TreeScope.TreeScope_Descendants, _searchCondition, _cacheRequest);
                        }
                        catch (COMException ex)
                        {
                            _logger.Warning($"FindAllBuildCache failed for a root: {ex.Message}");
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

                                    if (rect.Width > 0 && rect.Height > 0)
                                    {
                                        // Check for valid clickable point (filter out VT_EMPTY)
                                        var clickablePointObj = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_ClickablePointPropertyId);
                                        bool hasValidClickablePoint = clickablePointObj != null;
                                        bool isLegacyPatternAvailable = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsLegacyIAccessiblePatternAvailablePropertyId) is true;
                                        bool hasActivatablePattern = HasActivatablePattern(element);

                                        if (!hasValidClickablePoint)
                                        {
                                            if (!isLegacyPatternAvailable || IsLegacyElementOffscreenOrInvisible(element))
                                            {
                                                _logger.Debug($"Element {element.GetCachedPropertyValue(UIA_PropertyIds.UIA_NamePropertyId)} is offscreen/invisible per MSAA legacy state, skipping");
                                                continue;
                                            }
                                        }

                                        if (hasActivatablePattern)
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
                            }
                            catch (COMException ex)
                            {
                                _logger.Warning($"COM exception processing element {i}: {ex.Message}");
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning($"Exception processing element {i}: {ex.Message}");
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
                // Release per-call COM objects (element arrays and root elements)
                foreach (var elemArray in elementArraysToRelease)
                {
                    if (elemArray != null && Marshal.IsComObject(elemArray))
                        Marshal.ReleaseComObject(elemArray);
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

                    //case RootStrategy.FileExplorerCustomStrategy:
                    //    return ResolveFileExplorerActiveTab(root, windowTitle);

                    case RootStrategy.SearchHostCustomStrategy:
                        {
                            var resolved = ResolveSearchHostRoot(root);
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
                _logger.Warning($"COM exception in ResolveRootElements: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Exception in ResolveRootElements: {ex.Message}");
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

        private static readonly string[] SearchHostTargetNames = { "Start", "Search" };

        /// <summary>
        /// For SearchHost (Start menu / Search), the visible UI lives on a sibling of the
        /// initial CoreWindow. Walks up to the parent and does a single cached child search
        /// for a Window named "Start" or "Search" to efficiently locate the correct root.
        /// </summary>
        private IUIAutomationElement? ResolveSearchHostRoot(IUIAutomationElement root)
        {
            IUIAutomationCondition? windowCondition = null;
            IUIAutomationCondition? nameOr = null;
            IUIAutomationCondition? combined = null;
            var nameConditions = new List<IUIAutomationCondition>();

            try
            {
                windowCondition = _automation.CreatePropertyCondition(
                    UIA_PropertyIds.UIA_ControlTypePropertyId,
                    UIA_ControlTypeIds.UIA_WindowControlTypeId);

                foreach (var name in SearchHostTargetNames)
                {
                    nameConditions.Add(_automation.CreatePropertyCondition(
                        UIA_PropertyIds.UIA_NamePropertyId, name));
                }

                nameOr = _automation.CreateOrConditionFromArray(nameConditions.ToArray());
                combined = _automation.CreateAndCondition(windowCondition, nameOr);

                IUIAutomationElement? match = null;
                try
                {
                    match = _automation.GetRootElement().FindFirstBuildCache(TreeScope.TreeScope_Children, combined, _cacheRequest);
                }
                catch (COMException ex)
                {
                    _logger.Warning($"SearchHostCustomStrategy: FindFirstBuildCache failed: {ex.Message}");
                }

                if (match != null)
                {
                    string matchedName;
                    try { matchedName = match.CachedName ?? ""; } catch (COMException) { matchedName = ""; }
                    _logger.Info($"SearchHostCustomStrategy: matched window \"{matchedName}\"");

                    return match;
                }

                return null;
            }
            finally
            {
                if (combined != null && Marshal.IsComObject(combined))
                {
                    try { Marshal.ReleaseComObject(combined); } catch { }
                }
                if (nameOr != null && Marshal.IsComObject(nameOr))
                {
                    try { Marshal.ReleaseComObject(nameOr); } catch { }
                }
                foreach (var nc in nameConditions)
                {
                    if (nc != null && Marshal.IsComObject(nc))
                    {
                        try { Marshal.ReleaseComObject(nc); } catch { }
                    }
                }
                if (windowCondition != null && Marshal.IsComObject(windowCondition))
                {
                    try { Marshal.ReleaseComObject(windowCondition); } catch { }
                }
            }
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
                PInvoke.GetWindowThreadProcessId((HWND)windowHandle, out uint processId);
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
            unsafe
            {
                Span<char> buffer = stackalloc char[maxLength];
                fixed (char* pBuffer = buffer)
                {
                    int len = PInvoke.GetWindowText((HWND)windowHandle, pBuffer, maxLength);
                    if (len <= 0)
                        return null;
                    return new string(pBuffer, 0, len);
                }
            }
        }

        private bool HasActivatablePattern(IUIAutomationElement element)
        {
            return element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsInvokePatternAvailablePropertyId) is true
                || element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsExpandCollapsePatternAvailablePropertyId) is true
                || element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsSelectionItemPatternAvailablePropertyId) is true
                || element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsTogglePatternAvailablePropertyId) is true
                || element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsKeyboardFocusablePropertyId) is true;
        }

        /// <summary>
        /// Returns true when the element's MSAA <c>LegacyIAccessiblePattern.CachedState</c> has
        /// <c>STATE_SYSTEM_OFFSCREEN</c> or <c>STATE_SYSTEM_INVISIBLE</c> set. Fails open
        /// (returns false) on any error so we never accidentally hide a real clickable element.
        /// </summary>
        private bool IsLegacyElementOffscreenOrInvisible(IUIAutomationElement element)
        {
            IUIAutomationLegacyIAccessiblePattern? legacy = null;
            try
            {
                bool legacyAvailable = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsLegacyIAccessiblePatternAvailablePropertyId);
                if (!legacyAvailable)
                    return false;

                var legacyState = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_LegacyIAccessibleStatePropertyId);

                const uint mask = 0x00010000;
                return (legacyState & mask) != 0;
            }
            catch (COMException ex)
            {
                _logger.Debug($"IsLegacyElementOffscreenOrInvisible: COM exception reading legacy state: {ex.Message}");
                return false;
            }
            finally
            {
                if (legacy != null && Marshal.IsComObject(legacy))
                {
                    try { Marshal.ReleaseComObject(legacy); } catch { }
                }
            }
        }

        #region Scroll Discovery

        public IReadOnlyList<WindowsHinting.Models.ScrollableElement> FindScrollableElements(IntPtr windowHandle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            using (PerformanceMetrics.Start("UIAutomationService.FindScrollableElements", _logger, LogLevel.Info))
            {
                try
                {
                    return FindScrollableElementsCore(windowHandle);
                }
                catch (COMException ex)
                {
                    _logger.Error($"UIA COM exception: {ex.Message}");
                    return Array.Empty<WindowsHinting.Models.ScrollableElement>();
                }
                catch (Exception ex)
                {
                    _logger.Error("Unexpected error in FindScrollableElements", ex);
                    return Array.Empty<WindowsHinting.Models.ScrollableElement>();
                }
            }
        }

        public async Task<IReadOnlyList<WindowsHinting.Models.ScrollableElement>> FindScrollableElementsAsync(IntPtr windowHandle)
        {
            return await FindScrollableElementsAsync(windowHandle, 0);
        }

        public async Task<IReadOnlyList<WindowsHinting.Models.ScrollableElement>> FindScrollableElementsAsync(IntPtr windowHandle, int timeoutMs)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (timeoutMs <= 0)
            {
                return await Task.Run(() => FindScrollableElements(windowHandle));
            }

            var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                return await Task.Run(() => FindScrollableElements(windowHandle), cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning($"FindScrollableElementsAsync timed out after {timeoutMs}ms");
                return Array.Empty<WindowsHinting.Models.ScrollableElement>();
            }
        }

        private IReadOnlyList<WindowsHinting.Models.ScrollableElement> FindScrollableElementsCore(IntPtr windowHandle)
        {
            var (scrollCondition, scrollCache, ownedConditions) = BuildScrollSearchConditionsAndCache();
            var roots = new List<IUIAutomationElement>();
            var elementArraysToRelease = new List<IUIAutomationElementArray>();
            var results = new List<WindowsHinting.Models.ScrollableElement>();

            try
            {
                IUIAutomationElement? root = _automation.ElementFromHandle((HWND)windowHandle);
                if (root == null)
                {
                    _logger.Warning($"Unable to get automation element for window handle {windowHandle}");
                    return Array.Empty<WindowsHinting.Models.ScrollableElement>();
                }

                roots = ResolveRootElements(windowHandle, root);
                _logger.Debug($"Resolved {roots.Count} root element(s) for scrollable scan");

                using (PerformanceMetrics.Start($"FindAllBuildCache(Scroll, {roots.Count} roots)", _logger, LogLevel.Debug))
                {
                    foreach (var r in roots)
                    {
                        if (r == null) continue;

                        IUIAutomationElementArray? found = r.FindAllBuildCache(
                            TreeScope.TreeScope_Descendants,
                            scrollCondition,
                            scrollCache);

                        if (found != null && found.Length > 0)
                        {
                            elementArraysToRelease.Add(found);
                        }
                        else if (found != null && Marshal.IsComObject(found))
                        {
                            Marshal.ReleaseComObject(found);
                        }
                    }

                    if (elementArraysToRelease.Count == 0)
                    {
                        _logger.Debug("FindAllBuildCache returned no scrollable results");
                        return Array.Empty<WindowsHinting.Models.ScrollableElement>();
                    }
                }

                int totalElements = elementArraysToRelease.Sum(a => a.Length);
                _logger.Debug($"Processing {totalElements} scrollable elements across {elementArraysToRelease.Count} root(s)");

                using (PerformanceMetrics.Start($"ProcessScrollElements({totalElements})", _logger, LogLevel.Debug))
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

                                    if (rect.Width > 0 && rect.Height > 0)
                                    {
                                        var controlType = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_ControlTypePropertyId) as int? ?? 0;
                                        var name = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_NamePropertyId) as string ?? "";
                                        var hasScrollPattern = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsScrollPatternAvailablePropertyId) is true;
                                        var hasRangeValuePattern = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_IsRangeValuePatternAvailablePropertyId) is true;
                                        var isHorizontallyScrollable = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_ScrollHorizontallyScrollablePropertyId) is true;
                                        var isVerticallyScrollable = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_ScrollVerticallyScrollablePropertyId) is true;

                                        // Keep ScrollPattern elements even when scrollability flags are false
                                        // (Chromium-based apps may report false but still support scrolling via UIA or input)

                                        results.Add(new WindowsHinting.Models.ScrollableElement
                                        {
                                            Element = element,
                                            Bounds = rect,
                                            ControlType = controlType,
                                            Name = name,
                                            HasScrollPattern = hasScrollPattern,
                                            HasRangeValuePattern = hasRangeValuePattern,
                                            IsHorizontallyScrollable = isHorizontallyScrollable,
                                            IsVerticallyScrollable = isVerticallyScrollable
                                        });
                                        element = null; // Don't release - ownership transferred
                                    }
                                }
                            }
                            catch (COMException ex)
                            {
                                _logger.Warning($"COM exception processing scroll element {i}: {ex.Message}");
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning($"Exception processing scroll element {i}: {ex.Message}");
                            }
                            finally
                            {
                                if (element != null && Marshal.IsComObject(element))
                                {
                                    Marshal.ReleaseComObject(element);
                                }
                            }
                        }
                    }
                }

                _logger.Info($"Found {results.Count} valid scrollable elements");
                return results;
            }
            finally
            {
                foreach (var elemArray in elementArraysToRelease)
                {
                    if (elemArray != null && Marshal.IsComObject(elemArray))
                        Marshal.ReleaseComObject(elemArray);
                }

                foreach (var r in roots)
                {
                    if (r != null && Marshal.IsComObject(r))
                    {
                        try { Marshal.ReleaseComObject(r); } catch { }
                    }
                }

                foreach (var condition in ownedConditions)
                {
                    if (condition != null && Marshal.IsComObject(condition))
                        Marshal.ReleaseComObject(condition);
                }

                if (scrollCache != null && Marshal.IsComObject(scrollCache))
                    Marshal.ReleaseComObject(scrollCache);
            }
        }

        private (IUIAutomationCondition combined, IUIAutomationCacheRequest cache, List<IUIAutomationCondition> owned) BuildScrollSearchConditionsAndCache()
        {
            var owned = new List<IUIAutomationCondition>();

            // Condition 1: Elements with ScrollPattern available
            var scrollPatternCondition = _automation.CreatePropertyCondition(
                UIA_PropertyIds.UIA_IsScrollPatternAvailablePropertyId, true);
            owned.Add(scrollPatternCondition);

            // Condition 2: ScrollBar control type
            var scrollBarCondition = _automation.CreatePropertyCondition(
                UIA_PropertyIds.UIA_ControlTypePropertyId, UIA_ControlTypeIds.UIA_ScrollBarControlTypeId);
            owned.Add(scrollBarCondition);

            // OR condition: has ScrollPattern OR is ScrollBar
            var orCondition = _automation.CreateOrCondition(scrollPatternCondition, scrollBarCondition);
            owned.Add(orCondition);

            // Status filter: enabled and on-screen
            var enabledCondition = _automation.CreatePropertyCondition(UIA_PropertyIds.UIA_IsEnabledPropertyId, true);
            var onscreenCondition = _automation.CreatePropertyCondition(UIA_PropertyIds.UIA_IsOffscreenPropertyId, false);
            owned.Add(enabledCondition);
            owned.Add(onscreenCondition);

            var statusAndCondition = _automation.CreateAndCondition(enabledCondition, onscreenCondition);
            owned.Add(statusAndCondition);

            // Combined: (hasScrollPattern OR isScrollBar) AND enabled AND on-screen
            var combined = _automation.CreateAndCondition(statusAndCondition, orCondition);
            owned.Add(combined);

            // Build cache request with PRD-required properties
            var cache = _automation.CreateCacheRequest();
            cache.TreeScope = TreeScope.TreeScope_Element;
            cache.AddProperty(UIA_PropertyIds.UIA_BoundingRectanglePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_NamePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_ControlTypePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_IsScrollPatternAvailablePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_IsRangeValuePatternAvailablePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_ScrollHorizontalScrollPercentPropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_ScrollVerticalScrollPercentPropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_ScrollHorizontallyScrollablePropertyId);
            cache.AddProperty(UIA_PropertyIds.UIA_ScrollVerticallyScrollablePropertyId);
            cache.AddPattern(UIA_PatternIds.UIA_ScrollPatternId);
            cache.AddPattern(UIA_PatternIds.UIA_RangeValuePatternId);

            return (combined, cache, owned);
        }

        #endregion

        public void Dispose()
        {
            if (_disposed)
                return;

            if (_cacheRequest != null && Marshal.IsComObject(_cacheRequest))
                Marshal.ReleaseComObject(_cacheRequest);

            foreach (var condition in _ownedConditions)
            {
                if (condition != null && Marshal.IsComObject(condition))
                    Marshal.ReleaseComObject(condition);
            }

            if (_automation != null && Marshal.IsComObject(_automation))
            {
                Marshal.ReleaseComObject(_automation);
            }

            _disposed = true;
        }
    }
}
