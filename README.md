# Pentab Server (PC Side)

<div align="center">

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0078D6.svg)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4.svg)](https://dotnet.microsoft.com)

**Ultra-lightweight resident PC server converting Android tablet touch and stylus inputs into native Windows mouse & pen actions**

**[English](README.md)** | [日本語](README.ja.md) | [简体中文](README.zh.md)

[Android Client (Pentab)](https://github.com/SSZcreate/Pentab) | [Releases](https://github.com/SSZcreate/Pentab_server/releases) | [Technical Docs](docs/01_system_architecture.md)

</div>

---

<div align="center">
  <img src="docs/images/hero_banner.png" alt="Pentab System Overview" width="85%" />
</div>

---

## 🌟 Key Features

- ⚡ **Ultra-Fast & Lightweight Single Binary**: Standalone single executable `.exe` (Self-Contained Single File) requiring no external .NET runtime installation.
- 🖱️ **Low Latency Win32 Direct Input Injection**: Direct P/Invoke to `user32.dll` `SendInput` API for lowest possible latency OS-native pointer control.
- 📊 **Real-time Telemetry & Monitoring**:
  - Live packet rate (Hz / EPS) counter
  - Real-time Cursor Coordinates (X, Y), Stylus Pressure, and Action mode
- 📥 **System Tray Resident & Windows Auto-Start**:
  - Auto-Start on Windows boot (via Registry Run key)
  - Direct startup to System Tray (Notification Area)
  - Right-click tray menu & double-click to instantly restore window
- 🌐 **Multi-Language UI (i18n)**:
  - Instant one-click language switching between English, Japanese (日本語), and Simplified Chinese (简体中文).
- 🔌 **ADB Wired & Wi-Fi Wireless Support**:
  - Port forwarding via `adb reverse` for ultra-low latency wired experience
  - Automatic local IP detection with one-click copy button
- 🖥️ **Multi-Monitor & Aspect Ratio Mapping**:
  - Supports Primary Display, specific secondary screens, or Entire Virtual Desktop

---

## 📸 Application Screenshots

<div align="center">
  <img src="docs/images/server_ui.png" alt="Pentab Server PC Controller UI" width="85%" />
  <p><em>Pentab Server Main Window (Real-time Coords, Pressure, and Rate Monitoring)</em></p>
</div>

---

## 🚀 Quick Start

### 1. Download
Download the latest `PentabServer-v1.0.0-win-x64.zip` from [Releases](https://github.com/SSZcreate/Pentab_server/releases) and extract it anywhere.

### 2. Launch Server
1. Launch `PentabServer.exe`.
2. If Windows Defender / Firewall prompts on initial launch, allow access for Private Networks.

### 3. Connect Tablet

#### A. USB Connection (Recommended · Lowest Latency)
1. Connect your Android tablet via USB with USB Debugging enabled, and run in PowerShell / CMD:
   ```bash
   adb reverse tcp:8765 tcp:8765
   ```
2. On your tablet, leave host as `127.0.0.1:8765` and tap **"Connect"**.

#### B. Wi-Fi Wireless Connection
1. Click the "Copy" button next to the detected local IP (e.g. `192.168.1.xxx`).
2. Enter the IP into your tablet's host field and tap **"Connect"**.

---

## ⚙️ Settings & Background Options

| Option | Description |
| :--- | :--- |
| **Auto-Start with Windows** | Automatically launches PentabServer silently in background on Windows boot. |
| **Start in Tray** | Starts minimized into the system notification area without displaying the main window. |
| **Hide to Tray [📥]** | Minimizes window to system tray. Double-click tray icon to restore. |
| **Language** | Switch UI language dynamically between English, Japanese, and Chinese. |

---

## 🛠️ Development & Build

### Requirements
- Windows 10 / 11 (x64)
- .NET 8.0 SDK

### Build Instructions
```bash
# Clone the repository
git clone https://github.com/SSZcreate/Pentab_server.git
cd Pentab_server

# Debug Run
dotnet run

# Publish Standalone Single-file Executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

---

## 📄 Documentation

- [System Architecture Document](docs/01_system_architecture.md)
- [Protocol Specification (WebSocket JSON)](docs/02_protocol_specification.md)
- [Android Client Specification](docs/03_android_client_specification.md)
- [PC Server Specification](docs/04_pc_server_specification.md)
- [Build and Deployment Guide](docs/05_build_and_deployment_guide.md)

---

## 📜 License

This project is licensed under the [MIT License](LICENSE).
