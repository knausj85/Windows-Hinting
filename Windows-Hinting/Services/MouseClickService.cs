using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using WindowsHinting.Logging;
using WindowsHinting.Models;
using WindowsHinting.Services.Native;

namespace WindowsHinting.Services
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
                    case ClickAction.Default:
                    case ClickAction.LeftClick:
                        SendClick(x, y, WindowsConstants.MOUSEEVENTF_LEFTDOWN, WindowsConstants.MOUSEEVENTF_LEFTUP);
                        break;

                    case ClickAction.RightClick:
                        SendClick(x, y, WindowsConstants.MOUSEEVENTF_RIGHTDOWN, WindowsConstants.MOUSEEVENTF_RIGHTUP);
                        break;

                    case ClickAction.DoubleClick:
                        SendClick(x, y, WindowsConstants.MOUSEEVENTF_LEFTDOWN, WindowsConstants.MOUSEEVENTF_LEFTUP);
                        Thread.Sleep(30);
                        SendClick(x, y, WindowsConstants.MOUSEEVENTF_LEFTDOWN, WindowsConstants.MOUSEEVENTF_LEFTUP);
                        break;

                    case ClickAction.MouseMove:
                        SendMove(x, y);
                        break;

                    case ClickAction.CtrlClick:
                        SendModifierKeyDown(WindowsConstants.VK_CONTROL);
                        Thread.Sleep(10);
                        SendClick(x, y, WindowsConstants.MOUSEEVENTF_LEFTDOWN, WindowsConstants.MOUSEEVENTF_LEFTUP);
                        Thread.Sleep(10);
                        SendModifierKeyUp(WindowsConstants.VK_CONTROL);
                        break;

                    case ClickAction.ShiftClick:
                        SendModifierKeyDown(WindowsConstants.VK_SHIFT);
                        Thread.Sleep(10);
                        SendClick(x, y, WindowsConstants.MOUSEEVENTF_LEFTDOWN, WindowsConstants.MOUSEEVENTF_LEFTUP);
                        Thread.Sleep(10);
                        SendModifierKeyUp(WindowsConstants.VK_SHIFT);
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

        /// <summary>
        /// Converts screen pixel coordinates to normalized virtual-desktop absolute coordinates (0..65535).
        /// Required for SendInput with MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK to work correctly
        /// across multiple monitors, including monitors with negative coordinates (left-of-primary).
        /// </summary>
        private static (int absX, int absY) ToVirtualDesktopAbsolute(int screenX, int screenY)
        {
            int vx = NativeMethods.GetSystemMetrics(WindowsConstants.SM_XVIRTUALSCREEN);
            int vy = NativeMethods.GetSystemMetrics(WindowsConstants.SM_YVIRTUALSCREEN);
            int vw = NativeMethods.GetSystemMetrics(WindowsConstants.SM_CXVIRTUALSCREEN);
            int vh = NativeMethods.GetSystemMetrics(WindowsConstants.SM_CYVIRTUALSCREEN);

            if (vw < 2) vw = 2;
            if (vh < 2) vh = 2;

            int absX = (int)(((screenX - vx) * 65535.0) / (vw - 1));
            int absY = (int)(((screenY - vy) * 65535.0) / (vh - 1));

            // Clamp to valid range
            absX = Math.Max(0, Math.Min(65535, absX));
            absY = Math.Max(0, Math.Min(65535, absY));

            return (absX, absY);
        }

        private void SendMove(int screenX, int screenY)
        {
            var (absX, absY) = ToVirtualDesktopAbsolute(screenX, screenY);

            var inputs = new NativeMethods.INPUT[1];
            inputs[0].Type = WindowsConstants.INPUT_MOUSE;
            inputs[0].U.Mi.Dx = absX;
            inputs[0].U.Mi.Dy = absY;
            inputs[0].U.Mi.DwFlags = WindowsConstants.MOUSEEVENTF_MOVE
                                   | WindowsConstants.MOUSEEVENTF_ABSOLUTE
                                   | WindowsConstants.MOUSEEVENTF_VIRTUALDESK;

            uint sent = NativeMethods.SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf<NativeMethods.INPUT>());

            if (sent != inputs.Length)
            {
                _logger.Warning($"SendInput (move) returned {sent}, expected {inputs.Length}");
            }
        }

        private void SendModifierKeyDown(int vkCode)
        {
            var inputs = new NativeMethods.INPUT[1];
            inputs[0].Type = WindowsConstants.INPUT_KEYBOARD;
            inputs[0].U.Ki.Vk = (ushort)vkCode;
            inputs[0].U.Ki.Flags = 0;

            uint sent = NativeMethods.SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf<NativeMethods.INPUT>());

            if (sent != inputs.Length)
                _logger.Warning($"SendInput (modifier key down VK=0x{vkCode:X2}) returned {sent}, expected {inputs.Length}");
        }

        private void SendModifierKeyUp(int vkCode)
        {
            var inputs = new NativeMethods.INPUT[1];
            inputs[0].Type = WindowsConstants.INPUT_KEYBOARD;
            inputs[0].U.Ki.Vk = (ushort)vkCode;
            inputs[0].U.Ki.Flags = WindowsConstants.KEYEVENTF_KEYUP;

            uint sent = NativeMethods.SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf<NativeMethods.INPUT>());

            if (sent != inputs.Length)
                _logger.Warning($"SendInput (modifier key up VK=0x{vkCode:X2}) returned {sent}, expected {inputs.Length}");
        }

        private void SendClick(int screenX, int screenY, uint downFlag, uint upFlag)
        {
            var (absX, absY) = ToVirtualDesktopAbsolute(screenX, screenY);

            uint absoluteFlags = WindowsConstants.MOUSEEVENTF_ABSOLUTE | WindowsConstants.MOUSEEVENTF_VIRTUALDESK;

            var inputs = new NativeMethods.INPUT[2];

            inputs[0].Type = WindowsConstants.INPUT_MOUSE;
            inputs[0].U.Mi.Dx = absX;
            inputs[0].U.Mi.Dy = absY;
            inputs[0].U.Mi.DwFlags = downFlag | absoluteFlags;

            inputs[1].Type = WindowsConstants.INPUT_MOUSE;
            inputs[1].U.Mi.Dx = absX;
            inputs[1].U.Mi.Dy = absY;
            inputs[1].U.Mi.DwFlags = upFlag | absoluteFlags;

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
