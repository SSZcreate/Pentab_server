using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
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
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

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
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        private readonly ScreenMapper _screenMapper;
        private bool _isLeftDown = false;
        private bool _isRightDown = false;

        public InputInjector(ScreenMapper screenMapper)
        {
            _screenMapper = screenMapper;
        }

        public void MoveToPixel(int targetX, int targetY)
        {
            // 1. Direct SetCursorPos
            SetCursorPos(targetX, targetY);

            // 2. WinForms Cursor.Position
            try
            {
                Cursor.Position = new Point(targetX, targetY);
            }
            catch { }

            // 3. Hardware-like relative move delta
            if (GetCursorPos(out POINT cur))
            {
                int deltaX = targetX - cur.X;
                int deltaY = targetY - cur.Y;
                if (deltaX != 0 || deltaY != 0)
                {
                    mouse_event(MOUSEEVENTF_MOVE, deltaX, deltaY, 0, UIntPtr.Zero);
                }
            }
        }

        public void Inject(PenData data)
        {
            var (dx, dy, pixelX, pixelY) = _screenMapper.MapToVirtualDesktop(data.X, data.Y);

            // Move cursor to target pixel on Primary Screen
            MoveToPixel(pixelX, pixelY);

            // 4. Absolute Virtual Desktop SendInput
            uint flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
            bool isSecondaryButtonPressed = (data.ButtonState & 32) != 0 || (data.ButtonState & 2) != 0;

            switch (data.Action.ToUpperInvariant())
            {
                case ActionType.Down:
                    if (isSecondaryButtonPressed)
                    {
                        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                        flags |= MOUSEEVENTF_RIGHTDOWN;
                        _isRightDown = true;
                    }
                    else
                    {
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                        flags |= MOUSEEVENTF_LEFTDOWN;
                        _isLeftDown = true;
                    }
                    break;

                case ActionType.Move:
                    break;

                case ActionType.Up:
                case ActionType.Cancel:
                    if (_isLeftDown)
                    {
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                        flags |= MOUSEEVENTF_LEFTUP;
                        _isLeftDown = false;
                    }
                    if (_isRightDown)
                    {
                        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                        flags |= MOUSEEVENTF_RIGHTUP;
                        _isRightDown = false;
                    }
                    break;

                case ActionType.HoverExit:
                    if (_isLeftDown)
                    {
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                        flags |= MOUSEEVENTF_LEFTUP;
                        _isLeftDown = false;
                    }
                    if (_isRightDown)
                    {
                        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                        flags |= MOUSEEVENTF_RIGHTUP;
                        _isRightDown = false;
                    }
                    break;
            }

            var input = new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));

            File.AppendAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] Injected ({pixelX}, {pixelY}) -> (dx={dx}, dy={dy}) Action={data.Action}\n"
            );
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
