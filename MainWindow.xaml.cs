using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PentabServer.Models;
using PentabServer.Services;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using Forms = System.Windows.Forms;

namespace PentabServer
{
    public partial class MainWindow : Window
    {
        private readonly ScreenMapper _screenMapper;
        private readonly InputInjector _inputInjector;
        private readonly WebSocketServer _server;
        private readonly AppSettings _settings;

        private readonly DispatcherTimer _rateTimer;
        private int _eventCount = 0;
        private int _currentEps = 0;

        private Forms.NotifyIcon? _notifyIcon;
        private bool _isExplicitExit = false;

        public MainWindow()
        {
            InitializeComponent();

            _settings = AppSettings.Load();
            _screenMapper = new ScreenMapper();
            _inputInjector = new InputInjector(_screenMapper);
            _server = new WebSocketServer(_inputInjector);

            _server.ServerStateChanged += OnServerStateChanged;
            _server.ClientConnected += OnClientConnected;
            _server.ClientDisconnected += OnClientDisconnected;
            _server.PenDataReceived += OnPenDataReceived;
            _server.LogMessage += OnLogMessage;

            _rateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _rateTimer.Tick += RateTimer_Tick;
            _rateTimer.Start();

            // Initialize System Tray NotifyIcon
            InitializeNotifyIcon();

            // Apply loaded settings to UI
            PortTextBox.Text = _settings.Port.ToString();
            AutoStartCheckBox.IsChecked = _settings.AutoStart;
            StartMinimizedCheckBox.IsChecked = _settings.StartMinimized;

            PopulateMonitors();
            PopulateLocalIps();

            // Auto start server immediately
            try
            {
                _server.Start(_settings.Port);
            }
            catch (Exception ex)
            {
                AppendLog($"Auto-start server failed: {ex.Message}");
            }

            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Check if launched with --minimized / --tray argument or StartMinimized setting
            string[] args = Environment.GetCommandLineArgs();
            bool hasMinimizedArg = Array.Exists(args, a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

            if (hasMinimizedArg || _settings.StartMinimized)
            {
                HideToTray(showNotification: false);
            }
        }

        private void InitializeNotifyIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
                System.Drawing.Icon icon = File.Exists(iconPath)
                    ? new System.Drawing.Icon(iconPath)
                    : System.Drawing.SystemIcons.Application;

                var contextMenu = new Forms.ContextMenuStrip();

                var openItem = new Forms.ToolStripMenuItem("Open Pentab Server (表示)");
                openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
                openItem.Click += (s, e) => ShowFromTray();

                var statusItem = new Forms.ToolStripMenuItem("Status: Running on Port 8765");
                statusItem.Enabled = false;

                var autoStartItem = new Forms.ToolStripMenuItem("Start with Windows (自動起動)");
                autoStartItem.Checked = _settings.AutoStart;
                autoStartItem.Click += (s, e) =>
                {
                    _settings.AutoStart = !_settings.AutoStart;
                    autoStartItem.Checked = _settings.AutoStart;
                    AutoStartCheckBox.IsChecked = _settings.AutoStart;
                    _settings.Save();
                };

                var exitItem = new Forms.ToolStripMenuItem("Exit (終了)");
                exitItem.Click += (s, e) =>
                {
                    _isExplicitExit = true;
                    Close();
                };

                contextMenu.Items.Add(openItem);
                contextMenu.Items.Add(new Forms.ToolStripSeparator());
                contextMenu.Items.Add(statusItem);
                contextMenu.Items.Add(autoStartItem);
                contextMenu.Items.Add(new Forms.ToolStripSeparator());
                contextMenu.Items.Add(exitItem);

                _notifyIcon = new Forms.NotifyIcon
                {
                    Icon = icon,
                    Text = "Pentab PC Server",
                    ContextMenuStrip = contextMenu,
                    Visible = true
                };

                _notifyIcon.DoubleClick += (s, e) => ShowFromTray();
            }
            catch (Exception ex)
            {
                AppendLog($"Tray icon init warning: {ex.Message}");
            }
        }

        public void HideToTray(bool showNotification = true)
        {
            Hide();
            WindowState = WindowState.Minimized;

            if (showNotification && _notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(
                    2000,
                    "Pentab Server",
                    "インジケーター（システムトレイ）で常駐しています。ダブルクリックで再表示できます。",
                    Forms.ToolTipIcon.Info
                );
            }
        }

