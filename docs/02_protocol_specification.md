# Pentab 通信プロトコル仕様書 (Protocol Specification)

## 1. 通信方式の概要

Pentab は、Android クライアントと PC WPF サーバー間で低遅延双方向通信を行うために **RFC 6455 準拠の WebSocket (TCP)** を使用します。

- **トランスポート層**: TCP (デフォルトポート: `8765`)
- **プロトコル層**: WebSocket (`ws://<host>:<port>/pentab`)
- **データ形式**: UTF-8 JSON
- **通信経路**:
  1. **USB 有線接続**: ADB Reverse Port Forwarding (`adb reverse tcp:8765 tcp:8765`) による超低遅延（1ms以下）通信
  2. **Wi-Fi LAN 接続**: 同一ローカルネットワーク内のダイレクト TCP 通信

---

## 2. WebSocket ハンドシェイクとフレーム仕様

### 2.1 HTTP アップグレードハンドシェイク
クライアント (Android OkHttp) からサーバー (PC TcpListener) へ通常の HTTP GET リクエストを送信し、プロトコルを WebSocket に昇格します。

**クライアント要求 (Request):**
```http
GET /pentab HTTP/1.1
Host: 127.0.0.1:8765
Upgrade: websocket
Connection: Upgrade
Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==
Sec-WebSocket-Version: 13
```

**サーバー応答 (Response):**
```http
HTTP/1.1 101 Switching Protocols
Upgrade: websocket
Connection: Upgrade
Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=
```

> **Accept Key 生成ロジック**:
> `Sec-WebSocket-Key` の値にマジック文字列 `"258EAFA5-E914-47DA-95CA-C5AB0DC85B11"` を連結し、SHA-1 ハッシュを計算後 Base64 エンコード。

### 2.2 フレーム構造とサポートする Opcode
| Opcode | 種類 | 用途 | 処理内容 |
| :--- | :--- | :--- | :--- |
| `0x1` | Text Frame | JSON ペイロード | PenData または StatusMessage の送受信 |
| `0x8` | Connection Close | 切断要求 | ソケットクローズとリソース開放 |
| `0x9` | Ping | キープアライブ監視 | クライアントへ Pong (`0xA`) を即時返信 |
| `0xA` | Pong | キープアライブ応答 | 接続維持の確認 |

---

## 3. メッセージスキーマ (Message Schema)

### 3.1 Android → PC (入力イベント: `PenData`)

Android 端末上で発生したすべてのタッチ、スタイラス、ジェスチャー、ボタン操作イベントは以下の JSON スキーマで PC へ送信されます。

```json
{
  "mode": "TRACKPAD",
  "x": 0.542,
  "y": 0.318,
  "dx": 4.5,
  "dy": -2.1,
  "pressure": 0.85,
  "tiltX": 0.0,
  "tiltY": 0.0,
  "toolType": 2,
  "action": "MOVE",
  "clickType": "",
  "buttonState": 0,
  "scrollDelta": 0,
  "timestamp": 1756542642100
}
```

#### フィールド定義一覧

| フィールド名 | 型 | 必須 | デフォルト | 説明 |
| :--- | :--- | :--- | :--- | :--- |
| `mode` | String | ○ | `"ABSOLUTE"` | 動作モード。`"TRACKPAD"` (相対トラックパッド) または `"ABSOLUTE"` (絶対ペンタブ) |
| `x` | Float | ○ | `0.0` | 画面横方向の正規化座標 (`0.0`〜`1.0`)。左端=0.0, 右端=1.0 |
| `y` | Float | ○ | `0.0` | 画面縦方向の正規化座標 (`0.0`〜`1.0`)。上端=0.0, 下端=1.0 |
| `dx` | Float | - | `0.0` | トラックパッドモード時の X 軸相対移動量 (ピクセル / 加速度適用済) |
| `dy` | Float | - | `0.0` | トラックパッドモード時の Y 軸相対移動量 (ピクセル / 加速度適用済) |
| `pressure` | Float | - | `1.0` | 筆圧値 (`0.0`〜`1.0`)。指タッチ時は 1.0、スタイラスペン時は圧力センサー値 |
| `tiltX` | Float | - | `0.0` | スタイラスペンの X 軸傾き角度 (ラジアン/度) |
| `tiltY` | Float | - | `0.0` | スタイラスペンの Y 軸傾き角度 (ラジアン/度) |
| `toolType` | Int | ○ | `1` | 入力デバイス種別。<br>`0`: UNKNOWN, `1`: FINGER (指), `2`: STYLUS (ペン), `3`: ERASER (消しゴム), `4`: MOUSE |
| `action` | String | ○ | - | アクション種別（下記表参照） |
| `clickType` | String | - | `""` | クリック種別。<br>`"LEFT"`: 左クリック, `"RIGHT"`: 右クリック, `"MIDDLE"`: 中クリック, `"DOUBLE_LEFT"`: 左ダブルクリック |
| `buttonState` | Int | - | `0` | Android MotionEvent ボタン状態 (`1`: BUTTON_PRIMARY, `2`: BUTTON_SECONDARY, `32`: BUTTON_STYLUS_PRIMARY 等) |
| `scrollDelta` | Int | - | `0` | マウスホイールスクロール量 (正=上スクロール, 負=下スクロール) |
| `timestamp` | Long | ○ | 現在時刻 | イベント発生ミリ秒時刻 (`System.currentTimeMillis()`) |

