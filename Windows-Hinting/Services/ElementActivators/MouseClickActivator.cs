using System;
using System.Drawing;
using System.Runtime.InteropServices;
using UIAutomationClient;
using WindowsHinting.Logging;
using WindowsHinting.Models;
using WindowsHinting.Services;

namespace WindowsHinting.Services.ElementActivators
{
    internal sealed class MouseClickActivator : IElementActivator
    {
        private readonly MouseClickService _mouseClickService;
        private readonly ILogger _logger;

        public MouseClickActivator(MouseClickService mouseClickService, ILogger logger)
        {
            _mouseClickService = mouseClickService ?? throw new ArgumentNullException(nameof(mouseClickService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool TryActivate(IUIAutomationElement element)
        {
            try
            {
                var rectObj = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_BoundingRectanglePropertyId);
                if (rectObj is double[] rectArray && rectArray.Length == 4)
                {
                    var bounds = new Rectangle(
                        (int)rectArray[0],
                        (int)rectArray[1],
                        (int)rectArray[2],
                        (int)rectArray[3]);

                    if (bounds.Width > 0 && bounds.Height > 0)
                    {
                        var name = element.CachedName ?? "(unnamed)";
                        _logger.Info($"Falling back to mouse click for element '{name}'");
                        return _mouseClickService.PerformClick(bounds, ClickAction.LeftClick);
                    }
                }

                _logger.Warning("MouseClickActivator: element has no valid bounding rectangle");
                return false;
            }
            catch (COMException ex)
            {
                _logger.Debug($"MouseClickActivator failed (COM): {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Debug($"MouseClickActivator failed: {ex.Message}");
                return false;
            }
        }
    }
}