        public void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Focus();
        }

        private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e)
        {
            HideToTray(showNotification: true);
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                HideToTray(showNotification: false);
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                // Minimize to tray instead of quitting
                e.Cancel = true;
                HideToTray(showNotification: true);
                return;
            }

            _rateTimer.Stop();
            _server.Stop();

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _settings.Save();
        }

        private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _settings.AutoStart = AutoStartCheckBox.IsChecked == true;
            _settings.Save();
            AppendLog($"Auto-start with Windows: {(_settings.AutoStart ? "Enabled" : "Disabled")}");
        }

        private void StartMinimizedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _settings.StartMinimized = StartMinimizedCheckBox.IsChecked == true;
            _settings.Save();
        }

        private void PopulateMonitors()
        {
            MonitorComboBox.Items.Clear();
            MonitorComboBox.Items.Add("★ Primary Monitor (Default)");
            MonitorComboBox.Items.Add("Entire Virtual Desktop (All Screens)");

            var monitors = _screenMapper.GetMonitors();
            foreach (var m in monitors)
            {
                MonitorComboBox.Items.Add(m.ToString());
            }

            if (_settings.MonitorIndex == -1)
            {
                MonitorComboBox.SelectedIndex = 0;
            }
            else if (_settings.MonitorIndex == -2)
            {
                MonitorComboBox.SelectedIndex = 1;
            }
            else if (_settings.MonitorIndex >= 0 && _settings.MonitorIndex + 2 < MonitorComboBox.Items.Count)
            {
                MonitorComboBox.SelectedIndex = _settings.MonitorIndex + 2;
            }
            else
            {
                MonitorComboBox.SelectedIndex = 0;
            }
            _screenMapper.SelectedMonitorIndex = _settings.MonitorIndex;
        }

        private void PopulateLocalIps()
        {
            var ips = WebSocketServer.GetLocalIPAddresses();
            if (ips.Length > 0)
            {
                LocalIpText.Text = string.Join("\n", ips);
            }
            else
            {
                LocalIpText.Text = "127.0.0.1 (Localhost)";
            }
        }

        private void ToggleServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_server.IsRunning)
            {
                _server.Stop();
            }
            else
            {
                if (int.TryParse(PortTextBox.Text.Trim(), out int port))
                {
                    try
                    {
                        _settings.Port = port;
                        _settings.Save();
                        _server.Start(port);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to start server:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Please enter a valid port number.", "Invalid Port", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MonitorComboBox.SelectedIndex == 0)
            {
                _screenMapper.SelectedMonitorIndex = -1; // Primary Monitor
                _settings.MonitorIndex = -1;
                AppendLog("Target: Primary Monitor");
            }
            else if (MonitorComboBox.SelectedIndex == 1)
            {
                _screenMapper.SelectedMonitorIndex = -2; // All screens
                _settings.MonitorIndex = -2;
                AppendLog("Target: Entire Virtual Desktop");
            }
            else if (MonitorComboBox.SelectedIndex >= 2)
            {
                _screenMapper.SelectedMonitorIndex = MonitorComboBox.SelectedIndex - 2;
                _settings.MonitorIndex = _screenMapper.SelectedMonitorIndex;
                AppendLog($"Target: Monitor {_screenMapper.SelectedMonitorIndex}");
            }
            _settings.Save();
        }

        private void TestCursorButton_Click(object sender, RoutedEventArgs e)
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen;
            if (primary != null)
            {
                int cx = primary.Bounds.X + (primary.Bounds.Width / 2);
                int cy = primary.Bounds.Y + (primary.Bounds.Height / 2);
                _inputInjector.MoveToPixel(cx, cy);
                AppendLog($"Test: Moved cursor to center ({cx}, {cy})");
            }
        }

        private void CopyAdbButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText("adb reverse tcp:8765 tcp:8765");
            AppendLog("Copied ADB reverse command to clipboard.");
        }

        private void RateTimer_Tick(object? sender, EventArgs e)
        {
            _currentEps = _eventCount;
            _eventCount = 0;
            EventRateText.Text = $"{_currentEps} eps";
        }

        private void OnServerStateChanged(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                if (isRunning)
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)); // Green
                    StatusText.Text = $"Running on port {_server.Port}";
                    ToggleServerButton.Content = "Stop Server";
                    ToggleServerButton.Background = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
                    PortTextBox.IsEnabled = false;

                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Text = $"Pentab Server (Port: {_server.Port})";
                    }
                }
                else
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52)); // Red
                    StatusText.Text = "Server Stopped";
                    ToggleServerButton.Content = "Start Server";
                    ToggleServerButton.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7A, 0xFE));
                    PortTextBox.IsEnabled = true;
                    ClientIpText.Text = "None";

                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Text = "Pentab Server (Stopped)";
                    }
                }
            });
        }

        private void OnClientConnected(string clientIp)
        {
            Dispatcher.Invoke(() =>
            {
                ClientIpText.Text = clientIp;

                if (_notifyIcon != null)
                {
                    _notifyIcon.ShowBalloonTip(
                        1500,
                        "Pentab Connected",
                        $"Android tablet ({clientIp}) connected successfully.",
                        Forms.ToolTipIcon.Info
                    );
                }
            });
        }

        private void OnClientDisconnected()
        {
            Dispatcher.Invoke(() =>
            {
                ClientIpText.Text = "None";
            });
        }

        private void OnPenDataReceived(PenData data)
        {
            _eventCount++;

            // Throttle UI update to keep max performance
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                CoordsText.Text = $"{data.X:F3}, {data.Y:F3}";
                PressureBar.Value = data.Pressure * 100.0;
                PressureValueText.Text = $"{(data.Pressure * 100.0):F1}%";
                ActionTypeText.Text = data.Action;

                ToolTypeText.Text = data.ToolType switch
                {
                    ToolType.Stylus => "STYLUS",
                    ToolType.Eraser => "ERASER",
                    ToolType.Finger => "FINGER",
                    ToolType.Mouse => "MOUSE",
                    _ => "UNKNOWN"
                };
            });
        }

        private void OnLogMessage(string message)
        {
            Dispatcher.BeginInvoke(() =>
            {
                AppendLog(message);
            });
        }

        private void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogTextBox.AppendText($"[{timestamp}] {message}\n");
            LogTextBox.ScrollToEnd();
        }
    }
}