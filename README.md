# MCHOSE Offline Device Hub

[![Build and Release](https://github.com/mchose-offline/mchose-hub/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/mchose-offline/mchose-hub/actions/workflows/build-and-release.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-blue.svg)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Telemetry](https://img.shields.io/badge/Telemetry-Zero%20%2F%20100%25%20Offline-brightgreen.svg)]()

A lightweight, **100% native Windows offline driver and hardware configurator** for **MCHOSE** gaming mice (including the **A7 V2 Ultra / Pro**, **AX5**, **K7**, **M7**, and **A5** series).

Zero background telemetry, zero cloud dependencies, instant startup, and full control over your mouse's onboard hardware memory.

---

## Why MCHOSE Offline Hub?

The official web driver requires active internet connectivity, loads megabytes of third-party tracking scripts and cloud loaders, and cannot run in air-gapped or low-latency competitive gaming environments.

**MCHOSE Offline Hub** communicates directly with your mouse and 2.4G wireless dongle via native Windows HID APIs:

- 🚀 **100% Offline & Private:** Zero network requests, zero telemetry, zero analytics.
- ⚡ **Lightweight Native WPF:** Starts in <0.2s, consumes ~35 MB of RAM (compared to ~300+ MB for Electron or browser tabs).
- 💾 **Onboard Firmware Writing:** Changes are saved directly to the mouse's internal flash memory — settings persist even if plugged into another PC or console.
- 🎯 **8000 Hz (8K) Ready:** Full support for 125 Hz up to 8000 Hz polling rates in both wired and 2.4G wireless modes.
- 🔬 **Built-in Hardware Tester:** Live RawInput polling rate counter (current, average, peak Hz), click latency tester, and button visualizer.
- 📜 **Raw HID Packet Stream:** Built-in live hex monitor showing every command and response between the OS and the mouse controller.

---

## Supported Mice

| Model | Generation | Protocol Type | Tested & Verified |
|---|---|---|:---:|
| **MCHOSE A7 V2 Ultra / Pro** | PAW3950 / Realtek / Nordic | Classic Feature Report (XOR 0xFF) | ✅ **Full Support** |
| **MCHOSE A7 / A7 Pro** | PAW3395 | Classic Feature Report | ✅ **Full Support** |
| **MCHOSE AX5 / AX5 Pro / Max** | Magnesium Alloy / PAW3395 | Classic Feature Report | ✅ **Full Support** |
| **MCHOSE L7 / M7 Series** | PAW3395 / Nordic | Classic Feature Report | ✅ **Full Support** |
| **MCHOSE A5 / A5 Pro / A5 Max** | PAW3395 | Protocol V1 / V2 Auto-detect | ✅ **Supported** |
| **MCHOSE K7 Series** | PAW3395 | Protocol V2 Output Reports | ✅ **Supported** |

---

## Features

### 🎛️ Performance & Sensor Tuning
- **Polling Rate:** `125 Hz`, `500 Hz`, `1000 Hz`, `2000 Hz`, `4000 Hz`, and `8000 Hz`.
- **Lift-Off Distance (LOD):** Selectable between `0.7 mm` (low), `1.0 mm` (default), and `2.0 mm` (high).
- **Sensor Angle:** Rotate sensor tracking angle from `-30°` to `+30°` (useful for slanted mouse grips).
- **Ripple Control:** Smooths out micro-jitter at extreme DPIs without introducing input latency.
- **Angle Snapping:** Optional linear horizontal/vertical correction for design work.
- **Motion Sync:** Synchronizes sensor readings with USB polling frames.
- **Performance Curves:** Choose between **Eco** (energy saving), **Gaming** (balanced latency), or **Extreme Mode** (highest sensor responsiveness).

### 🎯 DPI Stages
- 6 fully customizable DPI stages ranging from **50 to 42,000 DPI** (in 50-DPI increments).
- One-click active DPI stage switching with live color badges.
- Hardware-level synchronization with the physical DPI button on the underside of the mouse.

### 🔋 Battery & Power Management
- Accurate wireless battery percentage indicator and charging status.
- Sleep timer control: Set auto-sleep standby between **1 and 60 minutes**, or choose **Never Sleep**.
- Left & Right switch key debounce adjustment from **1 ms to 20 ms** to eliminate double-clicking while keeping latency minimal.

### 🧪 Hardware Diagnostics & Tester Window (`F8`)
- Integrated Fullscreen **Hardware Tester** (press `F8` or click the header button):
  - High-precision Windows `RawInput` polling rate meter displaying live current Hz, average Hz, and peak Hz with microsecond timestamps.
  - **GPU DirectX-Accelerated Motion Trail:** Smooth, zero-lag dot trail showing registered hardware polling density with zero CPU overhead.
  - **Switch Latency & Chatter Detector:** Measures click-to-click interval (ms) and detects switch bounce/double-clicking.
  - Interactive mouse button test with physical switch indicators (Left, Right, Middle, Side 1, Side 2).
  - Quick reset via `[R]` and instant exit via `[ESC]`.

---

## Summary of Reverse-Engineered Protocol

For full protocol analysis, packet structure tables, and byte register definitions, see **[REVERSE_ENGINEERING_MCHOSE.md](REVERSE_ENGINEERING_MCHOSE.md)**.

### 1. Classic Feature Report Protocol (A7 / A7 V2 / AX5 / L7 / M7)
- **Communication Type:** USB HID Feature Reports (`GetFeature` / `SetFeature`).
- **Report IDs:** `0x11` (17 decimal, 20 bytes) and `0x12` (18 decimal, 64 bytes).
- **Bitwise XOR 0xFF Inversion:** Payload bytes sent to or received from the mouse controller are bitwise inverted (`byte ^ 0xFF`) and padded with `0xFF`.
- **Primary Commands:**
  - `0x11 [0x41, usbRate, wirelessRate]`: Polling rate (`0`=125Hz, `1`=500Hz, `2`=1000Hz, `3`=2000Hz, `4`=4000Hz, `5`=8000Hz).
  - `0x11 [0x42, lod, ripple, snap, motionSync, 0, 0, gameMode, 1, rotateVal]`: Sensor tuning & LOD.
  - `0x11 [0x43, debounceMs]`: Key debounce time (1–20 ms).
  - `0x11 [0x0A, sleepStatus, sleepMinutes]`: Standby timer.
  - `0x11 [0x06]`: Battery percentage and charging query.
  - `0x12 [0x40, activeIdx, activeIdx, 0x00, stages(12 bytes), ...]`: 6 DPI stage configuration.
  - `0x12 [0x67]`: Fetch all active onboard settings.

### 2. Protocol V2 (A5 V3 / K7 V2 Series)
- **Communication Type:** USB HID Output Reports (`0x4D` / ASCII `'M'`, 64 bytes).
- Commands include `0x0102` (Performance & Polling Rate), `0x0103` (DPI Tables), `0x0109` (LOD), and `0x0900` (Device Status & Battery) with an 8-bit XOR checksum.

---

## Download & Installation

### Option 1: Windows Installer (Recommended)
Download the latest `MchoseHub-Setup-v*.exe` from the [Releases](https://github.com/mchose-offline/mchose-hub/releases) page. Follow the setup wizard to install with start menu and optional desktop shortcuts.

### Option 2: Portable Executable
Download `MchoseHub-Portable-v*.exe` from [Releases](https://github.com/mchose-offline/mchose-hub/releases). It is a self-contained, single-file executable that requires no installation. Just run it!

### Option 3: Offline WebHID Configurator (Browser Alternative)
If you prefer running via Chrome, Edge, Brave, or Opera without installing the native desktop app:
1. Open the [`web/`](web/) directory.
2. Double-click `run_offline_driver.bat` (or serve `web/index.html` on `http://localhost:8080/`).
3. Click **"Connect Mouse"** and select your MCHOSE device.

---

## Building from Source

### Prerequisites
- [Windows 10 / 11 64-bit](https://www.microsoft.com/windows)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optional: [Inno Setup 6](https://jrsoftware.org/isinfo.php) (only if compiling the installer)

### Build Steps

1. **Clone the repository:**
   ```bash
   git clone https://github.com/mchose-offline/mchose-hub.git
   cd mchose-hub
   ```

2. **Publish Self-Contained Native Binary:**
   ```powershell
   dotnet publish src/MchoseHub/MchoseHub.csproj `
     -c Release `
     -r win-x64 `
     --self-contained true `
     -p:PublishSingleFile=true `
     -p:EnableCompressionInSingleFile=true `
     -p:IncludeNativeLibrariesForSelfExtract=true `
     -o publish/portable
   ```

3. **(Optional) Build Inno Setup Installer:**
   ```powershell
   & "C:\Program Files (x86)\Inno Setup 6\iscc.exe" installer/mchose_setup.iss "/DMyAppVersion=1.0.0"
   ```

---

## Project Structure

```text
├── .github/
│   └── workflows/
│       └── build-and-release.yml   # Automated GitHub Actions CI/CD release workflow
├── installer/
│   ├── app_icon.ico                # High-res installer & application icon
│   └── mchose_setup.iss            # Inno Setup 6 compiler script
├── src/
│   └── MchoseHub/                  # .NET 8 WPF Native Application
│       ├── Assets/                 # Embedded mouse renders and vector icons
│       ├── Hardware/               # Native HID, RawInput hooks & MCHOSE protocol logic
│       ├── Models/                 # Mouse state & diagnostic data models
│       ├── MainWindow.xaml         # Main configurator UI
│       └── TesterWindow.xaml       # Hardware polling rate & switch latency tester
├── web/                            # Offline WebHID browser configurator alternative
│   ├── index.html                  # Standalone WebHID single-page hub
│   ├── server.js                   # Lightweight localhost WebHID server
│   └── run_offline_driver.bat      # One-click launcher for web tool
├── REVERSE_ENGINEERING_MCHOSE.md   # Complete reverse-engineered protocol documentation
├── LICENSE                         # MIT License
└── README.md                       # Project overview & documentation
```

---

## Automated CI/CD Releases

This repository includes a GitHub Actions workflow (`.github/workflows/build-and-release.yml`) that automatically:
- Checks out the repository and sets up .NET 8 SDK.
- Publishes single-file self-contained native `win-x64` binaries with LZMA compression.
- Installs Inno Setup and compiles the setup installer.
- Creates a new GitHub Release with attached portable and installer binaries on every tag push (`v*`) or manual workflow dispatch.

---

## Disclaimer

This is an independent open-source project created by the community. It is **not** affiliated with, authorized, or endorsed by Shenzhen ZhiHaihe Technology Co., Ltd. or MCHOSE. All registered trademarks and product names belong to their respective owners.

---

## License

This project is licensed under the [MIT License](LICENSE).
