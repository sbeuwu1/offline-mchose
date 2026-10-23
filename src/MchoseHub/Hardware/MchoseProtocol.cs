using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MchoseHub.Models;

namespace MchoseHub.Hardware
{
    public class MchoseProtocol : IDisposable
    {
        private readonly NativeHid _hid;
        private readonly MouseState _state;

        // Command queue for flash write safety and RF packet stabilization
        private readonly List<CommandTask> _queue = new();
        private readonly object _queueLock = new();
        private bool _isProcessingQueue;
        private readonly CancellationTokenSource _cts = new();
        private readonly CancellationToken _token;

        // The CTS is never disposed: the worker can still race a Dispose() call and would
        // otherwise hit ObjectDisposedException on _cts.Token. A CTS without timers owns no
        // unmanaged resources, so leaving it alive is the safer trade-off.
        private volatile bool _disposed;

        public event Action<string, string>? LogMessage;
        public event Action<string>? StatusChanged;

        private void Log(string msg, string type = "Info") => LogMessage?.Invoke(msg, type);

        public MchoseProtocol(NativeHid hid, MouseState state)
        {
            _hid = hid;
            _state = state;
            _token = _cts.Token;

            _hid.InputReportReceived += OnInputReportReceived;
            _hid.LogMessage += (msg, type) => Log(msg, type);
        }

        public bool IsDisposed => _disposed;

        /// <summary>
        /// Number of commands waiting in the queue plus the one currently being executed.
        /// </summary>
        public int PendingCount
        {
            get
            {
                lock (_queueLock)
                {
                    return _queue.Count + (_isProcessingQueue ? 1 : 0);
                }
            }
        }

        private class CommandTask
        {
            public string Tag { get; set; } = string.Empty;
            public Func<Task> Action { get; set; } = null!;
            public TaskCompletionSource<bool> Tcs { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private Task EnqueueCommand(Func<Task> action, string tag = "")
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            var task = new CommandTask { Tag = tag, Action = action };

            lock (_queueLock)
            {
                // Coalesce rapid slider/toggle updates with the same tag
                if (!string.IsNullOrEmpty(tag))
                {
                    int existingIdx = _queue.FindIndex(t => t.Tag == tag);
                    if (existingIdx != -1)
                    {
                        _queue[existingIdx].Tcs.TrySetResult(false);
                        _queue.RemoveAt(existingIdx);
                    }
                }

                _queue.Add(task);
            }

            _ = ProcessQueueAsync();
            return task.Tcs.Task;
        }

