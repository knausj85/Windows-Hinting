using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using WindowsHinting.Configuration;
using WindowsHinting.Logging;
using WindowsHinting.NativeInterop;

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
            _automation = (IUIAutomation)new CUIAutomation();

            (_searchCondition, _cacheRequest, _ownedConditions) = BuildSearchConditionsAndCache();
        }

        private (IUIAutomationCondition combined, IUIAutomationCacheRequest cache, List<IUIAutomationCondition> owned) BuildSearchConditionsAndCache()
        {
            var owned = new List<IUIAutomationCondition>();

            var clickableControlTypes = new UIA_CONTROLTYPE_ID[]
            {
                UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_CheckBoxControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_ComboBoxControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_DataGridControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_DataItemControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_EditControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_GroupControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_HyperlinkControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_ListControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_ListItemControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_MenuControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_MenuItemControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_RadioButtonControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_SplitButtonControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_TabItemControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_TreeControlTypeId,
                UIA_CONTROLTYPE_ID.UIA_TreeItemControlTypeId
            };

            var enabledCondition = _automation.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsEnabledPropertyId, true);
            var onscreenCondition = _automation.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId, false);
            owned.Add(enabledCondition);
            owned.Add(onscreenCondition);

            var statusAndCondition = _automation.CreateAndCondition(enabledCondition, onscreenCondition);
            owned.Add(statusAndCondition);

            var controlTypeConditions = clickableControlTypes
                .Select(t => _automation.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)t))
                .ToArray();
            owned.AddRange(controlTypeConditions);

            var controlTypeOrCondition = _automation.CreateOrConditionFromNativeArray(controlTypeConditions);
            owned.Add(controlTypeOrCondition);

            var combined = _automation.CreateAndCondition(statusAndCondition, controlTypeOrCondition);
            owned.Add(combined);

            var cache = _automation.CreateCacheRequest();
            cache.TreeScope = TreeScope.TreeScope_Element;
            cache.AddProperty(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_ClickablePointPropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_ControlTypePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_IsTogglePatternAvailablePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_IsInvokePatternAvailablePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_IsExpandCollapsePatternAvailablePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_IsKeyboardFocusablePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_IsSelectionItemPatternAvailablePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_NamePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_ClassNamePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_ProcessIdPropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_IsLegacyIAccessiblePatternAvailablePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_LegacyIAccessibleStatePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_NativeWindowHandlePropertyId);
            cache.AddPattern(UIA_PATTERN_ID.UIA_InvokePatternId);
            cache.AddPattern(UIA_PATTERN_ID.UIA_ExpandCollapsePatternId);
            cache.AddPattern(UIA_PATTERN_ID.UIA_SelectionPatternId);
            cache.AddPattern(UIA_PATTERN_ID.UIA_SelectionItemPatternId);
            cache.AddPattern(UIA_PATTERN_ID.UIA_TogglePatternId);
            cache.AddPattern(UIA_PATTERN_ID.UIA_LegacyIAccessiblePatternId);

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
                root = _automation.ElementFromHandle((HWND)windowHandle);
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

                                var rectObj = element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
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
                                        var clickablePointObj = element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_ClickablePointPropertyId);
                                        bool hasValidClickablePoint = clickablePointObj != null;
                                        bool isLegacyPatternAvailable = element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsLegacyIAccessiblePatternAvailablePropertyId) is true;
                                        bool hasActivatablePattern = HasActivatablePattern(element);

                                        if (!hasValidClickablePoint)
                                        {
                                            if (!isLegacyPatternAvailable || IsLegacyElementOffscreenOrInvisible(element))
                                            {
                                                _logger.Debug($"Element {element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId)} is offscreen/invisible per MSAA legacy state, skipping");
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
            try
            {
                var className = root.GetCurrentClassName();
                var executableName = GetExecutableName(windowHandle);
                var windowTitle = GetWindowTitle(windowHandle);
                var strategy = _ruleRegistry.ResolveStrategy(executableName, className, windowTitle);

                _logger.Info($"Window rule resolved: exe={executableName}, class={className}, title={windowTitle}, strategy={strategy}");

                switch (strategy)
                {
                    case RootStrategy.ActiveWindow:
                        return [root];

                    case RootStrategy.FileExplorerCustomStrategy:
                        return ResolveFileExplorerActiveTab(windowHandle, root);

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

        // Win11 File Explorer hosts each tab in a direct child window of this
        // class. In the UIA tree each tab surfaces as a root child whose
        // NativeWindowHandle equals that window's handle.
        private const string ShellTabWindowClass = "ShellTabWindowClass";

        /// <summary>
        /// For File Explorer with tabs, returns the active tab's subtree plus the window
        /// chrome, dropping the subtrees of inactive tabs (which Windows otherwise exposes
        /// under the same root — issue #10). The active tab is identified by handle, not by
        /// window title: each <see cref="ShellTabWindowClass"/> root child is matched against
        /// the handle of the topmost such child window returned by <see cref="FindActiveExplorerTab"/>.
        /// This is robust to tabs showing identically-named folders, unlike title matching.
        /// </summary>
        private List<IUIAutomationElement> ResolveFileExplorerActiveTab(IntPtr windowHandle, IUIAutomationElement root)
        {
            var targets = new List<IUIAutomationElement>();
            var walker = _automation.ControlViewWalker;

            IntPtr activeTab = FindActiveExplorerTab(windowHandle);
            _logger.Debug($"FileExplorerActiveTab: active tab hwnd = 0x{activeTab.ToInt64():X}");

            var child = walker.GetFirstChildElement(root);
            while (child != null)
            {
                // Get next sibling before we potentially release child
                IUIAutomationElement? next = null;
                try { next = walker.GetNextSiblingElement(child); }
                catch (COMException) { }

                // Keep everything except inactive-tab panes: a child is an inactive tab
                // when it is a ShellTabWindowClass window whose handle is not the active
                // one. The active tab pane and all non-tab chrome (title bar, toolbars,
                // address bar) are kept. When no active tab handle is available (legacy
                // tab-less Explorer), activeTab is 0 and every child is kept.
                bool keep = true;
                if (activeTab != IntPtr.Zero)
                {
                    string childClass;
                    try { childClass = child.GetCurrentClassName() ?? ""; } catch (COMException) { childClass = ""; }

                    if (string.Equals(childClass, ShellTabWindowClass, StringComparison.Ordinal))
                    {
                        IntPtr childHandle;
                        try { childHandle = child.CurrentNativeWindowHandle; } catch (COMException) { childHandle = IntPtr.Zero; }

                        keep = childHandle == activeTab;
                        _logger.Info(keep
                            ? $"FileExplorerActiveTab: keeping active tab pane (hwnd=0x{childHandle.ToInt64():X})"
                            : $"FileExplorerActiveTab: skipping inactive tab pane (hwnd=0x{childHandle.ToInt64():X})");
                    }
                }

                if (keep)
                {
                    targets.Add(child);
                }
                else if (Marshal.IsComObject(child))
                {
                    try { Marshal.ReleaseComObject(child); } catch { }
                }

                child = next;
            }

            _logger.Debug($"FileExplorerActiveTab: resolved {targets.Count} root(s), activeTab=0x{activeTab.ToInt64():X}");

            if (targets.Count == 0)
            {
                // No children walked (unexpected) — can't reuse a released root, so keep
                // the original root and let the caller scan the whole window.
                return [root];
            }

            if (Marshal.IsComObject(root))
            {
                try { Marshal.ReleaseComObject(root); } catch { }
            }

            return targets;
        }

        /// <summary>
        /// Returns the window handle of the active File Explorer tab, or <see cref="IntPtr.Zero"/>
        /// for a tab-less (legacy) Explorer window. The active tab is hosted in the topmost
        /// <see cref="ShellTabWindowClass"/> child window; <c>FindWindowEx</c> enumerates children
        /// in z-order, so the first match is the active tab.
        /// </summary>
        /// <remarks>
        /// The window-title argument MUST be <c>null</c> (match any), never an empty string: an
        /// empty string filters for a window whose title is empty, which would exclude the tab
        /// (its title is the current folder name).
        /// </remarks>
        private static IntPtr FindActiveExplorerTab(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return IntPtr.Zero;

            return PInvoke.FindWindowEx((HWND)windowHandle, default, ShellTabWindowClass, null);
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
                    UIA_PROPERTY_ID.UIA_ControlTypePropertyId,
                    (int)UIA_CONTROLTYPE_ID.UIA_WindowControlTypeId);

                foreach (var name in SearchHostTargetNames)
                {
                    nameConditions.Add(_automation.CreatePropertyCondition(
                        UIA_PROPERTY_ID.UIA_NamePropertyId, name));
                }

                nameOr = _automation.CreateOrConditionFromNativeArray(nameConditions.ToArray());
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
                    try { matchedName = match.GetCachedName() ?? ""; } catch (COMException) { matchedName = ""; }
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

                            var controlType = parent.CurrentControlType;
                            if (controlType == UIA_CONTROLTYPE_ID.UIA_WindowControlTypeId)
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
            return element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsInvokePatternAvailablePropertyId) is true
                || element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsExpandCollapsePatternAvailablePropertyId) is true
                || element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsSelectionItemPatternAvailablePropertyId) is true
                || element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsTogglePatternAvailablePropertyId) is true
                || element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsKeyboardFocusablePropertyId) is true;
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
                bool legacyAvailable = element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_IsLegacyIAccessiblePatternAvailablePropertyId) is true;
                if (!legacyAvailable)
                    return false;

                var legacyState = element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_LegacyIAccessibleStatePropertyId);

                const uint mask = 0x00010000;
                return legacyState != null && (Convert.ToInt64(legacyState) & mask) != 0;
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
