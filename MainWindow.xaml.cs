using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PentabServer.Models;
using PentabServer.Services;

namespace PentabServer
{
    public partial class MainWindow : Window
    {
        private readonly ScreenMapper _screenMapper;
        private readonly InputInjector _inputInjector;
        private readonly WebSocketServer _server;

        private readonly DispatcherTimer _rateTimer;
        private int _eventCount = 0;
        private int _currentEps = 0;

        public MainWindow()
        {
            InitializeComponent();

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

            PopulateMonitors();
            PopulateLocalIps();

            // Auto start server immediately on creation
            try
            {
                _server.Start(8765);
            }
            catch (Exception ex)
            {
                AppendLog($"Auto-start failed: {ex.Message}");
            }

            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _rateTimer.Stop();
            _server.Stop();
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

            MonitorComboBox.SelectedIndex = 0; // Default to Primary Monitor
            _screenMapper.SelectedMonitorIndex = -1;
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
                AppendLog("Target: Primary Monitor");
            }
            else if (MonitorComboBox.SelectedIndex == 1)
            {
                _screenMapper.SelectedMonitorIndex = -2; // All screens
                AppendLog("Target: Entire Virtual Desktop");
            }
            else if (MonitorComboBox.SelectedIndex >= 2)
            {
                _screenMapper.SelectedMonitorIndex = MonitorComboBox.SelectedIndex - 2;
                AppendLog($"Target: Monitor {_screenMapper.SelectedMonitorIndex}");
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
                }
                else
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52)); // Red
                    StatusText.Text = "Server Stopped";
                    ToggleServerButton.Content = "Start Server";
                    ToggleServerButton.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7A, 0xFE));
                    PortTextBox.IsEnabled = true;
                    ClientIpText.Text = "None";
                }
            });
        }

        private void OnClientConnected(string clientIp)
        {
            Dispatcher.Invoke(() =>
            {
                ClientIpText.Text = clientIp;
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