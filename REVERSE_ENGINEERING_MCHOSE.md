# MCHOSE Mouse Reverse Engineering & Offline Configuration Guide

This guide documents the reverse-engineered USB HID protocol used by **MCHOSE** gaming mice (including the **A7 V2 Ultra / Pro**, **AX5**, **K7**, **M7**, **G3**, and **A5** series) to change **DPI**, **Polling Rate (Hz)**, **LOD**, **Debounce**, and read **Battery Percentage** without requiring the official web driver or internet access.

---

## 1. Two Protocol Architectures

MCHOSE mice fall into two distinct communication generations depending on model and firmware:

### A. Classic Feature Report Protocol (A7 / A7 V2 / AX5 / L7 / M7 Series)
- **Used by:** **MCHOSE A7 V2 Ultra / Pro**, A7, L7, M7, AX5.
- **Communication Type:** USB HID **Feature Reports** via `device.sendFeatureReport(reportId, buffer)` and `device.receiveFeatureReport(reportId)`.
- **Byte Inversion:** All data payload bytes sent or received are bitwise inverted with `XOR 0xFF` (`byte ^ 255`) and padded with `0xFF`.

#### 1. Polling Rate Command (`0x11 0x41`)
- **Report ID:** `0x11` (17 decimal), Total Buffer: 20 bytes.
- **Payload Bytes:** `[0x41, usbRate, freeRate]`
  - `0`: 125 Hz
  - `1`: 500 Hz
  - `2`: 1000 Hz
  - `3`: 2000 Hz
  - `4`: 4000 Hz
  - `5`: 8000 Hz
  - In 2.4G wireless mode: `usbRate = 0`, `freeRate = rateIndex`.
  - In wired USB mode: `usbRate = rateIndex`, `freeRate = 0`.

#### 2. Performance Modes & Sensor Tuning (`0x11 0x42`)
- **Report ID:** `0x11` (17 decimal), Total Buffer: 20 bytes.
- **Payload Bytes:** `[0x42, lod, ripple, line, motionSync, 0, 0, gameMode, rotateOpen, rotateVal]`
  - **`lod`:** `0` (0.7 mm), `1` (1.0 mm), `2` (2.0 mm).
  - **`ripple` (Ripple Control):** `1` = ON, `2` = OFF. High-speed jitter elimination.
  - **`line` (Angle Snapping):** `1` = ON, `2` = OFF. Linear horizontal/vertical path correction.
  - **`motionSync`:** `1` = ON, `2` = OFF. Sensor refresh cycle synchronization.
  - **`gameMode`:**
    - `1` = **Performance Mode** (Eco / Balanced sensor curve)
    - `2` = **Gaming Mode** (Standard gaming latency)
    - `3` = **Extreme Mode** (Maximum sensor responsiveness)
  - **`rotateOpen`:** Always `1` when committing sensor settings so the firmware applies and saves the rotation angle (including `0°`). If set to `0`, the mouse firmware treats the rotation angle as unchanged/unmodified and ignores `rotateVal`.
  - **`rotateVal`:** Sensor angle in degrees (-30° to +30°), two's complement.

#### 3. Key Debounce (`0x11 0x43`)
- **Report ID:** `0x11`, Total Buffer: 20 bytes.
- **Payload Bytes:** `[0x43, debounceMs]` (1 to 20 ms, default `8`).

#### 4. Sleep Timer (`0x11 0x0A`)
- **Report ID:** `0x11`, Total Buffer: 20 bytes.
- **Payload Bytes:** `[0x0A, sleepStatus, sleepMinutes]`
  - Auto sleep: `[0x0A, 1, minutes]` (1 to 60 min).
  - Never sleep: `[0x0A, 0, 0]`.

#### 5. Battery & Link Query (`0x11 0x06`)
- **Report ID:** `0x11`, Total Buffer: 20 bytes.
- **Payload Bytes:** `[0x06]`.
- **Response:**
  - `data[8]`: Battery percentage. *Note:* If raw value is `1`–`10`, it represents 10%–100% (multiply by 10). On newer firmware it reports direct percentage (`0`–`100`%).
  - `data[9]`: Charging status (`1` = Charging, `0` = Discharging).

