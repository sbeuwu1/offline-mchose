using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MchoseHub.Hardware
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// High-performance Raw Input receiver supporting 125 Hz up to 8000+ Hz polling rates.
    ///
    /// Key architectural highlights:
    /// 1. Dedicated background STA thread with message-only sink window (HWND_MESSAGE).
    /// 2. Event-driven Win32 GetMessageW message pump with batch drain of simultaneous packets.
    /// 3. Normal thread and process priority to ensure zero CPU core starvation and smooth 60+ FPS DWM rendering.
    /// 4. Microsecond-accurate sliding window duration calculation anchored by hardware timestamps.
    /// 5. Snappy 40ms zero-Hz clamp when mouse stops to prevent lingering and fake burst readings.
    /// </summary>
    public class RawInputReceiver : IDisposable
    {
        // ── Win32 Constants ──────────────────────────────────────────────
        private const int WM_INPUT = 0x00FF;
        private const int WM_DESTROY = 0x0002;
        private const int WM_QUIT = 0x0012;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_DEVNOTIFY = 0x00002000;
        private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        private const ushort HID_USAGE_GENERIC_MOUSE = 0x02;

        private const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
        private const ushort RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
        private const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
        private const ushort RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
        private const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
        private const ushort RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
        private const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;
        private const ushort RI_MOUSE_BUTTON_4_UP = 0x0080;
        private const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100;
        private const ushort RI_MOUSE_BUTTON_5_UP = 0x0200;

        private const int HWND_MESSAGE = -3;
        private const uint PM_REMOVE = 0x0001;

        // ── Win32 Structs ────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        // x64 RAWINPUTHEADER = 24 bytes, RAWMOUSE = 24 bytes → total 48 bytes
        [StructLayout(LayoutKind.Explicit, Size = 48)]
        private unsafe struct RAWINPUT64
        {
            [FieldOffset(0)] public uint dwType;
            [FieldOffset(4)] public uint dwSize;
            [FieldOffset(8)] public IntPtr hDevice;
            [FieldOffset(16)] public IntPtr wParam;
            [FieldOffset(24)] public ushort usFlags;
            [FieldOffset(28)] public ushort usButtonFlags;
            [FieldOffset(30)] public ushort usButtonData;
            [FieldOffset(32)] public uint ulRawButtons;
            [FieldOffset(36)] public int lLastX;
            [FieldOffset(40)] public int lLastY;
            [FieldOffset(44)] public uint ulExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEXW
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszClassName;
            public IntPtr hIconSm;
        }

        // ── Win32 Imports ────────────────────────────────────────────────
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] RAWINPUTDEVICE[] pRawInputDevices,
            uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(
            IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(
            uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        // ── Thread & Window State ────────────────────────────────────────
        private Thread? _inputThread;
        private IntPtr _sinkHwnd;
        private WndProcDelegate? _wndProcDelegate;
        private volatile bool _disposed;

        // ── Hz Measurement ───────────────────────────────────────────────
        private volatile bool _isHzTesting;
        private long _lastMotionTicks;
        private volatile int _currentHz;
        private volatile int _peakHz;
        private long _totalPackets;
        private long _firstPacketTicks;
        private readonly double _tickFreq = Stopwatch.Frequency;

        // Circular timestamp buffer (32,768 entries = ~4 seconds at 8K Hz)
        private const int TS_BUF_SIZE = 32768;
        private readonly long[] _tsBuf = new long[TS_BUF_SIZE];
        private int _tsHead;
        private int _tsCount;

        // Motion counter (input thread increments, UI reads delta)
        private long _motionCounter;
        private long _lastConsumedCounter;

        // ── Button / Click state ─────────────────────────────────────────
        private long _lastLeftDownTicks;
        private long _lastRightDownTicks;
        private long _lastMiddleDownTicks;
        private long _lastButton4DownTicks;
        private long _lastButton5DownTicks;
        private int _clickCount;
        private int _doubleClickCount;
        private double _fastestClickIntervalMs = 9999;
        private double _lastIntervalMs;

        public event Action<string, double, bool>? ClickRecorded;
        public event Action<string, bool>? ButtonStateChanged;

        // ── Public API ───────────────────────────────────────────────────
        public bool IsHzTesting
        {
            get => _isHzTesting;
            set
            {
                _isHzTesting = value;
                if (value)
                {
                    _lastMotionTicks = Stopwatch.GetTimestamp();
                    _firstPacketTicks = 0;
                }
            }
        }

        public int CurrentHz => _currentHz;
        public int PeakHz => _peakHz;
        public int ClickCount => _clickCount;
        public int DoubleClickCount => _doubleClickCount;
        public double FastestIntervalMs => _fastestClickIntervalMs == 9999 ? 0 : _fastestClickIntervalMs;
        public double LastIntervalMs => _lastIntervalMs;

        /// <summary>
        /// Returns number of raw motion packets since last call. Called by UI at 60 FPS.
        /// Zero locking, single atomic read.
        /// </summary>
        public long ConsumeMotionCount()
        {
            long current = Volatile.Read(ref _motionCounter);
            long delta = current - _lastConsumedCounter;
            _lastConsumedCounter = current;
            return delta;
        }

        public void Initialize(IntPtr testerHwnd)
        {
            _lastMotionTicks = Stopwatch.GetTimestamp();

            var readyEvent = new ManualResetEventSlim(false);
            _inputThread = new Thread(() => InputThreadProc(readyEvent))
            {
                IsBackground = true,
                Name = "RawInputSink",
                Priority = ThreadPriority.Normal
            };
            _inputThread.SetApartmentState(ApartmentState.STA);
            _inputThread.Start();

            readyEvent.Wait(TimeSpan.FromSeconds(5));
            readyEvent.Dispose();
            Debug.WriteLine($"[RawInput] Background thread started, sink HWND=0x{_sinkHwnd:X}");
        }

        // ── Background Thread ────────────────────────────────────────────

        private void InputThreadProc(ManualResetEventSlim ready)
        {
            try
            {
                timeBeginPeriod(1);

                _wndProcDelegate = SinkWndProc;
                IntPtr wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
                IntPtr hInst = GetModuleHandle(null);

                string cls = "MchoseRawSink_" + Environment.TickCount;
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    style = 0,
                    lpfnWndProc = wndProcPtr,
                    hInstance = hInst,
                    lpszClassName = cls,
                };

                if (RegisterClassExW(ref wc) == 0)
                {
                    Debug.WriteLine($"[RawInput] RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
                    ready.Set();
                    return;
                }

                _sinkHwnd = CreateWindowExW(0, cls, "MchoseRawSink", 0,
                    0, 0, 0, 0, (IntPtr)HWND_MESSAGE, IntPtr.Zero, hInst, IntPtr.Zero);

                if (_sinkHwnd == IntPtr.Zero)
                {
                    Debug.WriteLine($"[RawInput] CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
                    ready.Set();
                    return;
                }

                var dev = new RAWINPUTDEVICE[1];
                dev[0].usUsagePage = HID_USAGE_PAGE_GENERIC;
                dev[0].usUsage = HID_USAGE_GENERIC_MOUSE;
                dev[0].dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY;
                dev[0].hwndTarget = _sinkHwnd;

                if (!RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                    Debug.WriteLine($"[RawInput] RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}");

                ready.Set();

                // Clean event-driven message loop with zero CPU spinning
                RunMessageLoop();

                timeEndPeriod(1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RawInput] Thread exception: {ex}");
                ready.Set();
            }
        }

        private unsafe void RunMessageLoop()
        {
            MSG msg;
            RAWINPUT64 raw;

            while (!_disposed && GetMessageW(out msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_QUIT)
                    return;

                if (msg.message == WM_INPUT)
                {
                    uint size = 48;
                    if (GetRawInputData(msg.lParam, RID_INPUT, (IntPtr)(&raw), ref size, 24) != uint.MaxValue)
                    {
                        if (raw.dwType == 0) // RIM_TYPEMOUSE
                        {
                            ProcessMousePacket(ref raw);
                        }
                    }

                    // Drain all queued WM_INPUT packets that arrived simultaneously
                    while (PeekMessageW(out MSG peek, IntPtr.Zero, WM_INPUT, WM_INPUT, PM_REMOVE))
                    {
                        size = 48;
                        if (GetRawInputData(peek.lParam, RID_INPUT, (IntPtr)(&raw), ref size, 24) != uint.MaxValue)
                        {
                            if (raw.dwType == 0)
                            {
                                ProcessMousePacket(ref raw);
                            }
                        }
                    }
                }
                else
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
            }
        }

        private IntPtr SinkWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_DESTROY) { PostQuitMessage(0); return IntPtr.Zero; }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private void ProcessMousePacket(ref RAWINPUT64 raw)
        {
            long now = Stopwatch.GetTimestamp();

            // Motion detection
            int dx = raw.lLastX;
            int dy = raw.lLastY;

            if ((dx | dy) != 0 && _isHzTesting)
            {
                if (_firstPacketTicks == 0) _firstPacketTicks = now;

                _tsBuf[_tsHead] = now;
                _tsHead = (_tsHead + 1) & (TS_BUF_SIZE - 1);
                if (_tsCount < TS_BUF_SIZE) _tsCount++;
                _totalPackets++;
                _lastMotionTicks = now;

                _motionCounter++;
            }

            // Buttons
            ushort btn = raw.usButtonFlags;
            if (btn != 0) HandleButtons(btn, now);
        }

        private void HandleButtons(ushort btn, long now)
        {
            if ((btn & RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
            { ButtonStateChanged?.Invoke("Left", true); ProcessClick("Left Click", ref _lastLeftDownTicks, now); }
            if ((btn & RI_MOUSE_LEFT_BUTTON_UP) != 0)
            { ButtonStateChanged?.Invoke("Left", false); }

            if ((btn & RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
            { ButtonStateChanged?.Invoke("Right", true); ProcessClick("Right Click", ref _lastRightDownTicks, now); }
            if ((btn & RI_MOUSE_RIGHT_BUTTON_UP) != 0)
            { ButtonStateChanged?.Invoke("Right", false); }

            if ((btn & RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
            { ButtonStateChanged?.Invoke("Middle", true); ProcessClick("Middle Click", ref _lastMiddleDownTicks, now); }
            if ((btn & RI_MOUSE_MIDDLE_BUTTON_UP) != 0)
            { ButtonStateChanged?.Invoke("Middle", false); }

            if ((btn & RI_MOUSE_BUTTON_4_DOWN) != 0)
            { ButtonStateChanged?.Invoke("Side1", true); ProcessClick("Side 1 (Back)", ref _lastButton4DownTicks, now); }
            if ((btn & RI_MOUSE_BUTTON_4_UP) != 0)
            { ButtonStateChanged?.Invoke("Side1", false); }

            if ((btn & RI_MOUSE_BUTTON_5_DOWN) != 0)
            { ButtonStateChanged?.Invoke("Side2", true); ProcessClick("Side 2 (Forward)", ref _lastButton5DownTicks, now); }
            if ((btn & RI_MOUSE_BUTTON_5_UP) != 0)
            { ButtonStateChanged?.Invoke("Side2", false); }
        }

        // ── Hz Sampling (called by UI thread at 60 FPS) ──────────────────
        public void SampleHz(out int currentHz, out int peakHz, out int avgHz)
        {
            long now = Stopwatch.GetTimestamp();
            double idleSec = (now - _lastMotionTicks) / _tickFreq;

            // If no motion for 40ms, mouse has stopped moving -> immediate 0 Hz clamp
            if (idleSec >= 0.040)
            {
                _currentHz = 0;
            }
            else
            {
                // 80ms sliding window
                long windowTicks = (long)(0.080 * _tickFreq);
                long cutoff = now - windowTicks;

                int head = Volatile.Read(ref _tsHead);
                int histCount = _tsCount;

                int count = 0;
                long newest = 0;
                long oldest = now;

                for (int i = 0; i < histCount; i++)
                {
                    int idx = (head - 1 - i + TS_BUF_SIZE) & (TS_BUF_SIZE - 1);
                    long t = _tsBuf[idx];
                    if (t >= cutoff)
                    {
                        if (count == 0) newest = t;
                        oldest = t;
                        count++;
                    }
                    else break;
                }

                if (count >= 4 && newest > oldest)
                {
                    double dur = (newest - oldest) / _tickFreq;
                    if (dur >= 0.008) // At least 8ms window of packet stream
                    {
                        int hz = (int)Math.Round((count - 1) / dur);

                        if (hz <= 10000)
                        {
                            _currentHz = hz;
                            if (hz > _peakHz) _peakHz = hz;
                        }
                    }
                }
            }

            long totalPkts = _totalPackets;
            double totalSec = _firstPacketTicks > 0 ? (now - _firstPacketTicks) / _tickFreq : 0;
            avgHz = totalSec > 0.5 ? (int)Math.Round(totalPkts / totalSec) : _currentHz;
            currentHz = _currentHz;
            peakHz = _peakHz;
        }

        public void ResetHz()
        {
            _currentHz = 0;
            _peakHz = 0;
            _totalPackets = 0;
            _firstPacketTicks = 0;
            _tsCount = 0;
            _tsHead = 0;
            Array.Clear(_tsBuf, 0, TS_BUF_SIZE);
            _lastMotionTicks = Stopwatch.GetTimestamp();
            _lastConsumedCounter = Volatile.Read(ref _motionCounter);
        }

        public void ResetClicks()
        {
            _clickCount = 0;
            _doubleClickCount = 0;
            _fastestClickIntervalMs = 9999;
            _lastIntervalMs = 0;
            _lastLeftDownTicks = 0;
            _lastRightDownTicks = 0;
            _lastMiddleDownTicks = 0;
            _lastButton4DownTicks = 0;
            _lastButton5DownTicks = 0;
        }

        private void ProcessClick(string name, ref long lastDown, long now)
        {
            Interlocked.Increment(ref _clickCount);
            double ms = 0;
            bool fast = false;

            if (lastDown != 0)
            {
                ms = (now - lastDown) * 1000.0 / _tickFreq;
                if (ms > 0 && ms < 2000)
                {
                    _lastIntervalMs = ms;
                    if (ms < _fastestClickIntervalMs) _fastestClickIntervalMs = ms;
                    if (ms < 55.0) { Interlocked.Increment(ref _doubleClickCount); fast = true; }
                }
            }

            lastDown = now;
            ClickRecorded?.Invoke(name, ms, fast);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_sinkHwnd != IntPtr.Zero)
                PostMessageW(_sinkHwnd, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _inputThread?.Join(TimeSpan.FromSeconds(2));
            _inputThread = null;
        }
    }
}
