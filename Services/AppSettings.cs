using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace PentabServer.Services
{
    public class AppSettings
    {
        private const string RegRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "PentabServer";

        public int Port { get; set; } = 8765;
        public int MonitorIndex { get; set; } = -1; // -1: Primary, -2: Virtual Desktop, >=0: specific monitor
        public bool AutoStart { get; set; } = false;
        public bool StartMinimized { get; set; } = true;
        public string Language { get; set; } = "en"; // "en", "ja", "zh"

        private static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PentabServer",
            "settings.json"
        );

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        settings.AutoStart = IsAutoStartRegistered();
                        return settings;
                    }
                }
            }
            catch { }

            return new AppSettings
            {
                AutoStart = IsAutoStartRegistered()
            };
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);

                SetAutoStartRegistration(AutoStart);
            }
            catch { }
        }

        public static bool IsAutoStartRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegRunKey, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SetAutoStartRegistration(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegRunKey, true);
                if (key == null) return;

                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PentabServer.exe");
                    key.SetValue(AppName, $"\"{exePath}\" --minimized");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch { }
        }
    }
}
