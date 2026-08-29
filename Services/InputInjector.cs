using System;
using System.Runtime.InteropServices;
using PentabServer.Models;

namespace PentabServer.Services
{
    public class InputInjector
    {
        private const int INPUT_MOUSE = 0;

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)]
            public int type;
            [FieldOffset(8)]
            public MOUSEINPUT mi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private readonly ScreenMapper _screenMapper;
        private bool _isLeftDown = false;
        private bool _isRightDown = false;

        public InputInjector(ScreenMapper screenMapper)
        {
            _screenMapper = screenMapper;
        }

        public void Inject(PenData data)
        {
            var (absX, absY) = _screenMapper.MapToAbsoluteCoordinates(data.X, data.Y);

            // Check button states (e.g. stylus secondary button pressed = Right Click)
            bool isSecondaryButtonPressed = (data.ButtonState & 32) != 0 || (data.ButtonState & 2) != 0;

            switch (data.Action.ToUpperInvariant())
            {
                case ActionType.Down:
                    // 1. Move to position
                    SendMouseInput(absX, absY, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);

                    // 2. Press down
                    if (isSecondaryButtonPressed)
                    {
                        SendMouseInput(absX, absY, MOUSEEVENTF_RIGHTDOWN);
                        _isRightDown = true;
                    }
                    else
                    {
                        SendMouseInput(absX, absY, MOUSEEVENTF_LEFTDOWN);
                        _isLeftDown = true;
                    }
                    break;

                case ActionType.Move:
                    SendMouseInput(absX, absY, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);
                    break;

                case ActionType.Up:
                case ActionType.Cancel:
                    // 1. Move to final position
                    SendMouseInput(absX, absY, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);

                    // 2. Release button
                    if (_isLeftDown)
                    {
                        SendMouseInput(absX, absY, MOUSEEVENTF_LEFTUP);
                        _isLeftDown = false;
                    }
                    if (_isRightDown)
                    {
                        SendMouseInput(absX, absY, MOUSEEVENTF_RIGHTUP);
                        _isRightDown = false;
                    }
                    break;

                case ActionType.HoverMove:
                case ActionType.HoverEnter:
                    SendMouseInput(absX, absY, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);
                    break;

                case ActionType.HoverExit:
                    if (_isLeftDown)
                    {
                        SendMouseInput(absX, absY, MOUSEEVENTF_LEFTUP);
                        _isLeftDown = false;
                    }
                    if (_isRightDown)
                    {
                        SendMouseInput(absX, absY, MOUSEEVENTF_RIGHTUP);
                        _isRightDown = false;
                    }
                    break;
            }
        }

        private void SendMouseInput(int dx, int dy, uint flags)
        {
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
                    dwExtraInfo = IntPtr.Zero
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        public void ResetButtons()
        {
            if (_isLeftDown || _isRightDown)
            {
                uint flags = 0;
                if (_isLeftDown) flags |= MOUSEEVENTF_LEFTUP;
                if (_isRightDown) flags |= MOUSEEVENTF_RIGHTUP;

                SendMouseInput(0, 0, flags);
                _isLeftDown = false;
                _isRightDown = false;
            }
        }
    }
}
