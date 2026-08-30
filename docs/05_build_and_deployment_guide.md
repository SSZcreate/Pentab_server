# Pentab ビルド・環境構築・運用ガイド (Build & Deployment Guide)

## 1. 開発・動作環境要件

### 1.1 Android クライアント側
- **OS**: Android 7.0 (API レベル 24) 以上（Android 12〜14 推奨）
- **ビルドツール**: Android Gradle Plugin (AGP) 8.x / 9.x, Gradle 8.x / 9.x
- **JDK**: OpenJDK 17 または 21 (Java 11 コンパイルターゲット)
- **推奨ハードウェア**: スタイラスペン（S-Pen, USI Pen 等）またはタッチ対応 Android タブレット / スマートフォン

### 1.2 PC サーバー側
- **OS**: Windows 10 / Windows 11 (64-bit)
- **ランタイム**: .NET 8.0 Runtime (Desktop Runtime)
- **SDK (ビルド時)**: .NET 8.0 SDK

---

## 2. ビルド手順

### 2.1 Android アプリのビルド
プロジェクトルート (`c:\Users\ok122\AndroidStudioProjects\Pentab`) にて実行:

```powershell
# デバッグ APK のビルド
./gradlew.bat assembleDebug

# ビルド成果物パス:
# app/build/outputs/apk/debug/app-debug.apk

# 接続中の Android 端末へのインストール
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

### 2.2 PC サーバーのビルド
プロジェクトルート (`c:\Users\ok122\myappdev\Pentab_server`) にて実行:

```powershell
# デバッグビルド
dotnet build -c Debug

# 自己完結型リリース単一実行ファイル (Single-File) のパブリッシュ
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o ./publish
```

---

## 3. 接続・セットアップ手順

### 3.1 USB 接続手順 (最推奨: 遅延 1ms 未満)

1. Android 端末の「設定」→「端末情報」→「ビルド番号」を7回タップして「開発者向けオプション」を有効化。
2. 「開発者向けオプション」内の「USBデバッグ」を ON にする。
3. タブレットを USB ケーブルで PC に接続。
4. PC 側で PowerShell またはコマンドプロンプトを開き、ポートフォワーディングを実行:
   ```bash
   adb reverse tcp:8765 tcp:8765
   ```
5. PC 側で `PentabServer.exe` を起動（ポート 8765 で待機開始）。
6. Android 側で `Pentab` アプリを起動し、IP: `127.0.0.1`、Port: `8765` のまま **Connect** をタップ。
7. 即座に接続完了し、全画面キャンバスに切り替わります。

### 3.2 Wi-Fi LAN 接続手順 (ワイヤレス)

1. PC と Android 端末を同一の Wi-Fi ルーターに接続。
2. PC 側で `PentabServer.exe` を起動し、画面左下に表示される **Wi-Fi Local IPs** (例: `192.168.1.50`) を確認。
3. Android 側アプリの PC IP Address 欄に `192.168.1.50` を入力し、**Connect** をタップ。
4. ※ 初回起動時、Windows ファイアウォールの許可ダイアログが表示された場合は「プライベート ネットワーク」にチェックを入れてアクセスを許可してください。

---

## 4. トラブルシューティング & FAQ

### Q1. Android で「Connection Error」と表示され接続できない
- **USB 接続の場合**: `adb devices` で端末が認識されているか確認し、再度 `adb reverse tcp:8765 tcp:8765` を実行してください。
- **Wi-Fi 接続の場合**: PC と端末が別々のネットワーク（例: 2.4GHz と 5GHz でクライアント分離機能が有効なルーター）にいないか確認し、Windows Defender ファイアウォールでポート 8765 が遮断されていないか確認してください。

### Q2. 指で操作するとカーソルが意図せずドラッグや範囲選択になってしまう
- **解決策**: アプリ上部のモード切替チップで **「Mode: Trackpad」** に設定されていることを確認してください。Trackpad モードではスワイプは「移動」、タップは「クリック」として分離認識されます。

### Q3. PC 画面の縦横比とタブレット画面の縦横比が合わない
- **解決策**: アプリ上部の **「Area (プリセット)」** ボタンをタップし、お使いの PC モニターに合わせて `16:9 PC` や `80% Center`、またはカスタムスライダーでアクティブ領域を調整してください。

### Q4. PC サーバーを常駐させて Windows 起動時に自動で立ち上げたい
- **解決策**: PC サーバー画面の **「Windows起動時に自動起動 (Auto-Start)」** および **「起動時にトレイへ最小化 (Start in Tray)」** のチェックボックスを ON にしてください。次回 Windows 起動時からバックグラウンドで自動的にポート待機を開始します。
