using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

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
        // -1: Primary Monitor (Default), -2: Entire Virtual Desktop, >= 0: Monitor Index
        public int SelectedMonitorIndex { get; set; } = -1;

        public List<DisplayMonitorInfo> GetMonitors()
        {
            var list = new List<DisplayMonitorInfo>();
            var screens = Screen.AllScreens;

            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                list.Add(new DisplayMonitorInfo
                {
                    Index = i,
                    Name = s.DeviceName,
                    Left = s.Bounds.X,
                    Top = s.Bounds.Y,
                    Width = s.Bounds.Width,
                    Height = s.Bounds.Height,
                    IsPrimary = s.Primary
                });
            }

            return list;
        }

        public (int dx, int dy, int pixelX, int pixelY) MapToVirtualDesktop(float normX, float normY)
        {
            var virtScreen = SystemInformation.VirtualScreen;
            int virtLeft = virtScreen.X;
            int virtTop = virtScreen.Y;
            int virtWidth = virtScreen.Width;
            int virtHeight = virtScreen.Height;

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
                // Default: Primary Monitor (Primary == true or Left==0, Top==0)
                var primary = monitors.Find(m => m.IsPrimary) ?? monitors.Find(m => m.Left == 0 && m.Top == 0) ?? monitors[0];
                targetLeft = primary.Left;
                targetTop = primary.Top;
                targetWidth = primary.Width;
                targetHeight = primary.Height;
            }

            int pixelX = (int)Math.Round(targetLeft + (normX * (targetWidth - 1)));
            int pixelY = (int)Math.Round(targetTop + (normY * (targetHeight - 1)));

            // Map pixel coordinates to virtual desktop 0..65535 for SendInput
            int dx = (int)Math.Round(((pixelX - virtLeft) * 65535.0) / (virtWidth - 1));
            int dy = (int)Math.Round(((pixelY - virtTop) * 65535.0) / (virtHeight - 1));

            dx = Math.Clamp(dx, 0, 65535);
            dy = Math.Clamp(dy, 0, 65535);

            return (dx, dy, pixelX, pixelY);
        }
    }
}
