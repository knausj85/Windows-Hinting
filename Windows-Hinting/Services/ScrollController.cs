using System;
using System.Runtime.InteropServices;
using UIAutomationClient;
using WindowsHinting.Logging;
using WindowsHinting.Models;

namespace WindowsHinting.Services
{
    internal enum ScrollCommand
    {
        LineUp,
        LineDown,
        LineLeft,
        LineRight,
        PageUp,
        PageDown,
        Top,
        Bottom,
        Middle,
        PercentVertical,
        PercentHorizontal
    }

    internal sealed class ScrollController
    {
        private readonly ILogger _logger;
        private readonly MouseClickService _mouseClickService;

        public ScrollController(ILogger logger, MouseClickService mouseClickService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mouseClickService = mouseClickService ?? throw new ArgumentNullException(nameof(mouseClickService));
        }

        public bool ExecuteScrollCommand(ScrollableElement target, ScrollCommand command, int? percentValue = null)
        {
            if (target?.Element == null)
            {
                _logger.Warning("ExecuteScrollCommand: target or element is null");
                return false;
            }

            try
            {
                // Validate element is still available
                try
                {
                    _ = target.Element.CurrentBoundingRectangle;
                }
                catch (COMException)
                {
                    _logger.Warning("ExecuteScrollCommand: element is stale (no longer available)");
                    return false;
                }

                switch (command)
                {
                    case ScrollCommand.LineUp:
                        return ScrollLine(target, ScrollAmount.ScrollAmount_SmallDecrement, isVertical: true);
                    case ScrollCommand.LineDown:
                        return ScrollLine(target, ScrollAmount.ScrollAmount_SmallIncrement, isVertical: true);
                    case ScrollCommand.LineLeft:
                        return ScrollLine(target, ScrollAmount.ScrollAmount_SmallDecrement, isVertical: false);
                    case ScrollCommand.LineRight:
                        return ScrollLine(target, ScrollAmount.ScrollAmount_SmallIncrement, isVertical: false);
                    case ScrollCommand.PageUp:
                        return ScrollLine(target, ScrollAmount.ScrollAmount_LargeDecrement, isVertical: true);
                    case ScrollCommand.PageDown:
                        return ScrollLine(target, ScrollAmount.ScrollAmount_LargeIncrement, isVertical: true);
                    case ScrollCommand.Top:
                        return ScrollToPosition(target, 0, isVertical: true);
                    case ScrollCommand.Bottom:
                        return ScrollToPosition(target, 100, isVertical: true);
                    case ScrollCommand.Middle:
                        return ScrollToPosition(target, 50, isVertical: true);
                    case ScrollCommand.PercentVertical:
                        if (percentValue.HasValue)
                            return ScrollToPosition(target, percentValue.Value, isVertical: true);
                        break;
                    case ScrollCommand.PercentHorizontal:
                        if (percentValue.HasValue)
                            return ScrollToPosition(target, percentValue.Value, isVertical: false);
                        break;
                }

                _logger.Warning($"ExecuteScrollCommand: unsupported command {command}");
                return false;
            }
            catch (COMException ex)
            {
                _logger.Error($"COM exception executing scroll command {command}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error executing scroll command {command}", ex);
                return false;
            }
        }

        private bool ScrollLine(ScrollableElement target, ScrollAmount amount, bool isVertical)
        {
            // Try UIA ScrollPattern first
            if (target.HasScrollPattern)
            {
                try
                {
                    var scrollPattern = target.Element.GetCachedPattern(UIA_PatternIds.UIA_ScrollPatternId) as IUIAutomationScrollPattern;
                    if (scrollPattern != null)
                    {
                        try
                        {
                            if (isVertical)
                            {
                                scrollPattern.Scroll(ScrollAmount.ScrollAmount_NoAmount, amount);
                            }
                            else
                            {
                            scrollPattern.Scroll(amount, ScrollAmount.ScrollAmount_NoAmount);
                            }

                            _logger.Debug($"ScrollLine via UIA: executed {amount} {(isVertical ? "vertical" : "horizontal")}");
                            return true;
                        }
                        finally
                        {
                            if (Marshal.IsComObject(scrollPattern))
                                Marshal.ReleaseComObject(scrollPattern);
                        }
                    }
                }
                catch (COMException ex)
                {
                    _logger.Debug($"ScrollLine UIA failed: {ex.Message}, trying wheel fallback");
                }
            }

            // Fallback to wheel input for directional scrolling
            return ScrollLineViaWheel(target, amount, isVertical);
        }

