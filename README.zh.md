# Pentab Server (电脑端 服务器)

<div align="center">

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0078D6.svg)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4.svg)](https://dotnet.microsoft.com)

**将 Android 平板的触控与手写笔输入转化为 Windows 鼠标/手写笔操作的超轻量常驻服务器**

[English](README.md) | [日本語](README.ja.md) | **[简体中文](README.zh.md)**

[Android 客户端 (Pentab)](https://github.com/SSZcreate/Pentab) | [发布页面](https://github.com/SSZcreate/Pentab_server/releases) | [技术规格文档 (docs)](docs/01_system_architecture.md)

</div>

---

<div align="center">
  <img src="docs/images/hero_banner.png" alt="Pentab System Overview" width="85%" />
</div>

---

## 🌟 主要特性

- ⚡ **超高速·轻量单文件程序**: 无需额外安装 .NET 运行时，独立单文件 `.exe`（Self-Contained Single File）。
- 🖱️ **低延迟 Win32 原生输入注入**: 直接调用 `user32.dll` 的 `SendInput` API，实现系统级原生低延迟鼠标/手写笔操作。
- 📊 **实时数据监控**:
  - 实时接收频率（Hz / EPS）监测
  - 光标绝对坐标（X, Y）、手写笔压感（Pressure）及动作状态监控
- 📥 **系统托盘（通知区域）常驻与开机自启**:
  - 支持随 Windows 开机自动静默启动（注册表开机项）
  - 启动时可直接最小化至托盘
  - 托盘右键菜单 / 双击图标快速呼出主界面
- 🌐 **多语言界面支持 (i18n)**:
  - 支持 英语 (English)、日语 (日本語)、简体中文 实时无缝切换
- 🔌 **ADB 有线与 Wi-Fi 无线连接**:
  - 支持通过 `adb reverse` 实现极低延迟端口映射
  - 自动检测本地 IP 地址并支持一键复制
- 🖥️ **多显示器支持与宽高比映射**:
  - 支持主显示器、指定显示器或全部虚拟桌面跨屏映射

---

## 📸 应用界面截图

<div align="center">
  <img src="docs/images/server_ui.png" alt="Pentab Server PC Controller UI" width="85%" />
  <p><em>Pentab Server 主窗口（实时坐标、压感及接收频率监控）</em></p>
</div>

---

## 🚀 快速入门

### 1. 下载程序
从 [Releases](https://github.com/SSZcreate/Pentab_server/releases) 下载最新的 `PentabServer-v1.0.0-win-x64.zip` 并解压到任意文件夹。

### 2. 启动服务器
1. 运行 `PentabServer.exe`。
2. 首次启动时若弹出 Windows 防火墙提示，请勾选并允许访问“专用网络”。

### 3. 连接平板

#### A. USB 有线连接（推荐·超低延迟）
1. 连接平板至电脑，在命令行中执行：
   ```bash
   adb reverse tcp:8765 tcp:8765
   ```
2. 在平板端保持 `127.0.0.1:8765` 并点击 Connect。

#### B. Wi-Fi 无线连接
1. 点击主界面上的局域网 IP（例如 `192.168.1.xxx`）旁的“Copy”按钮。
2. 在平板端填入该 IP 并点击 Connect。

---

## ⚙️ 系统设置与后台选项

| 设置项 | 说明 |
| :--- | :--- |
| **开机自启 (Auto-Start)** | 勾选后，随 Windows 开机自动在后台启动。 |
| **启动时最小化 (Start in Tray)** | 启动时不弹出主窗口，直接最小化至右下角系统托盘。 |
| **最小化到托盘 [📥]** | 将主界面收入托盘，双击托盘图标即可唤出。 |
| **显示语言 (Language)** | 支持 English / 日本語 / 简体中文 实时切换。 |

---

## 🛠️ 编译与开发

### 环境要求
- Windows 10 / 11 (x64)
- .NET 8.0 SDK

### 编译步骤
```bash
# 克隆仓库
git clone https://github.com/SSZcreate/Pentab_server.git
cd Pentab_server

# 运行调试
dotnet run

# 发布独立单文件 exe
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

---

## 📜 开源协议

本项目基于 [MIT License](LICENSE) 协议开源。
