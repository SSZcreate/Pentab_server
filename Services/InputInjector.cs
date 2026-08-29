using System;
using System.IO;
using System.Runtime.InteropServices;
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        private readonly ScreenMapper _screenMapper;
        private bool _isLeftDown = false;
        private bool _isRightDown = false;

        public InputInjector(ScreenMapper screenMapper)
        {
            _screenMapper = screenMapper;
        }

        public void Inject(PenData data)
        {
            var (dx, dy, pixelX, pixelY) = _screenMapper.MapToVirtualDesktop(data.X, data.Y);

            // 1. Move cursor via SetCursorPos
            SetCursorPos(pixelX, pixelY);

            // 2. Also move cursor via mouse_event with absolute virtual desktop coordinates
            mouse_event(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, (uint)dx, (uint)dy, 0, UIntPtr.Zero);

            bool isSecondaryButtonPressed = (data.ButtonState & 32) != 0 || (data.ButtonState & 2) != 0;

            switch (data.Action.ToUpperInvariant())
            {
                case ActionType.Down:
                    if (isSecondaryButtonPressed)
                    {
                        mouse_event(MOUSEEVENTF_RIGHTDOWN, (uint)dx, (uint)dy, 0, UIntPtr.Zero);
                        _isRightDown = true;
                    }
                    else
                    {
                        mouse_event(MOUSEEVENTF_LEFTDOWN, (uint)dx, (uint)dy, 0, UIntPtr.Zero);
                        _isLeftDown = true;
                    }
                    break;

                case ActionType.Move:
                    break;

                case ActionType.Up:
                case ActionType.Cancel:
                    if (_isLeftDown)
                    {
                        mouse_event(MOUSEEVENTF_LEFTUP, (uint)dx, (uint)dy, 0, UIntPtr.Zero);
                        _isLeftDown = false;
                    }
                    if (_isRightDown)
                    {
                        mouse_event(MOUSEEVENTF_RIGHTUP, (uint)dx, (uint)dy, 0, UIntPtr.Zero);
                        _isRightDown = false;
                    }
                    break;

                case ActionType.HoverExit:
                    if (_isLeftDown)
                    {
                        mouse_event(MOUSEEVENTF_LEFTUP, (uint)dx, (uint)dy, 0, UIntPtr.Zero);
                        _isLeftDown = false;
                    }
                    if (_isRightDown)
                    {
                        mouse_event(MOUSEEVENTF_RIGHTUP, (uint)dx, (uint)dy, 0, UIntPtr.Zero);
                        _isRightDown = false;
                    }
                    break;
            }

            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Injected ({pixelX}, {pixelY}) -> (dx={dx}, dy={dy}) Action={data.Action}\n");
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