        private bool ScrollLineViaWheel(ScrollableElement target, ScrollAmount amount, bool isVertical)
        {
            const int WHEEL_DELTA = 120;  // Standard wheel delta per notch
            const int LINE_MULTIPLIER = 1;
            const int PAGE_MULTIPLIER = 3;

            int multiplier = amount switch
            {
                ScrollAmount.ScrollAmount_SmallIncrement or ScrollAmount.ScrollAmount_SmallDecrement => LINE_MULTIPLIER,
                ScrollAmount.ScrollAmount_LargeIncrement or ScrollAmount.ScrollAmount_LargeDecrement => PAGE_MULTIPLIER,
                _ => LINE_MULTIPLIER
            };

            bool isIncrement = amount == ScrollAmount.ScrollAmount_SmallIncrement || amount == ScrollAmount.ScrollAmount_LargeIncrement;
            int wheelDelta = (isIncrement ? -1 : 1) * WHEEL_DELTA * multiplier;

            _logger.Debug($"ScrollLine via wheel: delta={wheelDelta}, {(isVertical ? "vertical" : "horizontal")}");
            return _mouseClickService.PerformWheelScroll(target.Bounds, wheelDelta, !isVertical);
        }

        private bool ScrollToPosition(ScrollableElement target, int percent, bool isVertical)
        {
            percent = Math.Clamp(percent, 0, 100);

            // Try RangeValuePattern first (works for scrollbars and some controls)
            if (target.HasRangeValuePattern && target.ControlType == UIA_ControlTypeIds.UIA_ScrollBarControlTypeId)
            {
                if (TryScrollToPositionViaRangeValue(target, percent))
                    return true;
            }

            // Try ScrollPattern with SetScrollPercent
            if (target.HasScrollPattern)
            {
                if (TryScrollToPositionViaScrollPattern(target, percent, isVertical))
                    return true;
            }

            // Fallback: Try RangeValuePattern even if not a scrollbar
            if (target.HasRangeValuePattern)
            {
                if (TryScrollToPositionViaRangeValue(target, percent))
                    return true;
            }

            _logger.Warning($"ScrollToPosition: target does not support percent positioning (no RangeValuePattern or ScrollPattern)");
            return false;
        }

        private bool TryScrollToPositionViaScrollPattern(ScrollableElement target, int percent, bool isVertical)
        {
            try
            {
                var scrollPattern = target.Element.GetCachedPattern(UIA_PatternIds.UIA_ScrollPatternId) as IUIAutomationScrollPattern;
                if (scrollPattern == null)
                    return false;

                try
                {
                    double horizontalPercent = isVertical ? (double)UIA_ScrollPatternConstants.UIA_ScrollPatternNoScroll : percent;
                    double verticalPercent = isVertical ? percent : (double)UIA_ScrollPatternConstants.UIA_ScrollPatternNoScroll;

                    scrollPattern.SetScrollPercent(horizontalPercent, verticalPercent);
                    _logger.Debug($"ScrollToPosition via ScrollPattern: {percent}% {(isVertical ? "vertical" : "horizontal")}");
                    return true;
                }
                finally
                {
                    if (Marshal.IsComObject(scrollPattern))
                        Marshal.ReleaseComObject(scrollPattern);
                }
            }
            catch (COMException ex)
            {
                _logger.Debug($"TryScrollToPositionViaScrollPattern failed: {ex.Message}");
                return false;
            }
        }

        private bool TryScrollToPositionViaRangeValue(ScrollableElement target, int percent)
        {
            try
            {
                var rangePattern = target.Element.GetCachedPattern(UIA_PatternIds.UIA_RangeValuePatternId) as IUIAutomationRangeValuePattern;
                if (rangePattern == null)
                    return false;

                try
                {
                    double min = rangePattern.CurrentMinimum;
                    double max = rangePattern.CurrentMaximum;
                    double range = max - min;
                    double targetValue = min + (range * percent / 100.0);

                    rangePattern.SetValue(targetValue);
                    _logger.Debug($"ScrollToPosition via RangeValuePattern: {percent}% (value {targetValue:F2})");
                    return true;
                }
                finally
                {
                    if (Marshal.IsComObject(rangePattern))
                        Marshal.ReleaseComObject(rangePattern);
                }
            }
            catch (COMException ex)
            {
                _logger.Debug($"TryScrollToPositionViaRangeValue failed: {ex.Message}");
                return false;
            }
        }
    }

    internal static class UIA_ScrollPatternConstants
    {
        public const int UIA_ScrollPatternNoScroll = -1;
    }
}