#### 6. DPI Configuration (`0x12 0x40`)
- **Report ID:** `0x12` (18 decimal), Total Buffer: 64 bytes.
- **Payload Bytes:**
  - `data[0]`: `0x40` (command)
  - `data[1]`: `activeDpiIndex` (0 to 5)
  - `data[2]`: `activeDpiIndex`
  - `data[3]`: `0x00` (reserved)
  - `data[4..15]`: 6 DPI stage values as 16-bit little endian (50 to 42,000 DPI).
  - `data[16]`: Number of stages (`dpiSum`, 1 to 6).
  - `data[17]`: `0xFF` (`diff`, 255 for unified X/Y, or split).
  - `data[18..29]`: Y-axis DPI stage values (mirror of data[4..15]).

#### 7. Read All Settings (`0x12 0x67`)
- **Report ID:** `0x12`, Total Buffer: 64 bytes.
- **Payload Bytes:** `[0x67]`.
- Returns full onboard configuration: DPI stages, active stage, polling rate, debounce, sleep, sensor flags, and game mode.

---

### B. Protocol V2 (A5 V3 / K7 V2 Series)
- **Used by:** MCHOSE A5 V3, K7 V2, newer web driver releases.
- **Communication Type:** USB HID **Output Reports** via `device.sendReport(77, buffer)` and Input Reports (`oninputreport`).
- **Report ID:** `0x4D` (77 decimal, ASCII `'M'`).
- **Length:** 64 bytes (1 byte Report ID + 63 bytes payload).
- **Commands:** `0x0102` (Perf & Rate), `0x0103` (DPI), `0x0109` (LOD).

