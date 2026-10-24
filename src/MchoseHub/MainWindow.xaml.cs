using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MchoseHub.Hardware;
using MchoseHub.Localization;
using MchoseHub.Models;

namespace MchoseHub
{
    public partial class MainWindow : Window
    {
        private readonly MouseState _state = new();
        private readonly NativeHid _hid = new();
        private readonly MchoseProtocol _protocol;
        private readonly ObservableCollection<LogEntry> _logEntries = new();

        private readonly DispatcherTimer _sensorDebounceTimer;
        private readonly DispatcherTimer _dpiDebounceTimer;
        private readonly DispatcherTimer _debounceTimer;
        private readonly DispatcherTimer _sleepDebounceTimer;
        private readonly DispatcherTimer _batteryRefreshTimer;
        private readonly DispatcherTimer _deviceChangeTimer;
        private readonly DispatcherTimer _presenceWatchdogTimer;

        private HwndSource? _hwndSource;
        private bool _isConnecting;
        private bool _autoConnectSuspended;
        private bool _lastPresenceProbe;
        private bool _lastConnectedProbe;
        private DateTime _lastLinkRebuildUtc = DateTime.MinValue;
        private DateTime _lastLinkProbeUtc = DateTime.MinValue;
        private DateTime _lastConnectFailureUtc = DateTime.MinValue;

        private bool _isUpdatingUI = true;
        private bool _isInitialized;
        private bool _shutdownStarted;
        private bool _shutdownCompleted;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVNODES_CHANGED = 0x0007;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        public MainWindow()
        {
            _protocol = new MchoseProtocol(_hid, _state);
            _protocol.LogMessage += OnLogMessage;
            _protocol.StatusChanged += OnStatusChanged;

            _sensorDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _sensorDebounceTimer.Tick += async (s, e) =>
            {
                _sensorDebounceTimer.Stop();
                await SafeApplyAsync(_protocol.ApplySensorAndModeAsync);
            };

            _dpiDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _dpiDebounceTimer.Tick += async (s, e) =>
            {
                _dpiDebounceTimer.Stop();
                await SafeApplyAsync(_protocol.ApplyDpiAsync);
            };

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _debounceTimer.Tick += async (s, e) =>
            {
                _debounceTimer.Stop();
                await SafeApplyAsync(() => _protocol.ApplyDebounceAsync(_state.DebounceMs));
            };

            _sleepDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _sleepDebounceTimer.Tick += async (s, e) =>
            {
                _sleepDebounceTimer.Stop();
                await SafeApplyAsync(() => _protocol.ApplySleepAsync(_state.SleepMinutes, _state.NeverSleep));
            };

            // Battery drifts while the mouse is idle and live input reports are not guaranteed,
            // so poll it periodically.
            _batteryRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _batteryRefreshTimer.Tick += async (s, e) =>
            {
                if (_shutdownStarted || !_hid.IsConnected) return;
                await SafeApplyAsync(_protocol.ReadBatteryAsync);
            };

            // WM_DEVICECHANGE storms on every USB enumeration; coalesce them into one probe.
            _deviceChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _deviceChangeTimer.Tick += async (s, e) =>
            {
                _deviceChangeTimer.Stop();
                await SafeApplyAsync(RefreshDevicePresenceAsync);
            };

            // Safety net: WM_DEVICECHANGE is best-effort, so also reconcile the real device list
            // periodically. Enumeration is cheap (a few ms) and this keeps the UI honest.
            _presenceWatchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _presenceWatchdogTimer.Tick += async (s, e) =>
            {
                await SafeApplyAsync(RefreshDevicePresenceAsync);
            };
            _presenceWatchdogTimer.Start();

            _hid.DeviceRemoved += OnHidDeviceRemoved;
            _state.PropertyChanged += OnStatePropertyChanged;

            InitializeComponent();

            logItemsControl.ItemsSource = _logEntries;
            _isInitialized = true;
            _isUpdatingUI = false;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            ApplyWindowStyle(hwnd);

            // Listen for device arrival/removal so the UI tracks cable and dongle changes.
            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                int evt = wParam.ToInt32();
                if (evt == DBT_DEVNODES_CHANGED || evt == DBT_DEVICEARRIVAL || evt == DBT_DEVICEREMOVECOMPLETE)
                {
                    App.DebugLog($"WM_DEVICECHANGE: 0x{evt:X4}");

                    // Topology changed: a manual disconnect pause no longer applies.
                    _autoConnectSuspended = false;

                    _deviceChangeTimer.Stop();
                    _deviceChangeTimer.Start();
                }
            }

