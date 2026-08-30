# Pentab PC サーバー実装仕様書 (PC Server Specification)

## 1. モジュール概要

PC サーバーは、.NET 8 / WPF 上で動作し、Android 端末から送信される WebSocket 入力イベントを受信して Windows OS のマウス・ポインター入力へと高精度かつ低遅延に変換・注入するデスクトップアプリケーションです。

- **プロジェクト名**: `PentabServer`
- **ターゲットフレームワーク**: `.NET 8.0-windows`
- **UI フレームワーク**: WPF (Windows Presentation Foundation) + Windows Forms (NotifyIcon / Screen 取得用)
- **外部依存関係**: **外部 NuGet パッケージ不要** (.NET 8 標準機能のみで構築)

---

## 2. アーキテクチャとクラス責務

```
PentabServer
├── App.xaml / App.xaml.cs          # アプリケーション起動、グローバル未処理例外トラップ
├── MainWindow.xaml / .cs           # ダークテーマ UI、テレメトリ表示、トレイアイコン管理
├── Models/
│   └── PenData.cs                  # JSON デシリアライズ用データモデル (System.Text.Json)
└── Services/
    ├── WebSocketServer.cs          # RFC 6455 準拠 TcpListener WebSocket サーバー
    ├── ScreenMapper.cs             # マルチモニター / 仮想デスクトップ座標変換
    ├── InputInjector.cs            # Win32 API によるマウス/クリック/ホイール入力注入
    └── AppSettings.cs              # 設定ファイル (%AppData%) および 自動起動レジストリ
```

---

## 3. Win32 API 入力注入エンジン (`InputInjector`)

`InputInjector` は、Windows OS の `user32.dll` が提供するネイティブ API を P/Invoke 経由で呼び出し、カーソル移動やクリック操作を注入します。

### 3.1 P/Invoke 定義
```csharp
[DllImport("user32.dll", SetLastError = true)]
private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

[DllImport("user32.dll", SetLastError = true)]
private static extern bool SetCursorPos(int X, int Y);

[DllImport("user32.dll", SetLastError = true)]
private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
```

### 3.2 入力注入方式の使い分け
1. **絶対座標移動 (Absolute Pen Mode)**:
   - `SetCursorPos(pixelX, pixelY)` を使用。
   - `ScreenMapper` で計算された対象モニターのピクセル絶対座標へ瞬時にカーソルを移動させます。
2. **相対座標移動 (Trackpad Mode)**:
   - `mouse_event(MOUSEEVENTF_MOVE, rdx, rdy, 0, UIntPtr.Zero)` を使用。
   - 現在のカーソル位置を基準に相対差分だけスムーズに移動させます。
3. **クリック / ドラッグ操作**:
   - 左ボタン押し下げ: `mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero)`
   - 左ボタン離脱: `mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero)`
   - 右ボタン押し下げ: `mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero)`
   - 右ボタン離脱: `mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero)`
   - 中クリック: `MOUSEEVENTF_MIDDLEDOWN` -> `Thread.Sleep(20)` -> `MOUSEEVENTF_MIDDLEUP`
   - 左クリック: `LeftDown()` -> `Thread.Sleep(20)` -> `LeftUp()`
   - ホイールスクロール: `mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)delta, UIntPtr.Zero)`

---

## 4. RFC 6455 準拠 WebSocket サーバー (`WebSocketServer`)

外部ライブラリを使用せず、.NET 標準の `System.Net.Sockets.TcpListener` を用いて高速な WebSocket サーバーを実装しています。

### 4.1 特徴とパフォーマンス設計
- **NoDelay 有効化**: `tcpClient.NoDelay = true` (Nagle アルゴリズム無効化) によりパケット遅延を排除。
- **フレームパーサー**: マスク解除 (XOR)、Opcode (Text, Binary, Ping, Pong, Close)、ペイロード長 (7bit, 16bit extended, 64bit extended) をバイトレベルで高速解析。
- **自動 Ping/Pong**: クライアントからの Ping (`0x9`) に対し Pong (`0xA`) を即座にエコーバック。
- **古い接続の確実な切断**: 新規接続が来た際、既存の `_activeClientCts` をキャンセルして古いソケットを解放し、ゴースト接続を防止。
- **切断時の安全策**: 切断イベント発生時、押しっぱなし状態のマウスボタンを `_inputInjector.ResetButtons()` で確実にリリース。

---

## 5. 画面マッピングとマルチモニター対応 (`ScreenMapper`)

Windows のマルチモニター環境（デュアルモニター、トリプルモニター、解像度やスケーリングの異なる環境）に対応します。

### 5.1 モニター列挙と座標変換ロジック
`Screen.AllScreens` を取得し、ユーザーが指定した対象モニター（またはプライマリモニター、全仮想デスクトップ）の Bounds を取得します。

```csharp
public (int dx, int dy, int pixelX, int pixelY) MapToVirtualDesktop(float normX, float normY)
{
    var virtScreen = SystemInformation.VirtualScreen;
    // 対象モニターの境界 (targetLeft, targetTop, targetWidth, targetHeight) を取得
    
    // 1. 対象モニター内の絶対ピクセル座標を算出
    int pixelX = (int)Math.Round(targetLeft + (normX * (targetWidth - 1)));
    int pixelY = (int)Math.Round(targetTop + (normY * (targetHeight - 1)));

    // 2. モニター範囲内にクランプ
    pixelX = Math.Clamp(pixelX, (int)targetLeft, (int)(targetLeft + targetWidth - 1));
    pixelY = Math.Clamp(pixelY, (int)targetTop, (int)(targetTop + targetHeight - 1));

    // 3. 仮想デスクトップ全体 (0..65535) への正規化
    int dx = (int)Math.Round(((pixelX - virtScreen.X) * 65535.0) / (virtScreen.Width - 1));
    int dy = (int)Math.Round(((pixelY - virtScreen.Y) * 65535.0) / (virtScreen.Height - 1));

    return (dx, dy, pixelX, pixelY);
}
```

---

## 6. 設定永続化 & システムトレイ常駐

### 6.1 設定ファイル (`AppSettings`)
- 保存先: `%AppData%\PentabServer\settings.json`
- 保存項目:
  - `Port`: リッスンポート (デフォルト: `8765`)
  - `MonitorIndex`: 対象モニター番号 (`-1`: Primary, `-2`: Virtual Desktop, `0以上`: モニター番号)
  - `AutoStart`: Windows 起動時自動起動フラグ
  - `StartMinimized`: 起動時トレイ最小化フラグ

### 6.2 Windows 自動起動登録 (Registry)
レジストリ `HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` に実行ファイルパスを `--minimized` 引数付きで登録します。

```csharp
string exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PentabServer.exe");
key.SetValue("PentabServer", $"\"{exePath}\" --minimized");
```

### 6.3 システムトレイ (`System.Windows.Forms.NotifyIcon`)
- ウィンドウの「×」ボタン押下時、アプリを終了せずトレイへ格納（`e.Cancel = true; HideToTray();`）。
- トレイアイコンの右クリックメニュー: 「Open Pentab Server」「Start with Windows」「Exit」。
- Android クライアント接続時にバルーン通知をポップアップ表示。
