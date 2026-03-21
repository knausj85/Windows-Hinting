using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using HintOverlay.Logging;
using HintOverlay.Models;
using HintOverlay.Services.Native;

namespace HintOverlay.Services
{
    internal sealed class MouseClickService
    {
        private readonly ILogger _logger;

        public MouseClickService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool PerformClick(Rectangle elementBounds, ClickAction action)
        {
            int x = elementBounds.Left + elementBounds.Width / 2;
            int y = elementBounds.Top + elementBounds.Height / 2;

            _logger.Info($"Performing {action} at ({x}, {y})");

            // Save and restore cursor position
            NativeMethods.GetCursorPos(out var originalPos);

            try
            {
                NativeMethods.SetCursorPos(x, y);

                // Small delay to let the cursor settle
                Thread.Sleep(10);

                switch (action)
                {
                    case ClickAction.LeftClick:
                        SendClick(WindowsConstants.MOUSEEVENTF_LEFTDOWN, WindowsConstants.MOUSEEVENTF_LEFTUP);
                        break;

                    case ClickAction.RightClick:
                        SendClick(WindowsConstants.MOUSEEVENTF_RIGHTDOWN, WindowsConstants.MOUSEEVENTF_RIGHTUP);
                        break;

                    case ClickAction.DoubleClick:
                        SendClick(WindowsConstants.MOUSEEVENTF_LEFTDOWN, WindowsConstants.MOUSEEVENTF_LEFTUP);
                        Thread.Sleep(30);
                        SendClick(WindowsConstants.MOUSEEVENTF_LEFTDOWN, WindowsConstants.MOUSEEVENTF_LEFTUP);
                        break;

                    default:
                        _logger.Warning($"Unsupported click action: {action}");
                        return false;
                }

                _logger.Info($"{action} performed successfully at ({x}, {y})");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to perform {action}", ex);
                return false;
            }
        }

        private void SendClick(uint downFlag, uint upFlag)
        {
            var inputs = new NativeMethods.INPUT[2];

            inputs[0].Type = WindowsConstants.INPUT_MOUSE;
            inputs[0].Mi.DwFlags = downFlag;

            inputs[1].Type = WindowsConstants.INPUT_MOUSE;
            inputs[1].Mi.DwFlags = upFlag;

            uint sent = NativeMethods.SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf<NativeMethods.INPUT>());

            if (sent != inputs.Length)
            {
                _logger.Warning($"SendInput returned {sent}, expected {inputs.Length}");
            }
        }
    }
}