            return IntPtr.Zero;
        }

        private void ApplyWindowStyle(IntPtr hwnd)
        {
            try
            {
                int trueVal = 1;
                // Enable immersive dark mode
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueVal, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref trueVal, sizeof(int));

                // Seamless purple navbar caption color (BGR: 0x001F080F for #0F081F)
                int captionColor = 0x001F080F;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

                // White titlebar text
                int textColor = 0x00FFFFFF;
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
            }
            catch
            {
                // Fallback gracefully on systems without DWM caption color support
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            App.DebugLog("MainWindow_Loaded: start");
            Log("MCHOSE Offline Device Hub initialized. Direct OS HID active.", LogType.Info);
            RenderDpiStages();
            UpdateUIFromState();

            App.DebugLog("MainWindow_Loaded: UI rendered, calling TryConnectAsync...");
            // Auto-connect on launch if device is plugged in without blocking UI
            await TryConnectAsync();
            App.DebugLog("MainWindow_Loaded: completed.");
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_shutdownCompleted)
            {
                return;
            }

            // Keep the window alive until pending HID writes reach the mouse, then close for real.
            e.Cancel = true;

            if (_shutdownStarted)
            {
                return;
            }

            _shutdownStarted = true;
            StopAllTimers();
            App.DebugLog("MainWindow_Closing: flushing pending HID commands...");

