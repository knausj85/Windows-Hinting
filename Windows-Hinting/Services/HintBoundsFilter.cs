using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using UIAutomationClient;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using WindowsHinting.Logging;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Drops hints whose bounds fall outside a 10%-inflated rectangle around the
    /// scanned window. Elements that live in a different top-level HWND (combo-box
    /// dropdowns, context menus, tooltips, etc.) are exempted so popups still get
    /// hints. Foreground-window mode only — taskbar/SearchHost scans are not filtered.
    /// </summary>
    internal static class HintBoundsFilter
    {
        private const double MarginRatio = 0.10;

        public static IReadOnlyList<ClickableElement> ClampToWindow(
            IReadOnlyList<ClickableElement> elements,
            IntPtr scannedHwnd,
            ILogger logger)
        {
            if (elements.Count == 0)
                return elements;

            if (!TryGetWindowRect(scannedHwnd, logger, out var windowRect) || windowRect.Width <= 0 || windowRect.Height <= 0)
            {
                logger.Warning("HintBoundsFilter: could not determine window rect, skipping clamp");
                return elements;
            }

            int dx = (int)(windowRect.Width * MarginRatio);
            int dy = (int)(windowRect.Height * MarginRatio);
            var allowed = Rectangle.Inflate(windowRect, dx, dy);

            var kept = new List<ClickableElement>(elements.Count);
            int dropped = 0;

            foreach (var elem in elements)
            {
                var name = elem.Element.GetCachedPropertyValue(UIA_PropertyIds.UIA_NamePropertyId);
                var is_call = name == "Call Stack";
                if (is_call)
                {
                    logger.Info($"{elem.Bounds} vs {allowed}");
                }
                if (IsInDifferentTopLevelWindow(elem, scannedHwnd))
                {
                    if (is_call)
                    {
                        logger.Info("***Different top level window");
                    }
                    kept.Add(elem);
                }
                else if (allowed.Contains(elem.Bounds))
                {
                    if (is_call)
                        logger.Info("***check window rect");
                    kept.Add(elem);
                }
                else
                {
                    dropped++;
                    if (elem.Element != null && Marshal.IsComObject(elem.Element))
                    {
                        try { Marshal.ReleaseComObject(elem.Element); } catch { }
                    }
                }
            }

            logger.Debug($"HintBoundsFilter: window={windowRect}, allowed={allowed}, {elements.Count} → {kept.Count} (dropped {dropped})");
            return kept;
        }

        private static bool IsInDifferentTopLevelWindow(ClickableElement elem, IntPtr scannedHwnd)
        {
            try
            {
                var raw = elem.Element?.GetCachedPropertyValue(UIA_PropertyIds.UIA_NativeWindowHandlePropertyId);
                if (raw == null)
                    return false;

                long handle = Convert.ToInt64(raw);
                if (handle == 0)
                    return false;

                return (IntPtr)handle != scannedHwnd;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetWindowRect(IntPtr hwnd, ILogger logger, out Rectangle rect)
        {
            rect = Rectangle.Empty;
            if (hwnd == IntPtr.Zero)
                return false;

            var hwndTyped = (HWND)hwnd;

            // Prefer DWM extended frame bounds — excludes the invisible drop-shadow margin.
            try
            {
                unsafe
                {
                    RECT dwmRect;
                    var hr = PInvoke.DwmGetWindowAttribute(
                        hwndTyped,
                        DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                        &dwmRect,
                        (uint)sizeof(RECT));
                    if (hr.Succeeded)
                    {
                        rect = Rectangle.FromLTRB(dwmRect.left, dwmRect.top, dwmRect.right, dwmRect.bottom);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Debug($"HintBoundsFilter: DwmGetWindowAttribute failed: {ex.Message}");
            }

            // Fallback to GetWindowRect.
            try
            {
                if (PInvoke.GetWindowRect(hwndTyped, out var winRect))
                {
                    rect = Rectangle.FromLTRB(winRect.left, winRect.top, winRect.right, winRect.bottom);
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Debug($"HintBoundsFilter: GetWindowRect failed: {ex.Message}");
            }

            return false;
        }
    }
}
