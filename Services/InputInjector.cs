using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using PentabServer.Models;

namespace PentabServer.Services
{
    public class InputInjector
    {
        private const uint INPUT_MOUSE = 0;

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUT
        {
            [FieldOffset(0)]
            public uint type;
            [FieldOffset(8)]
            public MOUSEINPUT mi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        private readonly ScreenMapper _screenMapper;
        private bool _isLeftDown = false;
        private bool _isRightDown = false;

        public InputInjector(ScreenMapper screenMapper)
        {
            _screenMapper = screenMapper;
        }

        public void MoveToPixel(int targetX, int targetY)
        {
            SetCursorPos(targetX, targetY);
        }

        public void LeftDown()
        {
            if (!_isLeftDown)
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                _isLeftDown = true;
            }
        }

        public void LeftUp()
        {
            if (_isLeftDown)
            {
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                _isLeftDown = false;
            }
        }

        public void RightDown()
        {
            if (!_isRightDown)
            {
                mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                _isRightDown = true;
            }
        }

        public void RightUp()
        {
            if (_isRightDown)
            {
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                _isRightDown = false;
            }
        }

        public void MiddleClick()
        {
            mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
        }

        public void LeftClick()
        {
            LeftDown();
            Thread.Sleep(20);
            LeftUp();
        }

        public void RightClick()
        {
            RightDown();
            Thread.Sleep(20);
            RightUp();
        }

        public void DoubleLeftClick()
        {
            LeftClick();
            Thread.Sleep(60);
            LeftClick();
        }

        public void ScrollWheel(int delta)
        {
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)delta, UIntPtr.Zero);
        }

        public void Inject(PenData data)
        {
            string action = data.Action?.ToUpperInvariant() ?? string.Empty;
            string clickType = data.ClickType?.ToUpperInvariant() ?? string.Empty;
            string mode = data.Mode?.ToUpperInvariant() ?? "ABSOLUTE";

            // 1. Handle Explicit Mouse Down / Up actions
            if (action == ActionType.DownLeft)
            {
                LeftDown();
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Injected LeftDown\n"); } catch { }
                return;
            }
            else if (action == ActionType.UpLeft)
            {
                LeftUp();
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Injected LeftUp\n"); } catch { }
                return;
            }
            else if (action == ActionType.DownRight)
            {
                RightDown();
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Injected RightDown\n"); } catch { }
                return;
            }
            else if (action == ActionType.UpRight)
            {
                RightUp();
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Injected RightUp\n"); } catch { }
                return;
            }

            // 2. Handle Clicks (Only when action is CLICK or explicit click action)
            if (action == ActionType.Click)
            {
                if (clickType == "RIGHT" || action == "CLICK_RIGHT")
                {
                    RightClick();
                    try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Injected RightClick\n"); } catch { }
                }
                else if (clickType == "MIDDLE" || clickType == "MIDDLE_CLICK" || action == "CLICK_MIDDLE")
                {
                    MiddleClick();
                    try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Injected MiddleClick\n"); } catch { }
                }
                else if (clickType == "DOUBLE_LEFT")
                {
                    DoubleLeftClick();
                    try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Injected DoubleLeftClick\n"); } catch { }
                }
                else if (clickType == "LEFT" || string.IsNullOrEmpty(clickType))
                {
                    LeftClick();
                    try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Injected LeftClick\n"); } catch { }
                }
                return;
            }

            // 3. Handle Wheel Scroll
            if (action == ActionType.Scroll || data.ScrollDelta != 0)
            {
                ScrollWheel(data.ScrollDelta);
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Injected Scroll delta={data.ScrollDelta}\n"); } catch { }
                return;
            }

            // 4. Handle Trackpad Mode (Relative Cursor Movement)
            if (mode == "TRACKPAD")
            {
                int rdx = (int)Math.Round(data.Dx);
                int rdy = (int)Math.Round(data.Dy);

                if (rdx != 0 || rdy != 0)
                {
                    mouse_event(MOUSEEVENTF_MOVE, rdx, rdy, 0, UIntPtr.Zero);
                }

                if (action == ActionType.Down && (data.ButtonState & 1) != 0)
                {
                    LeftDown();
                }
                else if (action == ActionType.Up || action == ActionType.Cancel)
                {
                    // Only release if not held by button state
                    if ((data.ButtonState & 1) == 0 && (data.ButtonState & 2) == 0)
                    {
                        ResetButtons();
                    }
                }

                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Trackpad Move dx={rdx}, dy={rdy}, action={action}\n"); } catch { }
                return;
            }

            // 4. Handle Absolute Pen Tablet Mode
            var (dx, dy, pixelX, pixelY) = _screenMapper.MapToVirtualDesktop(data.X, data.Y);

            // Move cursor to absolute pixel coordinates
            SetCursorPos(pixelX, pixelY);

            bool isSecondaryButton = (data.ButtonState & 32) != 0 || (data.ButtonState & 2) != 0;

            switch (action)
            {
                case ActionType.Down:
                    if (isSecondaryButton)
                    {
                        if (!_isRightDown)
                        {
                            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                            _isRightDown = true;
                        }
                    }
                    else
                    {
                        if (!_isLeftDown)
                        {
                            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                            _isLeftDown = true;
                        }
                    }
                    break;

                case ActionType.Move:
                case ActionType.HoverMove:
                case ActionType.HoverEnter:
                    // Position already updated via SetCursorPos
                    break;

                case ActionType.Up:
                case ActionType.Cancel:
                case ActionType.HoverExit:
                    ResetButtons();
                    break;
            }

            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] Injected ({pixelX}, {pixelY}) Action={action} Mode={mode} Click={clickType}\n"
                );
            }
            catch { }
        }

        public void ResetButtons()
        {
            if (_isLeftDown)
            {
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                _isLeftDown = false;
            }
            if (_isRightDown)
            {
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                _isRightDown = false;
            }
        }
    }
}


