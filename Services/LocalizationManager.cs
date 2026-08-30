using System;
using System.Collections.Generic;
using System.Globalization;

namespace PentabServer.Services
{
    public class LocalizationManager
    {
        private static LocalizationManager? _instance;
        public static LocalizationManager Instance => _instance ??= new LocalizationManager();

        public event Action? LanguageChanged;

        public string CurrentLanguage { get; private set; } = "en";

        private readonly Dictionary<string, Dictionary<string, string>> _translations = new()
        {
            ["en"] = new()
            {
                ["AppTitle"] = "Pentab PC Server",
                ["AppSubtitle"] = "Ultra-low latency Android tablet bridge with System Tray & Auto-Start",
                ["StatusStopped"] = "Server Stopped",
                ["StatusRunning"] = "Server Running on port {0}",
                ["HideToTray"] = "📥 Hide to Tray",
                ["ConfigTitle"] = "Server Configuration",
                ["Port"] = "Port:",
                ["TargetMonitor"] = "Target Monitor:",
                ["StartupOptions"] = "Startup & Background Options:",
                ["AutoStart"] = "Auto-start with Windows",
                ["StartInTray"] = "Start minimized in tray",
                ["StartServer"] = "Start Server",
                ["StopServer"] = "Stop Server",
                ["TestCursor"] = "Test Move to Center",
                ["QuickConnectAdb"] = "Quick Connect (USB ADB):",
                ["CopyAdb"] = "Copy ADB Command",
                ["CopiedAdb"] = "Copied to Clipboard!",
                ["WifiLocalIps"] = "Wi-Fi Local IPs:",
                ["LiveTelemetry"] = "Live Telemetry",
                ["ConnectedClient"] = "Connected Client:",
                ["None"] = "None",
                ["EventRate"] = "Event Rate",
                ["NormalizedCoords"] = "Normalized Coords (X, Y)",
                ["StylusPressure"] = "Stylus Pressure",
                ["Action"] = "Action:",
                ["Tool"] = "Tool:",
                ["ActivityLog"] = "Activity Log",
                ["Language"] = "Language:",
                ["TrayOpen"] = "Open Pentab Server",
                ["TrayStartWithWindows"] = "Start with Windows",
                ["TrayStartServer"] = "Start Server",
                ["TrayStopServer"] = "Stop Server",
                ["TrayExit"] = "Exit",
                ["TrayTitle"] = "Pentab PC Server",
                ["TrayRunningText"] = "Running in system tray. Double-click to open."
            },
            ["ja"] = new()
            {
                ["AppTitle"] = "Pentab PC Server",
                ["AppSubtitle"] = "超低遅延 Android タブレット連携常駐サーバー",
                ["StatusStopped"] = "サーバー停止中",
                ["StatusRunning"] = "サーバー稼働中 (ポート: {0})",
                ["HideToTray"] = "📥 トレイへ格納",
                ["ConfigTitle"] = "サーバー設定",
                ["Port"] = "ポート番号:",
                ["TargetMonitor"] = "対象モニター:",
                ["StartupOptions"] = "起動・常駐設定:",
                ["AutoStart"] = "Windows起動時に自動起動 (Auto-Start)",
                ["StartInTray"] = "起動時にトレイへ最小化 (Start in Tray)",
                ["StartServer"] = "サーバー起動",
                ["StopServer"] = "サーバー停止",
                ["TestCursor"] = "中央へテスト移動",
                ["QuickConnectAdb"] = "USB有線接続ガイド (ADB):",
                ["CopyAdb"] = "ADBコマンドをコピー",
                ["CopiedAdb"] = "クリップボードにコピーしました！",
                ["WifiLocalIps"] = "Wi-Fi ローカルIP一覧:",
                ["LiveTelemetry"] = "リアルタイム入力モニター",
                ["ConnectedClient"] = "接続中クライアント:",
                ["None"] = "未接続",
                ["EventRate"] = "受信レート (Event Rate)",
                ["NormalizedCoords"] = "正規化座標 (X, Y)",
                ["StylusPressure"] = "ペン筆圧 (Pressure)",
                ["Action"] = "動作種別:",
                ["Tool"] = "入力デバイス:",
                ["ActivityLog"] = "動作ログ (Activity Log)",
                ["Language"] = "表示言語:",
                ["TrayOpen"] = "Pentab Server を開く",
                ["TrayStartWithWindows"] = "Windows 起動時に自動起動",
                ["TrayStartServer"] = "サーバー起動",
                ["TrayStopServer"] = "サーバー停止",
                ["TrayExit"] = "終了",
                ["TrayTitle"] = "Pentab PC Server",
                ["TrayRunningText"] = "タスクトレイで常駐中。ダブルクリックで開きます。"
            },
            ["zh"] = new()
            {
                ["AppTitle"] = "Pentab 电脑服务器",
                ["AppSubtitle"] = "超低延迟 Android 平板数位板/触控板桥接服务器",
                ["StatusStopped"] = "服务器已停止",
                ["StatusRunning"] = "服务器运行中 (端口: {0})",
                ["HideToTray"] = "📥 最小化到托盘",
                ["ConfigTitle"] = "服务器配置",
                ["Port"] = "端口号:",
                ["TargetMonitor"] = "目标显示器:",
                ["StartupOptions"] = "自启与后台选项:",
                ["AutoStart"] = "随 Windows 开机自启",
                ["StartInTray"] = "启动时最小化到系统托盘",
                ["StartServer"] = "启动服务器",
                ["StopServer"] = "停止服务器",
                ["TestCursor"] = "测试移动到屏幕中心",
                ["QuickConnectAdb"] = "USB 有线快速连接 (ADB):",
                ["CopyAdb"] = "复制 ADB 命令",
                ["CopiedAdb"] = "已复制到剪贴板！",
                ["WifiLocalIps"] = "Wi-Fi 局域网 IP:",
                ["LiveTelemetry"] = "实时输入遥测",
                ["ConnectedClient"] = "已连接设备:",
                ["None"] = "无",
                ["EventRate"] = "接收频率 (Event Rate)",
                ["NormalizedCoords"] = "归一化坐标 (X, Y)",
                ["StylusPressure"] = "压感力度 (Pressure)",
                ["Action"] = "动作类型:",
                ["Tool"] = "输入工具:",
                ["ActivityLog"] = "运行日志 (Activity Log)",
                ["Language"] = "显示语言:",
                ["TrayOpen"] = "打开 Pentab 服务器",
                ["TrayStartWithWindows"] = "开机自启",
                ["TrayStartServer"] = "启动服务器",
                ["TrayStopServer"] = "停止服务器",
                ["TrayExit"] = "退出",
                ["TrayTitle"] = "Pentab 电脑服务器",
                ["TrayRunningText"] = "正在系统托盘运行中。双击图标打开界面。"
            }
        };

        public void SetLanguage(string lang)
        {
            if (_translations.ContainsKey(lang))
            {
                CurrentLanguage = lang;
            }
            else
            {
                CurrentLanguage = "en";
            }
            LanguageChanged?.Invoke();
        }

        public string Get(string key, params object[] args)
        {
            if (_translations.TryGetValue(CurrentLanguage, out var dict) && dict.TryGetValue(key, out var val))
            {
                return args.Length > 0 ? string.Format(val, args) : val;
            }

            if (_translations["en"].TryGetValue(key, out var defaultVal))
            {
                return args.Length > 0 ? string.Format(defaultVal, args) : defaultVal;
            }

            return key;
        }
    }
}
