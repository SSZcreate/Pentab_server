# Pentab Android クライアント実装仕様書 (Android Client Specification)

## 1. モジュール概要

Android クライアントは、Kotlin + Jetpack Compose を基盤とし、高精度タッチ・スタイラス入力を取り扱うためのカスタム View (`PentabSurfaceView`) と、高速 WebSocket クライアント (`WebSocketManager`) で構成されます。

- **パッケージ名**: `com.example.pentab`
- **対象 SDK**: minSdk `24` / targetSdk `37` / compileSdk `37`
- **言語 & UI**: Kotlin 2.2.10 / Jetpack Compose BOM 2026.02.01 / Material 3
- **通信ライブラリ**: OkHttp 4.12.0 / Gson 2.11.0

---

## 2. 画面構成とライフサイクル

### 2.1 画面一覧
1. **ConnectionScreen (`ui/ConnectionScreen.kt`)**
   - サーバー接続設定画面。
   - IP アドレス、ポート番号の入力。
   - リアルタイム接続ステータスインジケーター（緑=接続中, 橙=接続試行中, 赤=切断/エラー）。
   - ADB Reverse ガイド、Wi-Fi 接続ガイド表示。
   - 起動時に前回/デフォルト設定（`127.0.0.1:8765`）で自動接続を試行。
   - 接続成功時、自動的に `PenInputScreen` へ画面遷移。

2. **PenInputScreen (`ui/PenInputView.kt`)**
   - メイン入力・キャンバス画面。
   - バックグラウンドで全画面の `PentabSurfaceView` をレンダリング。
   - 上部ヘッダー（接続状態、テレメトリ情報、モード切替チップ、画面マッピング設定、プリセット切替、設定ボタン）。
   - フローティング操作パネル（位置ドラッグ移動、縦型/横型切替、Left Click [長押しドラッグ]、Right Click、Middle Click、Drag Lock トグル）。

### 2.2 全画面 Immersive Mode 制御 (`MainActivity.kt`)
`PenInputScreen` 表示時は、システムバー（ステータスバー・ナビゲーションバー）を自動的に非表示化し、全画面を入力領域として利用します。

```kotlin
private fun setImmersiveMode(hideBars: Boolean) {
    val windowInsetsController = WindowCompat.getInsetsController(window, window.decorView)
    if (hideBars) {
        windowInsetsController.systemBarsBehavior =
            WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        windowInsetsController.hide(WindowInsetsCompat.Type.systemBars())
    } else {
        windowInsetsController.show(WindowInsetsCompat.Type.systemBars())
    }
}
```

---

## 3. 入力処理エンジン (`PentabSurfaceView`)

`PentabSurfaceView` は `android.view.View` を継承したネイティブタッチサーフェスです。Compose の `AndroidView` を経由して統合されています。

### 3.1 デュアル動作モード
1. **Trackpad Mode (デフォルト・指操作用)**:
   - 相対移動 (`dx`, `dy`) を送信し、PC カーソルをノート PC のトラックパッドのように滑らかに操作。
   - マルチタッチジェスチャー判定（タップでクリック、2本指スクロール、ダブルタップドラッグ）。
2. **Absolute Pen Mode (液タブ・ペンタブ用)**:
   - タブレット上の座標を 0.0〜1.0 に正規化し、PC 画面上の絶対位置へカーソルを直接マッピング。
   - スタイラスのホバー移動 (`ACTION_HOVER_MOVE`)、筆圧 (`event.pressure`)、傾き検知に対応。

---

### 3.2 トラックパッドジェスチャー判定アルゴリズム

```
[MotionEvent]
      |
      v
[操作パネル境界除外判定 (isInsideTrackpad)]
      |
      +---> 有効領域外: フローティングボタン側の Compose タッチイベントへ委譲
      |
      v 有効領域内
[タッチ本数追跡 (trackpadCount / maxPointersInGesture)]
      |
      +---> 1本指スワイプ: 
      |       - 移動差分 (dx, dy) を算出
      |       - 変位制限: -80px <= rawDx, rawDy <= 80px
      |       - 動的加速度曲線適用 (speedFactor: 1.0x 〜 2.4x)
      |       - PenData(mode="TRACKPAD", action="MOVE", dx, dy) 送信
      |
      +---> 2本指スワイプ:
      |       - 2点の中心 Y 座標の変位 (scrollDeltaY) を追尾
      |       - スクロール感度変換: wheelDelta = (scrollDeltaY * 7.5f).toInt()
      |       - PenData(mode="TRACKPAD", action="SCROLL", scrollDelta) 送信
      |
      +---> 離脱時 (ACTION_UP) のジェスチャー分類:
              - 1本指かつ時間 < 270ms かつ 移動距離 < 28px:
                  => 【1本指タップ: 左クリック】 (action="CLICK", clickType="LEFT")
              - 2本指かつ時間 < 350ms かつ 移動距離 < 45px:
                  => 【2本指タップ: 右クリック】 (action="CLICK", clickType="RIGHT")
              - 3本指かつ時間 < 350ms かつ 移動距離 < 50px:
                  => 【3本指タップ: 中クリック】 (action="CLICK", clickType="MIDDLE")
              - ダブルタップ & ドラッグ判定:
                  => 直前のタップ完了から 320ms 以内 かつ 距離 50px 以内に再接地した場合:
                     ダブルタップドラッグモードに突入 (DOWN_LEFT 送信 -> 移動 -> UP_LEFT 送信)
```

