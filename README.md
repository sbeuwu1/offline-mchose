# Offline Mchose

[![Build and Release](https://github.com/Rapou7/mchose-drivers-offline/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/Rapou7/mchose-drivers-offline/actions/workflows/build-and-release.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-blue.svg)](https://www.microsoft.com/windows)

A lightweight, native Windows offline driver and hardware configurator for MCHOSE gaming mice.

> [!IMPORTANT]
> **LEGAL & NON-AFFILIATION DISCLAIMER:**  
> **Offline Mchose** is an independent, unofficial community project created by **rapou7**. It is **NOT** affiliated with, authorized, maintained, sponsored, or endorsed by **Shenzhen ZhiHaihe Technology Co., Ltd. (深圳市智海和科技有限公司)**, **MCHOSE**, or any of their affiliates or subsidiaries.  
> 
> All product names, logos, brands, trademarks, and registered trademarks belong to their respective holders. Use of them does not imply any affiliation with or endorsement by them. This utility is provided strictly "as-is" without warranty.

---

## Why Offline Mchose?

Official MCHOSE configuration tools rely on browser-based web hubs that require an active internet connection, remote scripts, and external services.

Offline Mchose communicates directly with your mouse and 2.4G wireless dongle via native Windows HID APIs:

- **100% Offline:** Operates with no internet connection, no background telemetry, and no external dependencies.
- **Onboard Flash Memory:** All settings are written directly to the mouse's onboard hardware memory and persist across computers without software running.
- **Lightweight & Fast:** Starts instantly and uses minimal system memory compared to browser tabs or Electron apps.

---

## Supported Mice

| Model | Sensor | Status |
|---|---|---|
| MCHOSE A7 V2 Ultra / Pro | PAW3950 | Supported |
| MCHOSE A7 / A7 Pro | PAW3395 | Supported |
| MCHOSE AX5 / AX5 Pro / Max | PAW3395 | Supported |
| MCHOSE L7 / M7 Series | PAW3395 | Supported |
| MCHOSE A5 / A5 Pro / A5 Max | PAW3395 | Supported |
| MCHOSE K7 Series | PAW3395 | Supported |

---

## Features

- **Polling Rates:** Configurable from 125 Hz up to 8000 Hz in wired and 2.4G wireless modes.
- **DPI Stages:** Up to 6 custom stages from 50 to 42,000 DPI with independent active level selection.
- **Sensor Calibration:** Lift-off distance (0.7 mm, 1.0 mm, 2.0 mm), sensor angle rotation (-30° to +30°), ripple control, motion sync, and angle snapping.
- **Power & Switches:** Real-time battery indicator, adjustable auto-sleep timer, and key debounce adjustment (1 to 20 ms).
- **Hardware Tester (F8):** Real-time RawInput polling rate counter, GPU-accelerated motion trail, click latency measurement, and switch debounce tester.

---

## Protocol Documentation

Detailed reverse-engineered HID packet formats, register maps, and checksum specifications are documented in [REVERSE_ENGINEERING_MCHOSE.md](REVERSE_ENGINEERING_MCHOSE.md).

---

## Download & Installation

### Option 1: Windows Installer (Recommended)
Download the latest `MchoseHub-Setup-v*.exe` from the [Releases](https://github.com/Rapou7/mchose-drivers-offline/releases) page.

### Option 2: Portable Executable
Download `MchoseHub-Portable-v*.exe` from [Releases](https://github.com/Rapou7/mchose-drivers-offline/releases). Single-file executable that requires no installation.

### Option 3: Offline WebHID Configurator
If you prefer running via a Chromium-based browser (Chrome, Edge, Brave, Opera):
1. Open the [`web/`](web/) folder.
2. Launch `run_offline_driver.bat` and open `http://localhost:8080/`.
3. Click **"Connect Mouse"** to pair via WebHID.

---

## Building from Source

### Prerequisites
- Windows 10 / 11 64-bit
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optional: [Inno Setup 6](https://jrsoftware.org/isinfo.php) (for compiling the installer)

### Build Steps

1. **Clone the repository:**
   ```bash
   git clone https://github.com/Rapou7/mchose-drivers-offline.git
   cd mchose-drivers-offline
   ```

2. **Publish Portable Executable:**
   ```powershell
   dotnet publish src/MchoseHub/MchoseHub.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/portable
   ```

3. **(Optional) Build Installer:**
   ```powershell
   & "C:\Program Files (x86)\Inno Setup 6\iscc.exe" installer/mchose_setup.iss "/DMyAppVersion=1.0.0"
   ```

---

## Credits

* **rapou7** — Reverse engineering and development.

---

## Legal Disclaimer & Limitation of Liability

### 1. Unofficial Community Project (Non-Affiliation)
**Offline Mchose** is an independent open-source tool created by **rapou7**. It is **NOT** an official product of **Shenzhen ZhiHaihe Technology Co., Ltd. (深圳市智海和科技有限公司)** or **MCHOSE**, nor is it endorsed, supported, or certified by them.

### 2. Intellectual Property & Trademarks
"MCHOSE", "迈从", and related product names are trademarks of their respective owners. Their use here is solely for compatibility identification and interoperability under fair use.

### 3. Warranty Disclaimer ("AS IS")
THIS SOFTWARE IS PROVIDED BY THE AUTHOR (**rapou7**) AND CONTRIBUTORS "AS IS", AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE, ARE DISCLAIMED.

### 4. Limitation of Liability
IN NO EVENT SHALL THE AUTHOR OR CONTRIBUTORS BE LIABLE FOR ANY DAMAGES (INCLUDING DIRECT, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL DAMAGES, LOSS OF DATA, DEVICE DAMAGE, OR VOIDED WARRANTIES) ARISING FROM THE USE OF THIS SOFTWARE. USERS ASSUME ALL RESPONSIBILITY.

### 5. Interoperability & Clean-Room Research
The protocol implementation was developed through local observation of standard USB HID feature reports to enable offline device configuration.

---

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE) (GPLv3).
