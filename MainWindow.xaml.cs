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
        private readonly LocalizationManager _loc = LocalizationManager.Instance;

        private readonly DispatcherTimer _rateTimer;
        private int _eventCount = 0;
        private int _currentEps = 0;

        private Forms.NotifyIcon? _notifyIcon;
        private Forms.ToolStripMenuItem? _trayOpenItem;
        private Forms.ToolStripMenuItem? _trayStatusItem;
        private Forms.ToolStripMenuItem? _trayAutoStartItem;
        private Forms.ToolStripMenuItem? _trayServerItem;
        private Forms.ToolStripMenuItem? _trayExitItem;

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

            // Initialize Localization
            _loc.LanguageChanged += ApplyLocalization;
            _loc.SetLanguage(_settings.Language);

            // Set Language ComboBox Selection without triggering double event
            SelectLanguageComboBoxItem(_settings.Language);

            // Initialize System Tray NotifyIcon
            InitializeNotifyIcon();

            // Apply loaded settings to UI
            PortTextBox.Text = _settings.Port.ToString();
            AutoStartCheckBox.IsChecked = _settings.AutoStart;
            StartMinimizedCheckBox.IsChecked = _settings.StartMinimized;

            PopulateMonitors();
            PopulateLocalIps();
            ApplyLocalization();

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

        private void SelectLanguageComboBoxItem(string lang)
        {
            for (int i = 0; i < LanguageComboBox.Items.Count; i++)
            {
                if (LanguageComboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == lang)
                {
                    LanguageComboBox.SelectedIndex = i;
                    break;
                }
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string lang)
            {
                if (_settings.Language != lang)
                {
                    _settings.Language = lang;
                    _settings.Save();
                    _loc.SetLanguage(lang);
                }
            }
        }

        private void ApplyLocalization()
        {
            Title = _loc.Get("AppTitle");
            HeaderTitleText.Text = _loc.Get("AppTitle");
            HeaderSubtitleText.Text = _loc.Get("AppSubtitle");
            MinimizeToTrayButton.Content = _loc.Get("HideToTray");

            ConfigTitleText.Text = _loc.Get("ConfigTitle");
            PortLabelText.Text = _loc.Get("Port");
            TargetMonitorLabelText.Text = _loc.Get("TargetMonitor");
            StartupOptionsLabelText.Text = _loc.Get("StartupOptions");
            AutoStartCheckBox.Content = _loc.Get("AutoStart");
            StartMinimizedCheckBox.Content = _loc.Get("StartInTray");

            ToggleServerButton.Content = _server.IsRunning ? _loc.Get("StopServer") : _loc.Get("StartServer");
            TestCursorButton.Content = _loc.Get("TestCursor");
            QuickConnectAdbLabelText.Text = _loc.Get("QuickConnectAdb");
            CopyAdbButton.Content = _loc.Get("CopyAdb");
            WifiLocalIpsLabelText.Text = _loc.Get("WifiLocalIps");

            LiveTelemetryLabelText.Text = _loc.Get("LiveTelemetry");
            ConnectedClientLabelText.Text = _loc.Get("ConnectedClient");
            EventRateLabelText.Text = _loc.Get("EventRate");
            NormalizedCoordsLabelText.Text = _loc.Get("NormalizedCoords");
            StylusPressureLabelText.Text = _loc.Get("StylusPressure");
            ActionLabelText.Text = _loc.Get("Action");
            ToolLabelText.Text = _loc.Get("Tool");
            ActivityLogLabelText.Text = _loc.Get("ActivityLog");

            if (!_server.IsRunning)
            {
                StatusText.Text = _loc.Get("StatusStopped");
            }
            else
            {
                StatusText.Text = _loc.Get("StatusRunning", _server.Port);
            }

            // Update Tray Menu Items
            if (_trayOpenItem != null) _trayOpenItem.Text = _loc.Get("TrayOpen");
            if (_trayAutoStartItem != null) _trayAutoStartItem.Text = _loc.Get("TrayStartWithWindows");
            if (_trayServerItem != null) _trayServerItem.Text = _server.IsRunning ? _loc.Get("TrayStopServer") : _loc.Get("TrayStartServer");
            if (_trayExitItem != null) _trayExitItem.Text = _loc.Get("TrayExit");
            if (_trayStatusItem != null)
            {
                _trayStatusItem.Text = _server.IsRunning ? _loc.Get("StatusRunning", _server.Port) : _loc.Get("StatusStopped");
            }
            if (_notifyIcon != null)
            {
                _notifyIcon.Text = _loc.Get("TrayTitle");
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
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

                _trayOpenItem = new Forms.ToolStripMenuItem(_loc.Get("TrayOpen"));
                _trayOpenItem.Font = new System.Drawing.Font(_trayOpenItem.Font, System.Drawing.FontStyle.Bold);
                _trayOpenItem.Click += (s, e) => ShowFromTray();

                _trayStatusItem = new Forms.ToolStripMenuItem(_loc.Get("StatusStopped"));
                _trayStatusItem.Enabled = false;

                _trayAutoStartItem = new Forms.ToolStripMenuItem(_loc.Get("TrayStartWithWindows"));
                _trayAutoStartItem.Checked = _settings.AutoStart;
                _trayAutoStartItem.Click += (s, e) =>
                {
                    _settings.AutoStart = !_settings.AutoStart;
                    _trayAutoStartItem.Checked = _settings.AutoStart;
                    AutoStartCheckBox.IsChecked = _settings.AutoStart;
                    _settings.Save();
                };

                _trayServerItem = new Forms.ToolStripMenuItem(_loc.Get("TrayStartServer"));
                _trayServerItem.Click += (s, e) =>
                {
                    if (_server.IsRunning) _server.Stop();
                    else _server.Start(_settings.Port);
                };

                _trayExitItem = new Forms.ToolStripMenuItem(_loc.Get("TrayExit"));
                _trayExitItem.Click += (s, e) =>
                {
                    _isExplicitExit = true;
                    Close();
                };

                contextMenu.Items.Add(_trayOpenItem);
                contextMenu.Items.Add(new Forms.ToolStripSeparator());
                contextMenu.Items.Add(_trayStatusItem);
                contextMenu.Items.Add(_trayServerItem);
                contextMenu.Items.Add(_trayAutoStartItem);
                contextMenu.Items.Add(new Forms.ToolStripSeparator());
                contextMenu.Items.Add(_trayExitItem);

                _notifyIcon = new Forms.NotifyIcon
                {
                    Icon = icon,
                    Text = _loc.Get("TrayTitle"),
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
                    _loc.Get("TrayTitle"),
                    _loc.Get("TrayRunningText"),
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

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                HideToTray(showNotification: false);
            }
        }

        private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e)
        {
            HideToTray(showNotification: true);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
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
            if (_trayAutoStartItem != null) _trayAutoStartItem.Checked = _settings.AutoStart;
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
                _screenMapper.SelectedMonitorIndex = -2; // Virtual Desktop
                _settings.MonitorIndex = -2;
                AppendLog("Target: Virtual Desktop (All Monitors)");
            }
            else if (MonitorComboBox.SelectedIndex > 1)
            {
                int index = MonitorComboBox.SelectedIndex - 2;
                _screenMapper.SelectedMonitorIndex = index;
                _settings.MonitorIndex = index;
                AppendLog($"Target: Monitor {index + 1}");
            }
            _settings.Save();
        }

        private void CopyAdbButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText($"adb reverse tcp:{PortTextBox.Text.Trim()} tcp:{PortTextBox.Text.Trim()}");
                AppendLog(_loc.Get("CopiedAdb"));
            }
            catch (Exception ex)
            {
                AppendLog($"Clipboard error: {ex.Message}");
            }
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

        private void OnServerStateChanged(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                if (isRunning)
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // Green
                    StatusText.Text = _loc.Get("StatusRunning", _server.Port);
                    ToggleServerButton.Content = _loc.Get("StopServer");
                    ToggleServerButton.Background = new SolidColorBrush(Color.FromRgb(229, 57, 53)); // Red
                    PortTextBox.IsEnabled = false;

                    if (_trayStatusItem != null) _trayStatusItem.Text = _loc.Get("StatusRunning", _server.Port);
                    if (_trayServerItem != null) _trayServerItem.Text = _loc.Get("TrayStopServer");
                }
                else
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(255, 82, 82)); // Red
                    StatusText.Text = _loc.Get("StatusStopped");
                    ToggleServerButton.Content = _loc.Get("StartServer");
                    ToggleServerButton.Background = new SolidColorBrush(Color.FromRgb(58, 122, 254)); // Blue
                    PortTextBox.IsEnabled = true;
                    ClientIpText.Text = _loc.Get("None");

                    if (_trayStatusItem != null) _trayStatusItem.Text = _loc.Get("StatusStopped");
                    if (_trayServerItem != null) _trayServerItem.Text = _loc.Get("TrayStartServer");
                }
            });
        }

        private void OnClientConnected(string clientIp)
        {
            Dispatcher.Invoke(() =>
            {
                ClientIpText.Text = clientIp;
                AppendLog($"Client connected: {clientIp}");
            });
        }

        private void OnClientDisconnected()
        {
            Dispatcher.Invoke(() =>
            {
                ClientIpText.Text = _loc.Get("None");
                EventRateText.Text = "0 eps";
                CoordsText.Text = "0.000, 0.000";
                PressureBar.Value = 0;
                PressureValueText.Text = "0.0%";
                ActionTypeText.Text = "IDLE";
                AppendLog("Client disconnected");
            });
        }

        private void OnPenDataReceived(PenData data)
        {
            _eventCount++;

            Dispatcher.Invoke(() =>
            {
                CoordsText.Text = $"{data.X:F3}, {data.Y:F3}";
                PressureBar.Value = data.Pressure * 100f;
                PressureValueText.Text = $"{data.Pressure * 100f:F1}%";
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

        private void RateTimer_Tick(object? sender, EventArgs e)
        {
            _currentEps = _eventCount;
            _eventCount = 0;
            EventRateText.Text = $"{_currentEps} eps";
        }

        private void OnLogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                AppendLog(message);
            });
        }

        private void AppendLog(string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            LogTextBox.AppendText($"[{time}] {message}\n");
            LogTextBox.ScrollToEnd();
        }
    }
}