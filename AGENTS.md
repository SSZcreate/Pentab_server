# Pentab Server (PC側) — 実装ガイド

## プロジェクト概要
Androidタブレットから送信されるペン入力データを受信し、
Windowsのマウスカーソル操作に変換するWPFデスクトップアプリ。

## 技術スタック
- C# / .NET 8 / WPF
- System.Net.WebSockets (組み込み) — WebSocketサーバー
- System.Text.Json (組み込み) — JSONデシリアライズ
- user32.dll SendInput (P/Invoke) — マウス入力注入
- 外部NuGetパッケージは不要

## Android側アプリのリポジトリ
`c:\Users\ok122\AndroidStudioProjects\Pentab` (Kotlin/Compose)

## 通信プロトコル
WebSocket JSON on port 8765
USB接続時: `adb forward tcp:8765 tcp:8765`

### Android → PC メッセージ（受信）
```json
{
  "type": "pen_down" | "pen_move" | "pen_up",
  "x": 0.0-1.0,
  "y": 0.0-1.0,
  "pressure": 0.0-1.0,
  "tiltX": 0.0,
  "tiltY": 0.0,
  "toolType": "stylus" | "finger" | "eraser",
  "timestamp": 1693389600000
}
```

### PC → Android メッセージ（送信）
```json
{
  "type": "status",
  "connected": true,
  "screenWidth": 1920,
  "screenHeight": 1080
}
```

## 実装タスク（順番に実装すること）

### 1. データモデル作成
ファイル: `Models/PenData.cs`
```csharp
using System.Text.Json.Serialization;

namespace PentabServer.Models;

public record PenData(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("y")] float Y,
    [property: JsonPropertyName("pressure")] float Pressure,
    [property: JsonPropertyName("tiltX")] float TiltX,
    [property: JsonPropertyName("tiltY")] float TiltY,
    [property: JsonPropertyName("toolType")] string ToolType,
    [property: JsonPropertyName("timestamp")] long Timestamp
);

public record StatusMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("connected")] bool Connected,
    [property: JsonPropertyName("screenWidth")] int ScreenWidth,
    [property: JsonPropertyName("screenHeight")] int ScreenHeight
);
```

### 2. InputInjector 作成
ファイル: `Services/InputInjector.cs`
- Win32 SendInput API の P/Invoke 定義 (LibraryImport推奨)
- INPUT, MOUSEINPUT 構造体定義
- 正規化座標(0-1) → 画面絶対座標(0-65535)変換
- MoveTo(float x, float y) — カーソル移動
- MouseDown() / MouseUp() — 左クリック
- マルチモニター対応: Screen.PrimaryScreen使用

### 3. WebSocketServer 作成
ファイル: `Services/WebSocketServer.cs`
- HttpListener + WebSocket でサーバー実装
- ポート8765でリッスン
- JSONデシリアライズ → PenData
- pen_down → MouseDown + MoveTo
- pen_move → MoveTo
- pen_up → MouseUp
- 接続時にStatusMessage送信
- 非同期処理 (Task.Run)
- 接続/切断イベント通知

### 4. ScreenMapper 作成
ファイル: `Services/ScreenMapper.cs`
- タブレットの正規化座標 → PCスクリーン座標 変換
- プライマリモニターの解像度取得
- アスペクト比調整オプション

### 5. MainWindow UI 作成
ファイル: `MainWindow.xaml` + `MainWindow.xaml.cs`
- ダークテーマのモダンなUI
- サーバー起動/停止ボタン
- ポート番号設定
- 接続中デバイスの表示
- リアルタイム座標表示（デバッグ用）
- 受信レート表示(Hz)
- ADB接続ガイド表示

### 6. App.xaml — ダークテーマ設定

## P/Invoke 注意事項
- .NET 8 では `[LibraryImport]` を使用（`[DllImport]`より推奨）
- INPUT構造体のFieldOffsetに注意（x64対応）
- MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE でカーソル移動
- SendInputは管理者権限不要（通常のマウス操作として注入）

## ルール
- UIは ui-ux-pro-max スキルのWPFガイドラインに従うこと
- cybersecurity スキルに従いセキュリティを考慮すること
- 日本語コメントで実装すること
- 各ステップ実装後に `dotnet build` でビルド確認すること
- Dispatcherスレッドに注意：WebSocket受信はバックグラウンドスレッドで動く
