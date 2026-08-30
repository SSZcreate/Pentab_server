# Pentab システム全体アーキテクチャ仕様書 (System Architecture Specification)

## 1. システム概要 (System Overview)

**Pentab** は、Androidタブレット（スタイラスペン対応または指タッチ対応端末）を Windows PC の「高性能ペンタブレット」および「高精度トラックパッド」として活用するためのクライアント/サーバーシステムです。

専用のハードウェア（液晶タブレットやペンタブレット）を追加購入することなく、既存の Android タブレットを USB (ADB Reverse) または Wi-Fi 経由で Windows PC に接続し、低遅延かつ高精度なカーソル操作、ペン描画、筆圧検知、ジェスチャー操作を実現します。

---

## 2. 全体アーキテクチャ (Overall Architecture)

```
+-----------------------------------------------------------------------------------+
|                            Android Tablet (Client)                                |
|                                                                                   |
|  +---------------------------------------------+-------------------------------+  |
|  |                             Jetpack Compose UI                              |  |
|  |  +---------------------------+  +----------------------------------------+  |  |
|  |  |   ConnectionScreen        |  |            PenInputScreen              |  |  |
|  |  |  - IP/Port Config         |  |  - Mode Selector (Trackpad / Absolute) |  |  |
|  |  |  - Auto Connect Status    |  |  - Floating Action Panel (Draggable)   |  |  |
|  |  |  - ADB Guide Display      |  |  - Screen Mapping Area Config Dialog   |  |  |
|  |  +---------------------------+  +----------------------------------------+  |  |
|  +-----------------------------------------------------------------------------+  |
|                                        |                                          |
|  +-------------------------------------+---------------------------------------+  |
|  |                     PentabSurfaceView (Native View)                         |  |
|  |  - onTouchEvent (MotionEvent: Down/Move/Up/PointerDown/PointerUp)           |  |
|  |  - onGenericMotionEvent (HOVER_MOVE)                                        |  |
|  |  - Dual Mode Engine:                                                        |  |
|  |    * Trackpad: Relative displacement (dx, dy), Acceleration Curve           |  |
|  |    * Gestures: 1-Finger Tap (Left), 2-Finger Tap (Right),                   |  |
|  |                3-Finger Tap (Middle), 2-Finger Scroll (Wheel),              |  |
|  |                Double-Tap & Drag (Left Down/Move/Up)                        |  |
|  |    * Absolute: Direct coordinate normalization & Area mapping               |  |
|  +-----------------------------------------------------------------------------+  |
|                                        |                                          |
|  +-------------------------------------+---------------------------------------+  |
|  |                         WebSocketManager (OkHttp)                           |  |
|  |  - WebSocket Client (RFC 6455)                                              |  |
|  |  - Auto Reconnect (2s retry), Keep-Alive (3s ping)                          |  |
|  |  - JSON Serialization (Gson)                                                |  |
|  +-----------------------------------------------------------------------------+  |
+----------------------------------------|------------------------------------------+
                                         |
                       WebSocket over TCP / Port 8765
               (1) USB ADB Reverse: adb reverse tcp:8765 tcp:8765
               (2) Local Wi-Fi LAN: ws://<PC_IP>:8765/pentab
                                         |
+----------------------------------------v------------------------------------------+
|                             Windows PC (WPF Server)                               |
|                                                                                   |
|  +-----------------------------------------------------------------------------+  |
|  |                    WebSocketServer (Native TcpListener)                     |  |
|  |  - Lightweight RFC 6455 WebSocket Engine (zero external dependency)        |  |
|  |  - Handshake (Sec-WebSocket-Key -> SHA1 -> Sec-WebSocket-Accept)            |  |
|  |  - Frame parsing, Ping/Pong, Keep-alive, Client connection management      |  |
|  |  - JSON Deserialization (System.Text.Json -> PenData)                       |  |
|  +-----------------------------------------------------------------------------+  |
|                                        |                                          |
|  +-------------------------------------+---------------------------------------+  |
|  |                           Input & Display Pipeline                          |  |
|  |  +------------------------------------+  +-------------------------------+  |  |
|  |  |            ScreenMapper            |  |         InputInjector         |  |  |
|  |  |  - Multi-Monitor Enumeration       |  |  - Win32 API: SetCursorPos     |  |  |
|  |  |  - Target Screen Selection         |  |  - Win32 API: mouse_event      |  |  |
|  |  |  - Normalized (0-1) to Virtual     |  |  - Win32 API: SendInput        |  |  |
|  |  |    Desktop (0-65535 / Pixels)      |  |  - Left/Right/Middle Click     |  |  |
|  |  |                                    |  |  - Wheel Scroll & Drag Lock    |  |  |
|  |  +------------------------------------+  +-------------------------------+  |  |
|  +-----------------------------------------------------------------------------+  |
|                                        |                                          |
|  +-------------------------------------+---------------------------------------+  |
|  |                     WPF Presentation & System Host                          |  |
|  |  - MainWindow (Dark Theme, Real-time EPS counter, Pressure bar, Coords)     |  |
|  |  - System Tray Integration (NotifyIcon: Minimize to Tray, Auto-Start)       |  |
|  |  - AppSettings (JSON config in %AppData% + Registry HKCU\...\Run)           |  |
|  +-----------------------------------------------------------------------------+  |
+-----------------------------------------------------------------------------------+
```