            _ = FinishShutdownAsync();
        }

        private async Task FinishShutdownAsync()
        {
            try
            {
                bool flushed = await _protocol.FlushAsync(TimeSpan.FromSeconds(3));
                App.DebugLog(flushed
                    ? "MainWindow_Closing: command queue drained."
                    : "MainWindow_Closing: command queue did not drain within 3s.");
            }
            catch (Exception ex)
            {
                App.DebugLog($"MainWindow_Closing: flush failed: {ex.Message}");
            }

            App.DebugLog("MainWindow_Closing: disposing resources...");
            _hid.DeviceRemoved -= OnHidDeviceRemoved;
            _state.PropertyChanged -= OnStatePropertyChanged;

            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }

            _protocol.Dispose();
            _hid.Dispose();
            App.FlushLog();

            _shutdownCompleted = true;
            // Closing re-entrantly from inside the Closing handler is not allowed; defer it.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }

        private void StopAllTimers()
        {
            _sensorDebounceTimer.Stop();
            _dpiDebounceTimer.Stop();
            _debounceTimer.Stop();
            _sleepDebounceTimer.Stop();
            _batteryRefreshTimer.Stop();
            _deviceChangeTimer.Stop();
            _presenceWatchdogTimer.Stop();
        }

        private static async Task SafeApplyAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                // Never let a cancelled/failed background write surface as an error dialog.
                App.DebugLog($"Background apply failed: {ex.Message}");
            }
        }

        private void OnLogMessage(string message, string typeStr)
        {
            App.DebugLog($"[{typeStr}] {message}");

            // HID callbacks come from a background reader thread and must never block on the
            // UI thread (deadlock on shutdown / busy window).
            RunOnUi(() =>
            {
                if (!Enum.TryParse(typeStr, true, out LogType type))
                {
                    type = LogType.Info;
                }

                _logEntries.Add(new LogEntry { Type = type, Message = message });

                // Keep log capped at 500 lines
                while (_logEntries.Count > 500)
                {
                    _logEntries.RemoveAt(0);
                }

                logScrollViewer?.ScrollToEnd();
            });
        }

        private void RunOnUi(Action action)
        {
            var dispatcher = Dispatcher;
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                _ = dispatcher.InvokeAsync(action);
            }
            catch (InvalidOperationException)
            {
                // Dispatcher started shutting down between the check and the dispatch.
            }
            catch (TaskCanceledException)
            {
            }
        }

        private void Log(string message, LogType type = LogType.Info)
        {
            OnLogMessage(message, type.ToString());
        }

        private void OnStatusChanged(string status)
        {
            RunOnUi(() =>
            {
                if (statusText != null)
                {
                    statusText.Text = status;
                }
            });
        }

        private async Task TryConnectAsync(bool automatic = false)
        {
            if (_isConnecting || _shutdownStarted)
            {
                return;
            }

            // Do not hammer a mouse that is present but not answering (e.g. switched off).
            if (automatic && (DateTime.UtcNow - _lastConnectFailureUtc).TotalSeconds < 10)
            {
                return;
            }

            _isConnecting = true;
            try
            {
                Log("Searching for MCHOSE USB / 2.4G mouse hardware...", LogType.Info);
                App.DebugLog("TryConnectAsync: scanning HID devices in background...");

                bool connected = false;
                try
                {
                    connected = await Task.Run(() => _hid.Connect());
                }
                catch (Exception ex)
                {
                    App.DebugLog($"TryConnectAsync: HID enumeration failed: {ex}");
                    Log($"Device scan failed: {ex.Message}", LogType.Error);
                }

                App.DebugLog($"TryConnectAsync: connected={connected}");

                if (!connected || _hid.CurrentDevice == null)
                {
                    ApplyDisconnectedState(Loc.T("Status_Disconnected"));
                    App.DebugLog("TryConnectAsync: No matching device found.");
                    return;
                }

                bool linkIsLive = await ProbeCurrentLinkAsync();

                if (!linkIsLive)
                {
                    // The opened interface exists but carries no data. This is what happens when
                    // the cable and the 2.4G dongle are both plugged in: the mouse runs on only
                    // one of them, the other still accepts requests but answers with 0xFF.
                    string currentPath = _hid.CurrentDevice.DevicePath;
                    var alternatives = await Task.Run(() => NativeHid.ConfigCandidates()
                        .Where(d => d.DevicePath != currentPath)
                        .ToList());

                    foreach (var candidate in alternatives)
                    {
                        App.DebugLog($"Trying configuration link PID 0x{candidate.ProductId:X4}...");
                        _batteryRefreshTimer.Stop();
                        _hid.Disconnect();

                        bool opened = false;
                        try
                        {
                            opened = await Task.Run(() => _hid.Connect(candidate));
                        }
                        catch (Exception ex)
                        {
                            App.DebugLog($"Switching to PID 0x{candidate.ProductId:X4} failed: {ex.Message}");
                        }

                        if (!opened)
                        {
                            continue;
                        }

                        if (await ProbeCurrentLinkAsync())
                        {
                            linkIsLive = true;
                            break;
                        }
                    }
                }

                if (linkIsLive)
                {
                    _lastConnectFailureUtc = DateTime.MinValue;
                    UpdateUIFromState();
                    _batteryRefreshTimer.Start();
                    App.DebugLog("TryConnectAsync: Initial sync complete.");
                }
                else
                {
                    _lastConnectFailureUtc = DateTime.UtcNow;
                    _hid.Disconnect();
                    ApplyDisconnectedState(Loc.T("Status_NotResponding"));
                    Log("Mouse detected, but none of its configuration links answered. Check whether the mouse is in wired or 2.4G mode.", LogType.Warn);
                    App.DebugLog("TryConnectAsync: no live configuration link.");
                }
            }
            finally
            {
                _isConnecting = false;
            }
        }

        /// <summary>
        /// Shows the connected state for the currently opened link and verifies it carries real
        /// settings. Returns false when the link stays silent (wrong interface for the mode).
        /// </summary>
        private async Task<bool> ProbeCurrentLinkAsync()
        {
            var device = _hid.CurrentDevice;
            if (device == null)
            {
                return false;
            }

            _autoConnectSuspended = false;
            _state.IsConnected = true;
            _state.ProductName = device.Product;
            _state.VendorId = device.VendorId;
            _state.ProductId = device.ProductId;

            statusDot.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Green
            statusText.Text = _state.ProductName;
            btnConnect.Content = Loc.T("Btn_Disconnect");
            btnConnect.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));

            heroDeviceName.Text = _state.ProductName;
            heroVidPid.Text = _state.VidPidString;

            App.DebugLog($"TryConnectAsync: Reading settings (PID 0x{device.ProductId:X4})...");
            await _protocol.ReadAllSettingsAsync();

            if (!_protocol.LastReadHadData)
            {
                App.DebugLog($"PID 0x{device.ProductId:X4} opened but returned no settings.");
                return false;
            }

            await _protocol.ReadBatteryAsync();
            Log($"Connected to {_state.ProductName} ({_state.VidPidString})", LogType.Info);
            return true;
        }

        /// <summary>
        /// Puts the UI into the "no device" state. Used by user disconnect, unplug and failed scans.
        /// </summary>
        private void ApplyDisconnectedState(string status)
        {
            _state.IsConnected = false;
            statusDot.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)); // Red
            statusText.Text = status;
            batteryBadge.Visibility = Visibility.Collapsed;
            btnConnect.Content = Loc.T("Btn_Connect");
            btnConnect.Background = (SolidColorBrush)FindResource("BrushPrimary");
            heroBatteryText.Text = "--%";
            heroBatteryFill.Width = 0;
            _batteryRefreshTimer.Stop();
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_hid.IsConnected)
            {
                // Respect the user's choice: don't let the watchdog reconnect immediately.
                _autoConnectSuspended = true;
                _hid.Disconnect();
                ApplyDisconnectedState(Loc.T("Status_Disconnected"));
                Log("Mouse disconnected by user.", LogType.Info);
            }
            else
            {
                _autoConnectSuspended = false;
                await TryConnectAsync();
            }
        }

        private void UpdateUIFromState()
        {
            _isUpdatingUI = true;

            try
            {
                // Battery
                UpdateBatteryUI();

                // Modes
                SetModeButtonStyles(_state.GameMode);

                // Polling Rate
                SetRateButtonStyles(_state.RateIndex);

                // Level Count
                SetLevelButtonStyles(_state.NumLevels);

                // LOD
                SetLodButtonStyles(_state.LodIndex);

                // Sensor Toggles
                toggleMotionSync.IsChecked = _state.MotionSync;
                toggleRipple.IsChecked = _state.Ripple;
                toggleAngleSnap.IsChecked = _state.AngleSnap;

                // Rotation
                sliderRotate.Value = _state.RotateVal;
                txtRotateVal.Text = Loc.F("Sensor_RotateFmt", _state.RotateVal);

                // Debounce
                sliderDebounce.Value = _state.DebounceMs;
                txtDebounceVal.Text = Loc.F("Power_DebounceValueFmt", _state.DebounceMs);

                // Sleep
                sliderSleep.Value = _state.SleepMinutes;
                txtSleepVal.Text = Loc.F("Power_SleepValueFmt", _state.SleepMinutes);
                chkNeverSleep.IsChecked = _state.NeverSleep;

                // DPI Stages
                RenderDpiStages();
            }
            finally
            {
                _isUpdatingUI = false;
            }
        }

        private void UpdateBatteryUI()
        {
            if (_state.BatteryLevel < 0)
            {
                return;
            }

            heroBatteryText.Text = _state.BatteryText;
            batteryBadge.Text = _state.BatteryText;
            batteryBadge.Visibility = Visibility.Visible;

            heroBatteryFill.Width = (_state.BatteryLevel / 100.0) * 34.0;
            Color batColor = _state.BatteryLevel switch
            {
                <= 20 => Color.FromRgb(0xEF, 0x44, 0x44),
                <= 40 => Color.FromRgb(0xF5, 0x9E, 0x0B),
                _ => Color.FromRgb(0xA8, 0x55, 0xF7)
            };
            heroBatteryFill.Background = new SolidColorBrush(batColor);
        }

        /// <summary>
        /// Reacts to battery / active-stage changes coming from the HID thread (live input
        /// reports and periodic battery polls) so the UI is no longer frozen at connect time.
        /// </summary>
        private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_shutdownStarted)
            {
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(MouseState.BatteryLevel):
                case nameof(MouseState.IsCharging):
                    RunOnUi(() =>
                    {
                        if (_isUpdatingUI) return;
                        UpdateBatteryUI();
                    });
                    break;

                case nameof(MouseState.ActiveDpiIndex):
                    RunOnUi(() =>
                    {
                        if (_isUpdatingUI) return;
                        activeDpiBadge.Text = _state.ActiveDpiText;
                        RenderDpiStages();
                    });
                    break;
            }
        }

        private void OnHidDeviceRemoved()
        {
            RunOnUi(() =>
            {
                if (_shutdownStarted) return;

                ApplyDisconnectedState(Loc.T("Status_DeviceUnplugged"));
                Log("Mouse disconnected (device removed).", LogType.Warn);

                // Probe again shortly in case the dongle re-enumerates.
                _deviceChangeTimer.Stop();
                _deviceChangeTimer.Start();
            });
        }

        /// <summary>
        /// Hot-plug handler: reconciles the UI with what is actually plugged in.
        /// Reconnects whenever a config-capable device is present, unless the user has just
        /// pressed "Disconnect" themselves (that pause is cleared by the next topology change).
        /// </summary>
        private async Task RefreshDevicePresenceAsync()
        {
            if (_shutdownStarted)
            {
                return;
            }

            bool present = NativeHid.IsSupportedDevicePresent();
            bool connected = _hid.IsConnected;

            if (present != _lastPresenceProbe || connected != _lastConnectedProbe)
            {
                App.DebugLog($"Presence probe: present={present}, connected={connected}, suspended={_autoConnectSuspended}");
                _lastPresenceProbe = present;
                _lastConnectedProbe = connected;
            }

            if (connected)
            {
                if (!_hid.IsCurrentDevicePresent())
                {
                    _hid.Disconnect();
                    ApplyDisconnectedState(Loc.T("Status_DeviceUnplugged"));
                    Log("Mouse disconnected (device removed).", LogType.Warn);
                    return;
                }

                // Poll link health so a wired <-> 2.4G switch is noticed without user action.
                if ((DateTime.UtcNow - _lastLinkProbeUtc).TotalSeconds >= 20 && _protocol.PendingCount == 0)
                {
                    _lastLinkProbeUtc = DateTime.UtcNow;
                    await _protocol.ProbeLinkAsync();
                }

                MaybeRebuildLink();
                return;
            }

            if (present && !_autoConnectSuspended)
            {
                await TryConnectAsync(automatic: true);
            }
        }

        /// <summary>
        /// If the opened link stops answering while another configuration link exists (the user
        /// moved the mouse from the cable to the 2.4G dongle or back), drop the current handle so
        /// the next connect probes and picks the live one. Throttled to avoid churn.
        /// </summary>
        private void MaybeRebuildLink()
        {
            if (_protocol.LastReadHadData) return;

            if ((DateTime.UtcNow - _lastLinkRebuildUtc).TotalSeconds < 60) return;
            if (NativeHid.ConfigInterfaceCount() < 2) return;

            _lastLinkRebuildUtc = DateTime.UtcNow;
            App.DebugLog("Connected link stopped answering; re-probing configuration interfaces...");
            _hid.Disconnect();
            ApplyDisconnectedState(Loc.T("Status_Reconnecting"));
        }

        private void SetModeButtonStyles(int mode)
        {
            var activeBorder = (SolidColorBrush)FindResource("BrushAccent");
            var activeBg = new SolidColorBrush(Color.FromRgb(0x1F, 0x13, 0x3D));
            var idleBorder = new SolidColorBrush(Color.FromRgb(0x2E, 0x1A, 0x5E));
            var idleBg = new SolidColorBrush(Color.FromRgb(0x17, 0x0E, 0x2F));

            Button[] modeButtons = { btnMode1, btnMode2, btnMode3 };
            for (int i = 0; i < modeButtons.Length; i++)
            {
                bool isActive = (i + 1 == mode);
                modeButtons[i].BorderBrush = isActive ? activeBorder : idleBorder;
                modeButtons[i].BorderThickness = new Thickness(isActive ? 1.5 : 1);
                modeButtons[i].Background = isActive ? activeBg : idleBg;
                if (modeButtons[i].Content is StackPanel sp)
                {
                    if (sp.Children.Count > 0 && sp.Children[0] is TextBlock tb1)
                        tb1.Foreground = isActive ? activeBorder : Brushes.White;
                    if (sp.Children.Count > 1 && sp.Children[1] is TextBlock tb2)
                        tb2.Foreground = isActive ? activeBorder : (SolidColorBrush)FindResource("BrushTextMuted");
                }
            }

            activeModeBadge.Text = mode switch
            {
                1 => Loc.T("Mode_Performance"),
                3 => Loc.T("Mode_Extreme"),
                _ => Loc.T("Mode_Gaming")
            };

            modeDescText.Text = mode switch
            {
                1 => Loc.T("Mode_Desc_Performance"),
                3 => Loc.T("Mode_Desc_Extreme"),
                _ => Loc.T("Mode_Desc_Gaming")
            };
        }

        private void SetRateButtonStyles(int rateIdx)
        {
            Button[] buttons = { btnRate0, btnRate1, btnRate2, btnRate3, btnRate4, btnRate5 };
            var activeBorder = (SolidColorBrush)FindResource("BrushAccent");
            var activeBg = new SolidColorBrush(Color.FromRgb(0x1F, 0x13, 0x3D));
            var idleBorder = new SolidColorBrush(Color.FromRgb(0x2E, 0x1A, 0x5E));
            var idleBg = new SolidColorBrush(Color.FromRgb(0x17, 0x0E, 0x2F));

            for (int i = 0; i < buttons.Length; i++)
            {
                bool isActive = (i == rateIdx);
                buttons[i].BorderBrush = isActive ? activeBorder : idleBorder;
                buttons[i].BorderThickness = new Thickness(isActive ? 1.5 : 1);
                buttons[i].Background = isActive ? activeBg : idleBg;
                if (buttons[i].Content is StackPanel sp)
                {
                    if (sp.Children.Count > 0 && sp.Children[0] is TextBlock tb1)
                        tb1.Foreground = isActive ? activeBorder : Brushes.White;
                    if (sp.Children.Count > 1 && sp.Children[1] is TextBlock tb2)
                        tb2.Foreground = isActive ? activeBorder : (SolidColorBrush)FindResource("BrushTextMuted");
                }
            }

            activeHzBadge.Text = Loc.F("Rate_ValueFmt", _state.ActiveHz);
        }

        private void SetLevelButtonStyles(int count)
        {
            Button[] buttons = { btnLvl1, btnLvl2, btnLvl3, btnLvl4, btnLvl5, btnLvl6 };
            for (int i = 0; i < buttons.Length; i++)
            {
                bool isActive = (i + 1 == count);
                buttons[i].Background = isActive ? (SolidColorBrush)FindResource("BrushAccent") : Brushes.Transparent;
                buttons[i].Foreground = isActive ? Brushes.Black : (SolidColorBrush)FindResource("BrushTextMuted");
            }
        }

        private void SetLodButtonStyles(int lodIdx)
        {
            Button[] buttons = { btnLod0, btnLod1, btnLod2 };
            for (int i = 0; i < buttons.Length; i++)
            {
                bool isActive = (i == lodIdx);
                buttons[i].Background = isActive ? (SolidColorBrush)FindResource("BrushAccent") : Brushes.Transparent;
                buttons[i].Foreground = isActive ? Brushes.Black : (SolidColorBrush)FindResource("BrushTextMuted");
            }
        }

        private void RenderDpiStages()
        {
            bool wasUpdating = _isUpdatingUI;
            _isUpdatingUI = true;
            try
            {
                dpiStagesContainer.Children.Clear();

            for (int i = 0; i < _state.NumLevels; i++)
            {
                int stageIndex = i;
                int currentVal = (stageIndex < _state.DpiStages.Length) ? _state.DpiStages[stageIndex] : 800;
                string hexColor = MouseState.StageColors[stageIndex % MouseState.StageColors.Length];
                var stageColor = (Color)ColorConverter.ConvertFromString(hexColor);

                var rowBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x60, 0x13, 0x1B, 0x2E)),
                    BorderBrush = (stageIndex == _state.ActiveDpiIndex) 
                        ? (SolidColorBrush)FindResource("BrushAccent") 
                        : new SolidColorBrush(Color.FromRgb(0x25, 0x33, 0x4D)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                // Radio Selector
                var radio = new RadioButton
                {
                    GroupName = "DpiStageGroup",
                    IsChecked = (stageIndex == _state.ActiveDpiIndex),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                radio.Checked += async (s, e) =>
                {
                    if (_isUpdatingUI) return;
                    _state.ActiveDpiIndex = stageIndex;
                    activeDpiBadge.Text = _state.ActiveDpiText;
                    RenderDpiStages();
                    await _protocol.ApplyDpiAsync();
                };
                Grid.SetColumn(radio, 0);
                grid.Children.Add(radio);

                // Stage Color Dot
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(stageColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(dot, 1);
                grid.Children.Add(dot);

                // Stage Name
                var lbl = new TextBlock
                {
                    Text = Loc.F("Dpi_StageName", stageIndex + 1),
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    Foreground = (SolidColorBrush)FindResource("BrushText"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lbl, 2);
                grid.Children.Add(lbl);

                // Slider
                var slider = new Slider
                {
                    Minimum = 50,
                    Maximum = 42000,
                    SmallChange = 50,
                    LargeChange = 500,
                    Value = currentVal,
                    Style = (Style)FindResource("CyberSlider"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 16, 0)
                };
                Grid.SetColumn(slider, 3);
                grid.Children.Add(slider);

                // Number Box
                var txtNum = new TextBox
                {
                    Text = currentVal.ToString(),
                    Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x13, 0x22)),
                    Foreground = (SolidColorBrush)FindResource("BrushAccent"),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 4, 6, 4),
                    FontFamily = new FontFamily("Consolas, monospace"),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(txtNum, 4);
                grid.Children.Add(txtNum);

                slider.ValueChanged += (s, e) =>
                {
                    if (_isUpdatingUI) return;
                    int stepVal = ((int)Math.Round(slider.Value / 50.0)) * 50;
                    stepVal = Math.Clamp(stepVal, 50, 42000);
                    _state.DpiStages[stageIndex] = stepVal;
                    txtNum.Text = stepVal.ToString();
                    if (stageIndex == _state.ActiveDpiIndex)
                    {
                        activeDpiBadge.Text = _state.ActiveDpiText;
                    }
                    ScheduleDpi();
                };

                txtNum.LostFocus += (s, e) =>
                {
                    if (int.TryParse(txtNum.Text, out int parsed))
                    {
                        int clamped = Math.Clamp(parsed, 50, 42000);
                        slider.Value = clamped;
                        ScheduleDpi();
                    }
                    else
                    {
                        txtNum.Text = slider.Value.ToString(CultureInfo.InvariantCulture);
                    }
                };

                rowBorder.Child = grid;
                dpiStagesContainer.Children.Add(rowBorder);
            }

                activeDpiBadge.Text = _state.ActiveDpiText;
            }
            finally
            {
                _isUpdatingUI = wasUpdating;
            }
        }

        private async void BtnMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse(b.Tag?.ToString(), out int mode))
            {
                _state.GameMode = mode;
                SetModeButtonStyles(mode);
                await _protocol.ApplySensorAndModeAsync();
            }
        }

        private async void BtnRate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse(b.Tag?.ToString(), out int rate))
            {
                _state.RateIndex = rate;
                SetRateButtonStyles(rate);
                await _protocol.ApplyPollingRateAsync(rate);
            }
        }

        private async void BtnLevelCount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse(b.Tag?.ToString(), out int count))
            {
                _state.NumLevels = count;
                SetLevelButtonStyles(count);
                RenderDpiStages();
                await _protocol.ApplyDpiAsync();
            }
        }

        private async void BtnSaveDpi_Click(object sender, RoutedEventArgs e)
        {
            await _protocol.ApplyDpiAsync();
        }

        private async void BtnLod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse(b.Tag?.ToString(), out int lod))
            {
                _state.LodIndex = lod;
                SetLodButtonStyles(lod);
                await _protocol.ApplySensorAndModeAsync();
            }
        }

        private void SensorToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingUI || toggleMotionSync == null || toggleRipple == null || toggleAngleSnap == null) return;

            _state.MotionSync = (toggleMotionSync.IsChecked == true);
            _state.Ripple = (toggleRipple.IsChecked == true);
            _state.AngleSnap = (toggleAngleSnap.IsChecked == true);

            ScheduleSensor();
        }

        private void SliderRotate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _isUpdatingUI || txtRotateVal == null || sliderRotate == null) return;

            int rot = (int)Math.Round(sliderRotate.Value);
            _state.RotateVal = rot;
            txtRotateVal.Text = Loc.F("Sensor_RotateFmt", rot);

            ScheduleSensor();
        }

        private async void BtnResetRotate_Click(object sender, RoutedEventArgs e)
        {
            _sensorDebounceTimer.Stop();
            _isUpdatingUI = true;
            try
            {
                if (sliderRotate != null) sliderRotate.Value = 0;
                _state.RotateVal = 0;
                if (txtRotateVal != null) txtRotateVal.Text = Loc.T("Sensor_RotateDefault");
            }
            finally
            {
                _isUpdatingUI = false;
            }
            await _protocol.ApplySensorAndModeAsync();
        }

        private void SliderDebounce_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _isUpdatingUI || txtDebounceVal == null || sliderDebounce == null || _debounceTimer == null) return;

            int ms = (int)Math.Round(sliderDebounce.Value);
            _state.DebounceMs = ms;
            txtDebounceVal.Text = Loc.F("Power_DebounceValueFmt", ms);

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void SliderSleep_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _isUpdatingUI || txtSleepVal == null || sliderSleep == null || _sleepDebounceTimer == null) return;

            int min = (int)Math.Round(sliderSleep.Value);
            _state.SleepMinutes = min;
            txtSleepVal.Text = Loc.F("Power_SleepValueFmt", min);

            _sleepDebounceTimer.Stop();
            _sleepDebounceTimer.Start();
        }

        private async void ChkNeverSleep_Click(object sender, RoutedEventArgs e)
        {
            _state.NeverSleep = (chkNeverSleep.IsChecked == true);
            await _protocol.ApplySleepAsync(_state.SleepMinutes, _state.NeverSleep);
        }

        private void ScheduleSensor()
        {
            if (_isUpdatingUI) return;
            _sensorDebounceTimer.Stop();
            _sensorDebounceTimer.Start();
        }

        private void ScheduleDpi()
        {
            if (_isUpdatingUI) return;
            _dpiDebounceTimer.Stop();
            _dpiDebounceTimer.Start();
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            _logEntries.Clear();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F8)
            {
                OpenTester();
                e.Handled = true;
            }
        }

        private TesterWindow? _testerWindow;

        private void BtnLaunchTester_Click(object sender, RoutedEventArgs e)
        {
            OpenTester();
        }

        private void OpenTester()
        {
            if (_testerWindow != null && _testerWindow.IsLoaded)
            {
                _testerWindow.Activate();
                return;
            }

            _testerWindow = new TesterWindow { Owner = this };
            _testerWindow.Closed += (s, e) => _testerWindow = null;
            _testerWindow.ShowDialog();
        }
    }
}