The offline driver ([`mchose_offline_hub.html`](file:///c:/Users/silvu/Documents/mchose%20drivers%20offline/mchose_offline_hub.html)) automatically detects the connected model and switches to the correct protocol!

---

## 2. Packet Architecture

All control commands sent to the mouse are 64-byte packets with the following structure:

| Byte Offset | Field | Value / Description |
|---|---|---|
| `0` | Report ID / Magic | `0x4D` (77 decimal, ASCII `'M'`) |
| `1` | Protocol Version | `0x01` |
| `2` | Flags | `0x01` |
| `3` | Data Payload Length | Number of bytes in data payload ($N$) |
| `4` | Command ID (LSB) | `cmd & 0xFF` |
| `5` | Command ID (MSB) | `(cmd >> 8) & 0xFF` |
| `6` | BizCode | `0x00` |
| `7` | Sequence Number | `0x00` |
| `8 .. (7 + N)` | Data Payload | Command-specific parameter bytes |
| `8 + N` | XOR Checksum | XOR reduction of bytes `2` through `(7 + N)` |
| `(9 + N) .. 63` | Padding | `0x00` zeroes |

### Checksum Algorithm:
```javascript
function calcChecksum(packet, start = 2, end = 7 + dataLength) {
  let chk = 0;
  for (let i = start; i <= end; i++) chk ^= packet[i];
  return chk;
}
```

---

## 3. Command Table & Register Layout

### 3.1. Polling Rate & Performance Register (`0x0102` SET / `0x0002` GET)

Command ID:
- **Read current:** `0x0002`
- **Write:** `0x0102`

#### Polling Rate Index Mapping:
| Rate Index | Polling Rate | Hardware Encoded Value |
|---|---|---|
| `0` | **125 Hz** | `0` |
| `1` | **500 Hz** | `2` |
| `2` | **1000 Hz** | `3` |
| `3` | **2000 Hz** | `4` |
| `4` | **4000 Hz** | `5` |
| `5` | **8000 Hz** | `6` |

*Note: In the protocol, hardware value = `rateIndex > 0 ? rateIndex + 1 : 0`.*

#### Payload Data Bytes for `0x0102`:
- `data[0]`: `profileIndex` (default `0`)
- `data[1]`: `(usbRateHw << 4) | (usbDpiIndex & 0x0F)`
- `data[2]`: `(wirelessRateHw << 4) | (usbDpiIndex & 0x0F)`
- `data[3]`: `sleepTimer` (minutes, e.g. `3`)
- `data[4]`: `sleepMode` (`0` = auto sleep, `1` = never)
- `data[5]`: `sensorMode` (`0` = Performance, `1` = Gaming, `2` = Extreme)
- `data[6]`: `sensorAngle` (`0` degrees)
- `data[7]`: `leftKeyDebounce` (milliseconds, e.g. `8`)
- `data[8]`: `rightKeyDebounce` (milliseconds, e.g. `8`)
- `data[9..18]`: Ten `0x00` padding bytes

---

### 3.2. DPI Table Configuration (`0x0103` SET / `0x0003` GET)

Command ID:
- **Read stages:** `0x0003`
- **Write all stages:** `0x0103`

#### Payload Data Bytes for `0x0103`:
- `data[0]`: `profileIndex` (`0`)
- `data[1]`: `dpiDirection` (`0` for synchronous X/Y, `1` for Y only)
- `data[2]`: `dpiHasY` (`0` = standard, `1` = split X/Y)
- `data[3]`: `dpiSum` (number of active stages minus 1, usually `5` for 6 stages)
- `data[4]`: `activeDpiIndex` (`0` for Stage 1, `1` for Stage 2, ..., `5` for Stage 6)
- `data[5..16]`: 6 DPI stage values as **16-bit little-endian** integers:
  - Stage 1: `data[5] = LSB`, `data[6] = MSB`
  - Stage 2: `data[7] = LSB`, `data[8] = MSB`
  - Stage 3: `data[9] = LSB`, `data[10] = MSB`
  - Stage 4: `data[11] = LSB`, `data[12] = MSB`
  - Stage 5: `data[13] = LSB`, `data[14] = MSB`
  - Stage 6: `data[15] = LSB`, `data[16] = MSB`

*Example:* To set Stage 1 to 800 DPI (`0x0320`), bytes are `0x20, 0x03`.

---

### 3.3. Lift-Off Distance (LOD) (`0x0109` SET / `0x0009` GET)

Command ID:
- **Read:** `0x0009`
- **Write:** `0x0109`

#### Payload Data Bytes:
- `data[0]`: `profileIndex` (`0`)
- `data[1]`: `lodIndex`:
  - `0` = **0.7 mm**
  - `1` = **1.0 mm** (Default)
  - `2` = **2.0 mm**

---

### 3.4. Device Info & Battery Query (`0x0900` GET)

Command ID: `0x0900` (Empty data payload).

Mouse response payload bytes:
- `payload[0..1]`: Vendor ID (Little Endian)
- `payload[2..3]`: Product ID (Little Endian)
- `payload[10]`: Connection Status (`1` = Wired, `2` = 2.4G Dongle, `3` = Bluetooth)
- `payload[11]`: Charging Status (`0` = Normal, `1` = Charging, `2` = Fully Charged)
- `payload[12]`: Battery Level (`0` - `100`%)

---

## 4. How to Use the Included Offline Configurator

We have created an offline web tool: [`mchose_offline_hub.html`](file:///c:/Users/silvu/Documents/mchose%20drivers%20offline/mchose_offline_hub.html).

### Steps:
1. Double-click [`run_offline_driver.bat`](file:///c:/Users/silvu/Documents/mchose%20drivers%20offline/run_offline_driver.bat).
2. It starts a lightweight local server at `http://localhost:8080/` and opens your browser (Chrome, Brave, Edge, or Opera).
3. Click **"Connect Mouse"**.
4. In the browser popup, select your **MCHOSE Dongle or Mouse** and click **Connect**.
5. You can now:
   - Click any Polling Rate button (**125Hz, 500Hz, 1000Hz, 2000Hz, 4000Hz, 8000Hz**) and click **Apply**.
   - Select your active DPI stage or adjust the DPI sliders.
   - Adjust LOD (0.7mm, 1mm, 2mm) or Key Debounce time.
   - View every raw packet in the live **HID Packet Monitor**.