        private async Task ProcessQueueAsync()
        {
            lock (_queueLock)
            {
                if (_isProcessingQueue || _queue.Count == 0) return;
                _isProcessingQueue = true;
            }

            try
            {
                while (true)
                {
                    CommandTask? current;
                    lock (_queueLock)
                    {
                        if (_queue.Count == 0)
                        {
                            _isProcessingQueue = false;
                            break;
                        }
                        current = _queue[0];
                        _queue.RemoveAt(0);
                    }

                    if (!string.IsNullOrEmpty(current.Tag) && !current.Tag.StartsWith("read", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusChanged?.Invoke("Syncing...");
                    }

                    try
                    {
                        await current.Action().ConfigureAwait(false);
                        current.Tcs.TrySetResult(true);
                    }
                    catch (OperationCanceledException)
                    {
                        // Shutdown in progress: don't report this as a device error.
                        current.Tcs.TrySetCanceled();
                    }
                    catch (Exception ex)
                    {
                        Log($"Command error [{current.Tag}]: {ex.Message}", "Error");
                        current.Tcs.TrySetException(ex);
                    }

                    try
                    {
                        // 75ms guard delay: prevents MCU flash write stalls and RF pipeline stutter
                        await Task.Delay(75, _token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                bool restart;
                lock (_queueLock)
                {
                    _isProcessingQueue = false;
                    restart = !_disposed && _queue.Count > 0;
                }

                if (_disposed)
                {
                    DrainQueue();
                }
                else if (restart)
                {
                    _ = ProcessQueueAsync();
                }
                else
                {
                    StatusChanged?.Invoke(_state.ProductName);
                }
            }
        }

        /// <summary>
        /// Completes every queued command as canceled so callers awaiting them never hang.
        /// </summary>
        private void DrainQueue()
        {
            List<CommandTask> remaining;
            lock (_queueLock)
            {
                remaining = new List<CommandTask>(_queue);
                _queue.Clear();
            }

            foreach (var pending in remaining)
            {
                pending.Tcs.TrySetCanceled();
            }
        }

        /// <summary>
        /// Waits until the command queue is empty (used on shutdown so the last change still
        /// reaches the mouse's onboard flash). Returns false if the timeout expired first.
        /// </summary>
        public async Task<bool> FlushAsync(TimeSpan timeout)
        {
            var elapsed = Stopwatch.StartNew();
            while (!_disposed && PendingCount > 0 && elapsed.Elapsed < timeout)
            {
                await Task.Delay(25).ConfigureAwait(false);
            }
            return PendingCount == 0;
        }

        private static byte[] Invert(byte[] data)
        {
            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ 0xFF);
            }
            return result;
        }

        private async Task SendClassicFeatureAsync(byte reportId, byte[] rawBytes, int totalLength, string tag = "")
        {
            await EnqueueCommand(() =>
            {
                if (!_hid.IsConnected) throw new InvalidOperationException("Device not connected");

                byte[] inverted = Invert(rawBytes);
                byte[] payload = new byte[totalLength];
                Array.Fill<byte>(payload, 0xFF);
                Array.Copy(inverted, 0, payload, 0, Math.Min(inverted.Length, totalLength));

                bool ok = _hid.SendFeatureReport(reportId, payload);
                if (!ok)
                {
                    Log($"SendFeatureReport 0x{reportId:X2} could not be delivered to mouse.", "Warn");
                }

                return Task.CompletedTask;
            }, tag);
        }

        private async Task<byte[]> ReadClassicFeatureAsync(byte reportId, byte commandByte, string tag = "readFeature")
        {
            byte[]? result = null;

            await EnqueueCommand(async () =>
            {
                if (!_hid.IsConnected)
                {
                    Log("Cannot read feature report: device disconnected.", "Warn");
                    return;
                }

                int totalLength = (reportId == 0x11) ? 20 : 64;
                byte[] queryPayload = new byte[totalLength];
                Array.Fill<byte>(queryPayload, 0xFF);
                queryPayload[0] = (byte)(commandByte ^ 0xFF);

                // Retry loop: 2.4G dongles may take 1-3 RF cycles to fetch data from the wireless mouse
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    bool sendOk = _hid.SendFeatureReport(reportId, queryPayload);
                    if (!sendOk)
                    {
                        Log($"Query for report 0x{reportId:X2} cmd 0x{commandByte:X2} failed (attempt {attempt + 1}).", "Warn");
                        await Task.Delay(50, _token);
                        continue;
                    }

                    await Task.Delay(80, _token);

                    byte[]? raw = _hid.GetFeatureReport(reportId, totalLength);
                    if (raw != null && raw.Length > 2)
                    {
                        byte[] inverted = Invert(raw);
                        // Check if payload contains real data (not all 0x00 / all 0xFF before inversion)
                        bool hasData = false;
                        for (int i = 1; i < Math.Min(inverted.Length, 12); i++)
                        {
                            if (inverted[i] != 0)
                            {
                                hasData = true;
                                break;
                            }
                        }

                        if (hasData || attempt == 4)
                        {
                            result = inverted;
                            break;
                        }
                    }

                    await Task.Delay(60, _token);
                }
            }, tag);

            return result ?? Array.Empty<byte>();
        }

        private void OnInputReportReceived(byte[] raw)
        {
            byte[] unInverted = Invert(raw);
            if (unInverted.Length >= 6 && unInverted[0] == 0xE2)
            {
                int charge = unInverted[3];
                int battery = unInverted[4];

                if (battery > 0 && battery <= 10)
                {
                    battery *= 10;
                }

                if (battery >= 0 && battery <= 100)
                {
                    _state.BatteryLevel = battery;
                    _state.IsCharging = (charge == 1);
                    Log($"Live Battery Update: {battery}% (Charging: {_state.IsCharging})", "Rx");
                }

                if (unInverted.Length >= 6)
                {
                    int dpiIdx = unInverted[5];
                    if (dpiIdx >= 0 && dpiIdx < _state.NumLevels && dpiIdx != _state.ActiveDpiIndex)
                    {
                        _state.ActiveDpiIndex = dpiIdx;
                        Log($"Live DPI switch detected from mouse: Stage {dpiIdx + 1} ({_state.ActiveDpiValue} DPI)", "Rx");
                    }
                }
            }
        }

        public async Task ReadAllSettingsAsync()
        {
            if (_disposed) return;

            try
            {
                Log("Querying mouse onboard settings (Report 0x12 0x67)...", "Info");
                byte[] raw = await ReadClassicFeatureAsync(0x12, 0x67, "readAllSettings");
                if (raw.Length < 20)
                {
                    Log("Onboard settings response too short.", "Warn");
                    return;
                }

                // Align data so data[0] is profileIndex (the start of the Zat schema)
                // NativeHid already strips the ReportId byte (0x12).
                // raw[0] is the command echo byte 0x67.
                byte[] data = raw;
                if (data.Length > 2 && data[0] == 0x12 && data[1] == 0x67)
                {
                    data = data.Skip(2).ToArray();
                }
                else if (data.Length > 1 && (data[0] == 0x67 || data[0] == 0x12))
                {
                    data = data.Skip(1).ToArray();
                }

                if (data.Length >= 20)
                {
                    byte byte1 = data[1]; // gDpiIndex (low 4) & gRateIndex (high 4)
                    byte byte2 = data[2]; // usbDpiIndex (low 4) & usbRateIndex (high 4)

                    int gDpi = byte1 & 0x0F;
                    int gRate = (byte1 >> 4) & 0x0F;
                    int usbDpi = byte2 & 0x0F;
                    int usbRate = (byte2 >> 4) & 0x0F;

                    // Unified Polling Rate detection:
                    // If usbRate and gRate both have valid indices (0..5):
                    int activeRate = _state.RateIndex;
                    if (usbRate >= 0 && usbRate <= 5 && gRate >= 0 && gRate <= 5)
                    {
                        if (usbRate == gRate)
                        {
                            activeRate = usbRate;
                        }
                        else if (usbRate == _state.RateIndex)
                        {
                            activeRate = usbRate;
                        }
                        else if (gRate == _state.RateIndex)
                        {
                            activeRate = gRate;
                        }
                        else
                        {
                            activeRate = (usbRate > 0) ? usbRate : gRate;
                        }
                    }
                    else if (usbRate >= 0 && usbRate <= 5)
                    {
                        activeRate = usbRate;
                    }
                    else if (gRate >= 0 && gRate <= 5)
                    {
                        activeRate = gRate;
                    }

                    int activeDpi = (gDpi >= 0 && gDpi <= 5) ? gDpi : ((usbDpi >= 0 && usbDpi <= 5) ? usbDpi : 0);

                    // DPI Stages (data[4..15] -> 6 stages of uint16 little-endian)
                    var stages = new List<int>();
                    for (int i = 0; i < 6; i++)
                    {
                        int val = data[4 + i * 2] | (data[4 + i * 2 + 1] << 8);
                        if (val >= 50 && val <= 42000)
                        {
                            stages.Add(val);
                        }
                        else
                        {
                            stages.Add((i < _state.DpiStages.Length) ? _state.DpiStages[i] : 800);
                        }
                    }
                    _state.DpiStages = stages.ToArray();

                    // DPI Level count: hardware reports actual stage count (1 to 6)
                    int dpiSum = data[16];
                    if (dpiSum >= 1 && dpiSum <= 6)
                    {
                        _state.NumLevels = dpiSum;
                    }
                    else if (dpiSum == 0)
                    {
                        _state.NumLevels = 1;
                    }

                    if (activeDpi >= 0 && activeDpi < _state.NumLevels)
                    {
                        _state.ActiveDpiIndex = activeDpi;
                    }
                    else if (_state.ActiveDpiIndex >= _state.NumLevels)
                    {
                        _state.ActiveDpiIndex = _state.NumLevels - 1;
                    }

                    if (activeRate >= 0 && activeRate <= 5)
                    {
                        _state.RateIndex = activeRate;
                    }

                    // Sensor features (Byte 17)
                    byte sensorByte = data[17];
                    int bit0 = (sensorByte >> 0) & 1;
                    int bit1 = (sensorByte >> 1) & 1;
                    int bit2 = (sensorByte >> 2) & 1;
                    int bit3 = (sensorByte >> 3) & 1;
                    int bit4 = (sensorByte >> 4) & 1;
                    int bit6 = (sensorByte >> 6) & 1;
                    int bit7 = (sensorByte >> 7) & 1;

                    // LOD: 00 = 0.7mm, 10 = 1.0mm, 01 = 2.0mm
                    int lod = 0;
                    if (bit0 == 0 && bit1 == 0) lod = 0;
                    else if (bit0 == 1 && bit1 == 0) lod = 1;
                    else if (bit0 == 0 && bit1 == 1) lod = 2;
                    _state.LodIndex = lod;

                    _state.Ripple = (bit2 == 1);
                    _state.AngleSnap = (bit3 == 1);
                    _state.MotionSync = (bit4 == 1);

                    // Game Mode:
                    // bit 7 == 0 -> Mode 1 (Performance / Eco)
                    // bit 7 == 1 && bit 6 == 0 -> Mode 2 (Gaming)
                    // bit 7 == 1 && bit 6 == 1 -> Mode 3 (Extreme)
                    int mode = 1;
                    if (bit7 == 0) mode = 1;
                    else if (bit7 == 1 && bit6 == 0) mode = 2;
                    else if (bit7 == 1 && bit6 == 1) mode = 3;
                    _state.GameMode = mode;

                    // Debounce (Byte 18)
                    byte debounce = data[18];
                    if (debounce >= 1 && debounce <= 20)
                    {
                        _state.DebounceMs = debounce;
                    }

                    // Sleep (Byte 19)
                    byte sleep = data[19];
                    if (sleep <= 60)
                    {
                        _state.SleepMinutes = sleep == 0 ? 3 : sleep;
                        _state.NeverSleep = (sleep == 0);
                    }

                    // Rotation Angle (Byte 49 in Zat schema)
                    if (data.Length > 49)
                    {
                        byte rawRot = data[49];
                        sbyte signedRot = (sbyte)rawRot;
                        int rot = (int)signedRot;
                        _state.RotateVal = (rot >= -30 && rot <= 30) ? rot : 0;
                    }

                    Log($"Onboard settings loaded! Rate: {_state.ActiveHz}Hz, Mode: {_state.GameMode}, Active Stage: {_state.ActiveDpiIndex + 1} ({_state.ActiveDpiValue} DPI), Sensor Angle: {_state.RotateVal}°, Stages: [{string.Join(", ", _state.DpiStages.Take(_state.NumLevels))}]", "Info");
                }
            }
            catch (Exception ex)
            {
                Log($"Error reading onboard settings: {ex.Message}", "Warn");
            }
        }

        public async Task ReadBatteryAsync()
        {
            if (_disposed) return;

            try
            {
                Log("Querying battery status (Report 0x11 0x06)...", "Info");
                byte[] raw = await ReadClassicFeatureAsync(0x11, 0x06, "readBattery");
                byte[] data = (raw.Length > 2) ? raw.Skip(2).ToArray() : raw;
                if (data.Length >= 10)
                {
                    int battery = data[8];
                    int charge = data[9];
                    if (battery > 0 && battery <= 10) battery *= 10;

                    if (battery >= 0 && battery <= 100)
                    {
                        _state.BatteryLevel = battery;
                        _state.IsCharging = (charge == 1);
                        Log($"Battery: {battery}%, Charging: {_state.IsCharging}", "Info");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Battery read note: {ex.Message}", "Warn");
            }
        }

        public async Task ApplyPollingRateAsync(int rateIndex)
        {
            if (_disposed) return;

            _state.RateIndex = rateIndex;
            if (!_hid.IsConnected) return;

            byte r = (byte)rateIndex;
            Log($"Setting Polling Rate to {_state.ActiveHz}Hz...", "Info");
            // Set BOTH usbRate and freeRate so the mouse applies the rate whether on USB wire or 2.4G dongle
            byte[] payload = { 0x41, r, r };
            await SendClassicFeatureAsync(0x11, payload, 20, "rate");
            Log($"SUCCESS: Polling rate set to {_state.ActiveHz}Hz!", "Info");
        }

        public async Task ApplySensorAndModeAsync()
        {
            if (_disposed || !_hid.IsConnected) return;

            byte lod = (byte)_state.LodIndex;
            byte ripple = (byte)(_state.Ripple ? 1 : 2);
            byte line = (byte)(_state.AngleSnap ? 1 : 2);
            byte motionSync = (byte)(_state.MotionSync ? 1 : 2);
            byte gameMode = (byte)_state.GameMode;

            // MANDATORY RULE: rotateOpen MUST ALWAYS BE 1 so the mouse firmware commits the rotation angle to flash
            byte rotateOpen = 1;
            byte rot = (byte)(_state.RotateVal < 0 ? (_state.RotateVal + 256) : _state.RotateVal);

            Log($"Applying Sensor Settings: Mode {gameMode}, LOD {_state.LodIndex}, Ripple: {_state.Ripple}, MotionSync: {_state.MotionSync}, Angle: {_state.RotateVal}°...", "Info");
            byte[] payload = { 0x42, lod, ripple, line, motionSync, 0, 0, gameMode, rotateOpen, rot };
            await SendClassicFeatureAsync(0x11, payload, 20, "sensorMode");
            Log($"SUCCESS: Sensor and performance mode updated (Angle: {_state.RotateVal}°)", "Info");
        }

        public async Task ApplyDpiAsync()
        {
            if (_disposed || !_hid.IsConnected) return;

            int activeIdx = Math.Min(_state.ActiveDpiIndex, _state.NumLevels - 1);
            Log($"Writing DPI stages: {string.Join(", ", _state.DpiStages.Take(_state.NumLevels))} (Active: Stage {activeIdx + 1})...", "Info");

            var payload = new List<byte>
            {
                0x40,
                (byte)activeIdx,
                (byte)activeIdx,
                0x00 // reserved
            };

            for (int i = 0; i < 6; i++)
            {
                int val = (i < _state.DpiStages.Length) ? _state.DpiStages[i] : 800;
                payload.Add((byte)(val & 0xFF));
                payload.Add((byte)((val >> 8) & 0xFF));
            }

            payload.Add((byte)_state.NumLevels); // sum: actual stage count (1 to 6)
            payload.Add(0xFF);                   // diff: 0xFF (255) for standard unified X/Y

            for (int i = 0; i < 6; i++)
            {
                int val = (i < _state.DpiStages.Length) ? _state.DpiStages[i] : 800;
                payload.Add((byte)(val & 0xFF));
                payload.Add((byte)((val >> 8) & 0xFF));
            }

            await SendClassicFeatureAsync(0x12, payload.ToArray(), 64, "dpi");
            Log("SUCCESS: DPI configuration saved to onboard memory!", "Info");
        }

        public async Task ApplyDebounceAsync(int ms)
        {
            if (_disposed) return;

            _state.DebounceMs = ms;
            if (!_hid.IsConnected) return;

            Log($"Setting Key Debounce to {ms}ms...", "Info");
            byte[] payload = { 0x43, (byte)ms };
            await SendClassicFeatureAsync(0x11, payload, 20, "debounce");
            Log($"SUCCESS: Debounce updated to {ms}ms!", "Info");
        }

        public async Task ApplySleepAsync(int minutes, bool never)
        {
            if (_disposed) return;

            _state.SleepMinutes = minutes;
            _state.NeverSleep = never;
            if (!_hid.IsConnected) return;

            byte sleepStatus = (byte)(never ? 0 : 1);
            byte sleepMin = (byte)(never ? 0 : minutes);

            Log($"Setting Sleep Timeout to {(never ? "Never" : minutes + "m")}...", "Info");
            byte[] payload = { 0x0A, sleepStatus, sleepMin };
            await SendClassicFeatureAsync(0x11, payload, 20, "sleep");
            Log("SUCCESS: Sleep timer updated!", "Info");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop feeding device events into a window that is going away.
            _hid.InputReportReceived -= OnInputReportReceived;

            try { _cts.Cancel(); } catch { }

            // Release anyone still awaiting a queued command; otherwise closing the window
            // leaves dangling continuations that never complete.
            DrainQueue();
        }
    }
}