---

## 3. 主要コンポーネントと責務 (Components and Responsibilities)

### 3.1 Android クライアント側 (`com.example.pentab`)
| コンポーネント | ファイル | 責務 |
| :--- | :--- | :--- |
| **MainActivity** | `MainActivity.kt` | ライフサイクル管理、Edge-to-Edge 全画面制御、キャンバス時のシステムバー隠蔽 (Immersive Mode)、画面遷移制御 |
| **PenData Model** | `data/PenData.kt` | ペン/タッチデータ、モード、アクション種別、ボタン状態、タイムスタンプを定義するシリアライズモデル |
| **WebSocketManager** | `network/WebSocketManager.kt` | OkHttp による WebSocket クライアント。自動再接続、死活監視、接続状態 StateFlow 公開、JSON 送信 |
| **PentabSurfaceView** | `ui/PenInputView.kt` | 描画領域 & 高速タッチ/スタイラス入力パーサー。トラックパッド相対移動・加速度曲線・マルチタッチジェスチャー・絶対座標変換 |
| **PenInputScreen** | `ui/PenInputView.kt` | Compose UI。フローティング操作パネル（位置ドラッグ移動、縦横切替、クリック/ドラッグロック）、テレメトリ表示、マッピング設定ダイアログ |
| **ConnectionScreen** | `ui/ConnectionScreen.kt` | 接続設定画面。IP/ポート入力、自動接続、接続ステータス、ADB 接続ガイド |

### 3.2 PC WPF サーバー側 (`PentabServer`)
| コンポーネント | ファイル | 責務 |
| :--- | :--- | :--- |
| **App / MainWindow** | `App.xaml.cs`, `MainWindow.xaml.cs` | アプリケーションエントリーポイント、例外トラップ、WPF UI、システムトレイ常駐、レート計算 (EPS) |
| **PenData Model** | `Models/PenData.cs` | Android から受信した JSON を `System.Text.Json` で高速デシリアライズする C# レコード/クラス |
| **WebSocketServer** | `Services/WebSocketServer.cs` | .NET 標準の `TcpListener` を用いた RFC 6455 準拠の自作 WebSocket サーバー。外部 NuGet 依存ゼロ、Ping/Pong、Status メッセージ送信 |
| **ScreenMapper** | `Services/ScreenMapper.cs` | プライマリモニター、仮想デスクトップ、各物理モニターの境界取得と座標変換 (0.0〜1.0 → ピクセル / 0〜65535) |
| **InputInjector** | `Services/InputInjector.cs` | Win32 API (`SetCursorPos`, `mouse_event`, `SendInput`) による Windows OS への入力注入 |
| **AppSettings** | `Services/AppSettings.cs` | 設定保存 (`%AppData%\PentabServer\settings.json`) および Windows 起動時自動起動レジストリ登録 |
