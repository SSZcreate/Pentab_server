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

        public override string ToString() => $"{(IsPrimary ? "★ Primary Monitor" : $"Monitor {Index}")}: {Width}x{Height} (Left={Left}, Top={Top})";
    }

    public class ScreenMapper
    {
        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

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

        // -1 means Primary Monitor (Default), -2 means Entire Virtual Desktop
        public int SelectedMonitorIndex { get; set; } = -1;

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
                    int left = mi.rcMonitor.Left;
                    int top = mi.rcMonitor.Top;
                    int width = mi.rcMonitor.Right - mi.rcMonitor.Left;
                    int height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                    bool isPrimary = (left == 0 && top == 0) || (mi.dwFlags & 1) != 0;

                    monitors.Add(new DisplayMonitorInfo
                    {
                        Index = index++,
                        Name = mi.szDevice,
                        Left = left,
                        Top = top,
                        Width = width,
                        Height = height,
                        IsPrimary = isPrimary
                    });
                }
                return true;
            }, IntPtr.Zero);

            return monitors;
        }

        public (int dx, int dy, int pixelX, int pixelY) MapToVirtualDesktop(float normX, float normY)
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

            if (SelectedMonitorIndex == -2) // Entire Virtual Desktop
            {
                targetLeft = virtLeft;
                targetTop = virtTop;
                targetWidth = virtWidth;
                targetHeight = virtHeight;
            }
            else if (SelectedMonitorIndex >= 0 && SelectedMonitorIndex < monitors.Count)
            {
                var m = monitors[SelectedMonitorIndex];
                targetLeft = m.Left;
                targetTop = m.Top;
                targetWidth = m.Width;
                targetHeight = m.Height;
            }
            else
            {
                // Default: Primary Monitor (Always Left=0, Top=0)
                var primary = monitors.Find(m => m.IsPrimary) ?? monitors.Find(m => m.Left == 0 && m.Top == 0);
                if (primary != null)
                {
                    targetLeft = primary.Left;
                    targetTop = primary.Top;
                    targetWidth = primary.Width;
                    targetHeight = primary.Height;
                }
                else
                {
                    targetLeft = 0;
                    targetTop = 0;
                    targetWidth = GetSystemMetrics(SM_CXSCREEN);
                    targetHeight = GetSystemMetrics(SM_CYSCREEN);
                }
            }

            int pixelX = (int)Math.Round(targetLeft + (normX * (targetWidth - 1)));
            int pixelY = (int)Math.Round(targetTop + (normY * (targetHeight - 1)));

            // Map pixel coordinates to virtual desktop 0..65535
            int dx = (int)Math.Round(((pixelX - virtLeft) * 65535.0) / (virtWidth - 1));
            int dy = (int)Math.Round(((pixelY - virtTop) * 65535.0) / (virtHeight - 1));

            dx = Math.Clamp(dx, 0, 65535);
            dy = Math.Clamp(dy, 0, 65535);

            return (dx, dy, pixelX, pixelY);
        }
    }
}
