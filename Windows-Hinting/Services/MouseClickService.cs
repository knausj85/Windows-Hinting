using System;
using System.Drawing;
using System.Threading;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using WindowsHinting.Logging;
using WindowsHinting.Models;

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
            PInvoke.GetCursorPos(out var originalPos);

            try
            {
                PInvoke.SetCursorPos(x, y);

                // Small delay to let the cursor settle
                Thread.Sleep(10);

                switch (action)
                {
                    case ClickAction.Default:
                    case ClickAction.LeftClick:
                        SendClick(x, y, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP);
                        break;

                    case ClickAction.RightClick:
                        SendClick(x, y, MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP);
                        break;

                    case ClickAction.DoubleClick:
                        SendClick(x, y, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP);
                        Thread.Sleep(30);
                        SendClick(x, y, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP);
                        break;

                    case ClickAction.MouseMove:
                        SendMove(x, y);
                        break;

                    case ClickAction.CtrlClick:
                        SendModifierKeyDown(VIRTUAL_KEY.VK_CONTROL);
                        Thread.Sleep(10);
                        SendClick(x, y, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP);
                        Thread.Sleep(10);
                        SendModifierKeyUp(VIRTUAL_KEY.VK_CONTROL);
                        break;

                    case ClickAction.ShiftClick:
                        SendModifierKeyDown(VIRTUAL_KEY.VK_SHIFT);
                        Thread.Sleep(10);
                        SendClick(x, y, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP);
                        Thread.Sleep(10);
                        SendModifierKeyUp(VIRTUAL_KEY.VK_SHIFT);
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
            int vx = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
            int vy = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
            int vw = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
            int vh = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);

            if (vw < 2) vw = 2;
            if (vh < 2) vh = 2;

            int absX = (int)(((screenX - vx) * 65535.0) / (vw - 1));
            int absY = (int)(((screenY - vy) * 65535.0) / (vh - 1));

            // Clamp to valid range
            absX = Math.Max(0, Math.Min(65535, absX));
            absY = Math.Max(0, Math.Min(65535, absY));

            return (absX, absY);
        }

        private static unsafe uint SendInputs(ReadOnlySpan<INPUT> inputs)
        {
            return PInvoke.SendInput(inputs, sizeof(INPUT));
        }

        private void SendMove(int screenX, int screenY)
        {
            var (absX, absY) = ToVirtualDesktopAbsolute(screenX, screenY);

            Span<INPUT> inputs = stackalloc INPUT[1];
            inputs[0].type = INPUT_TYPE.INPUT_MOUSE;
            inputs[0].Anonymous.mi.dx = absX;
            inputs[0].Anonymous.mi.dy = absY;
            inputs[0].Anonymous.mi.dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE
                                           | MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE
                                           | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK;

            uint sent = SendInputs(inputs);
            if (sent != inputs.Length)
            {
                _logger.Warning($"SendInput (move) returned {sent}, expected {inputs.Length}");
            }
        }

        private void SendModifierKeyDown(VIRTUAL_KEY vkCode)
        {
            Span<INPUT> inputs = stackalloc INPUT[1];
            inputs[0].type = INPUT_TYPE.INPUT_KEYBOARD;
            inputs[0].Anonymous.ki.wVk = vkCode;
            inputs[0].Anonymous.ki.dwFlags = 0;

            uint sent = SendInputs(inputs);
            if (sent != inputs.Length)
                _logger.Warning($"SendInput (modifier key down VK=0x{(int)vkCode:X2}) returned {sent}, expected {inputs.Length}");
        }

        private void SendModifierKeyUp(VIRTUAL_KEY vkCode)
        {
            Span<INPUT> inputs = stackalloc INPUT[1];
            inputs[0].type = INPUT_TYPE.INPUT_KEYBOARD;
            inputs[0].Anonymous.ki.wVk = vkCode;
            inputs[0].Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;

            uint sent = SendInputs(inputs);
            if (sent != inputs.Length)
                _logger.Warning($"SendInput (modifier key up VK=0x{(int)vkCode:X2}) returned {sent}, expected {inputs.Length}");
        }

        private void SendClick(int screenX, int screenY, MOUSE_EVENT_FLAGS downFlag, MOUSE_EVENT_FLAGS upFlag)
        {
            var (absX, absY) = ToVirtualDesktopAbsolute(screenX, screenY);

            MOUSE_EVENT_FLAGS absoluteFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK;

            Span<INPUT> inputs = stackalloc INPUT[2];

            inputs[0].type = INPUT_TYPE.INPUT_MOUSE;
            inputs[0].Anonymous.mi.dx = absX;
            inputs[0].Anonymous.mi.dy = absY;
            inputs[0].Anonymous.mi.dwFlags = downFlag | absoluteFlags;

            inputs[1].type = INPUT_TYPE.INPUT_MOUSE;
            inputs[1].Anonymous.mi.dx = absX;
            inputs[1].Anonymous.mi.dy = absY;
            inputs[1].Anonymous.mi.dwFlags = upFlag | absoluteFlags;

            uint sent = SendInputs(inputs);
            if (sent != inputs.Length)
            {
                _logger.Warning($"SendInput returned {sent}, expected {inputs.Length}");
            }
        }
    }
}