---

### 3.2 アクション種別一覧 (`action`)

| action 名 | モード | 説明 | PC サーバーでの処理 |
| :--- | :--- | :--- | :--- |
| `MOVE` | TRACKPAD | 指のスワイプ移動 | `mouse_event(MOUSEEVENTF_MOVE, dx, dy)` |
| `MOVE` | ABSOLUTE | スタイラス/指の絶対移動 | `SetCursorPos(targetPixelX, targetPixelY)` |
| `DOWN` | ABSOLUTE | ペン/指の接地 | `SetCursorPos` + `mouse_event(MOUSEEVENTF_LEFTDOWN)` |
| `UP` | 両方 | 指/ペンの離脱 | `ResetButtons()` (`MOUSEEVENTF_LEFTUP` / `RIGHTUP`) |
| `HOVER_MOVE` | ABSOLUTE | スタイラスのホバー追尾 | `SetCursorPos(targetPixelX, targetPixelY)` |
| `CLICK` | 両方 | タップによるクリック実行 | `clickType` に応じて `LeftClick()`, `RightClick()`, `MiddleClick()` |
| `SCROLL` | TRACKPAD | 2本指スワイプスクロール | `mouse_event(MOUSEEVENTF_WHEEL, scrollDelta)` |
| `DOWN_LEFT` | 両方 | 左ボタン明示的押し下げ (ドラッグ開始) | `mouse_event(MOUSEEVENTF_LEFTDOWN)` |
| `UP_LEFT` | 両方 | 左ボタン明示的離脱 (ドラッグ終了) | `mouse_event(MOUSEEVENTF_LEFTUP)` |
| `DOWN_RIGHT` | 両方 | 右ボタン明示的押し下げ | `mouse_event(MOUSEEVENTF_RIGHTDOWN)` |
| `UP_RIGHT` | 両方 | 右ボタン明示的離脱 | `mouse_event(MOUSEEVENTF_RIGHTUP)` |
| `CANCEL` | 両方 | ジェスチャーキャンセル | `ResetButtons()` |

---

### 3.3 PC → Android (ステータスメッセージ: `StatusMessage`)

WebSocket 接続確立直後に PC サーバーから Android クライアントへ送信されるシステム状態通知です。

```json
{
  "type": "status",
  "connected": true,
  "screenWidth": 1920,
  "screenHeight": 1080
}
```

#### フィールド定義
- `type` (String): `"status"` 固定
- `connected` (Boolean): 接続成否 (`true`)
- `screenWidth` (Int): PC プライマリモニターの解像度幅 (px)
- `screenHeight` (Int): PC プライマリモニターの解像度高さ (px)

---

## 4. ネットワーク接続・設定手順

### 4.1 USB ADB Reverse 接続 (推奨・超低遅延)
1. Android 端末の「設定」→「端末情報」→「ビルド番号」を7回タップして「開発者向けオプション」を有効化。
2. 「開発者向けオプション」内の「USBデバッグ」を ON にする。
3. タブレットを USB ケーブルで PC に接続。
4. PC のコマンドプロンプトまたは PowerShell で以下を実行:
   ```bash
   adb reverse tcp:8765 tcp:8765
   ```
   > これにより、Android 端末内の `127.0.0.1:8765` 宛てのパケットが USB を通じて PC 側のポート `8765` へ転送されます。
5. Android アプリの IP に `127.0.0.1`、ポートに `8765` を指定して Connect をタップ。

### 4.2 Wi-Fi LAN 接続 (ワイヤレス)
1. Android 端末と PC を同一の Wi-Fi ネットワーク（ルーター）に接続。
2. PC 側アプリの UI 上に表示されている Wi-Fi ローカル IP (例: `192.168.1.50`) を確認。
3. Android アプリの IP に上記 PC の IP を入力して Connect をタップ。
4. Windows Defender ファイアウォールでポート `8765` (TCP) の受信を許可する。
