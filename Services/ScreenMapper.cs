using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PentabServer.Services
{
    public class DisplayMonitorInfo
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }

        public override string ToString() => $"Monitor {Index}: {Width}x{Height} ({(IsPrimary ? "Primary" : "Secondary")})";
    }

    public class ScreenMapper
    {
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        private const uint MONITORINFOF_PRIMARY = 0x00000001;

        public int SelectedMonitorIndex { get; set; } = -1; // -1 means Primary Monitor or Full Virtual Desk

        public List<DisplayMonitorInfo> GetMonitors()
        {
            var monitors = new List<DisplayMonitorInfo>();
            int index = 0;

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    monitors.Add(new DisplayMonitorInfo
                    {
                        Index = index++,
                        Name = mi.szDevice,
                        Left = mi.rcMonitor.Left,
                        Top = mi.rcMonitor.Top,
                        Width = mi.rcMonitor.Right - mi.rcMonitor.Left,
                        Height = mi.rcMonitor.Bottom - mi.rcMonitor.Top,
                        IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0
                    });
                }
                return true;
            }, IntPtr.Zero);

            return monitors;
        }

        public (int absX, int absY) MapToAbsoluteCoordinates(float normX, float normY)
        {
            int virtLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int virtTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int virtWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int virtHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            if (virtWidth == 0) virtWidth = GetSystemMetrics(SM_CXSCREEN);
            if (virtHeight == 0) virtHeight = GetSystemMetrics(SM_CYSCREEN);

            double targetLeft;
            double targetTop;
            double targetWidth;
            double targetHeight;

            var monitors = GetMonitors();
            if (SelectedMonitorIndex >= 0 && SelectedMonitorIndex < monitors.Count)
            {
                var m = monitors[SelectedMonitorIndex];
                targetLeft = m.Left;
                targetTop = m.Top;
                targetWidth = m.Width;
                targetHeight = m.Height;
            }
            else
            {
                // Default to Primary Monitor or Entire Virtual Screen
                var primary = monitors.Find(m => m.IsPrimary);
                if (primary != null && SelectedMonitorIndex == -2)
                {
                    targetLeft = primary.Left;
                    targetTop = primary.Top;
                    targetWidth = primary.Width;
                    targetHeight = primary.Height;
                }
                else
                {
                    targetLeft = virtLeft;
                    targetTop = virtTop;
                    targetWidth = virtWidth;
                    targetHeight = virtHeight;
                }
            }

            double pixelX = targetLeft + (normX * targetWidth);
            double pixelY = targetTop + (normY * targetHeight);

            // Convert pixel to absolute 0..65535 scale across virtual screen
            int absX = (int)Math.Round(((pixelX - virtLeft) * 65535.0) / (virtWidth - 1));
            int absY = (int)Math.Round(((pixelY - virtTop) * 65535.0) / (virtHeight - 1));

            absX = Math.Clamp(absX, 0, 65535);
            absY = Math.Clamp(absY, 0, 65535);

            return (absX, absY);
        }
    }
}