#### 加速度曲線 (Adaptive Acceleration Curve)
微小な操作ではドット単位の精密な位置合わせが可能となり、素早いスワイプでは画面端まで一気に届くように速度倍率を変化させます。
```kotlin
val dist = hypot(rawDx.toDouble(), rawDy.toDouble()).toFloat()
val speedFactor = when {
    dist < 2.5f -> 1.0f    // 精密操作
    dist < 8.0f -> 1.35f   // 通常移動
    dist < 25.0f -> 1.85f  // 高速スワイプ
    else -> 2.4f          // 最大加速
}
val scaledDx = rawDx * speedFactor
val scaledDy = rawDy * speedFactor
```

---

### 3.3 画面マッピング領域 (Active Mapping Area)

タブレットと PC モニターのアスペクト比の差異（例: タブレット 16:10、PC 16:9 やウルトラワイド）を解消するため、タブレット画面内の特定領域だけを PC 画面全体に対応させるマッピング機能を備えています。

```kotlin
fun mapCoordinates(rawX: Float, rawY: Float, w: Float, h: Float): Pair<Float, Float> {
    val rawNormX = (rawX / w).coerceIn(0f, 1f)
    val rawNormY = (rawY / h).coerceIn(0f, 1f)
    val areaW = (mappingRightRatio - mappingLeftRatio).coerceAtLeast(0.05f)
    val areaH = (mappingBottomRatio - mappingTopRatio).coerceAtLeast(0.05f)
    val mappedX = ((rawNormX - mappingLeftRatio) / areaW).coerceIn(0f, 1f)
    val mappedY = ((rawNormY - mappingTopRatio) / areaH).coerceIn(0f, 1f)
    return Pair(mappedX, mappedY)
}
```

#### 提供プリセット
- `FULL`: 全画面 (0%〜100%)
- `16:9 PC`: 上下 5% 余白を付与し 16:9 PC 画面に完全アスペクト比一致
- `16:10`: 16:10 モニター向け
- `80% Center`: 中央 80% 領域 (余白 10%)
- `Right 65%` / `Left 65%`: 片手操作・右手利き/左手利き用エリア
- `CUSTOM`: スライダーによる上下左右マージン自由設定

---

### 3.4 操作パネル境界除外 (Hit Exclusion)
Compose のフローティング操作パネルが配置されている領域を `PentabSurfaceView.buttonPanelBounds` に通知し、パネル上のボタンをタップした際にトラックパッドの誤動作やカーソルジャンプが発生しないよう除外します。

```kotlin
private fun isInsideTrackpad(x: Float, y: Float): Boolean {
    val density = resources.displayMetrics.density
    val topBarHeight = 65f * density
    if (y < topBarHeight) return false // 上部ヘッダー領域を除外

    buttonPanelBounds?.let { bounds ->
        val margin = 8f * density
        if (x >= (bounds.left - margin) && x <= (bounds.right + margin) &&
            y >= (bounds.top - margin) && y <= (bounds.bottom + margin)) {
            return false // フローティングパネル領域を除外
        }
    }
    return true
}
```

---

## 4. 通信マネージャー (`WebSocketManager`)

- **スレッドモデル**: 送信および再接続は Kotlin Coroutines (`Dispatchers.IO`) で非同期実行。
- **接続状態管理**: `ConnectionState` (Disconnected, Connecting, Connected, Error) を `StateFlow` で Compose UI へ公開。
- **キープアライブ**: OkHttp の `pingInterval(3, TimeUnit.SECONDS)` により TCP コネクションの切断を即座に検知。
- **自動再接続**: 予期せぬ切断時は 2 秒待機後に自動再接続ループを実行。手動切断時は再接続を停止。
- **プール破棄**: 再接続時に滞留したデッドソケットを `client.connectionPool.evictAll()` で完全クリーンアップ。
