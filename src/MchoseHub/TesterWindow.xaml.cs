using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MchoseHub.Hardware;

namespace MchoseHub
{
    /// <summary>
    /// Lightweight, 100% GPU DirectX accelerated trail renderer.
    /// Draws dots using frozen brushes directly in OnRender with zero CPU bitmap copies.
    /// </summary>
    public class TrailCanvas : FrameworkElement
    {
        private struct ActiveDot
        {
            public float X;
            public float Y;
            public long Ticks;
        }

        private const int MAX_DOTS = 300;
        private readonly ActiveDot[] _dots = new ActiveDot[MAX_DOTS];
        private int _dotCount;
        private readonly double _lifespanTicks = 0.28 * Stopwatch.Frequency; // 280ms crisp trail

        private static readonly Brush BrushNew = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
        private static readonly Brush BrushMid = Freeze(new SolidColorBrush(Color.FromRgb(0xD8, 0xB4, 0xFE)));
        private static readonly Brush BrushOld = Freeze(new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7)));
        private static readonly Brush BrushFading = Freeze(new SolidColorBrush(Color.FromArgb(130, 0x7C, 0x3A, 0xED)));

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }

        public void AddDot(float x, float y, long ticks)
        {
            if (_dotCount < MAX_DOTS)
            {
                _dots[_dotCount++] = new ActiveDot { X = x, Y = y, Ticks = ticks };
            }
            else
            {
                Array.Copy(_dots, 1, _dots, 0, MAX_DOTS - 1);
                _dots[MAX_DOTS - 1] = new ActiveDot { X = x, Y = y, Ticks = ticks };
            }
        }

        public void ClearDots()
        {
            _dotCount = 0;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (_dotCount == 0) return;

            long now = Stopwatch.GetTimestamp();

            // 1. Remove expired dots
            int writeIdx = 0;
            for (int i = 0; i < _dotCount; i++)
            {
                if ((now - _dots[i].Ticks) <= _lifespanTicks)
                {
                    if (writeIdx != i) _dots[writeIdx] = _dots[i];
                    writeIdx++;
                }
            }
            _dotCount = writeIdx;

            // 2. Hardware-accelerated GPU draw calls
            for (int i = 0; i < _dotCount; i++)
            {
                double age = (double)(now - _dots[i].Ticks) / _lifespanTicks;
                Brush brush;
                double radius;
                if (age < 0.20)
                {
                    brush = BrushNew;
                    radius = 3.5;
                }
                else if (age < 0.50)
                {
                    brush = BrushMid;
                    radius = 3.0;
                }
                else if (age < 0.80)
                {
                    brush = BrushOld;
                    radius = 2.5;
                }
                else
                {
                    brush = BrushFading;
                    radius = 2.0;
                }

                dc.DrawEllipse(brush, null, new Point(_dots[i].X, _dots[i].Y), radius, radius);
            }
        }
    }

    public partial class TesterWindow : Window
    {
        private readonly RawInputReceiver _rawInput = new();
        private DispatcherTimer? _uiTimer;

        // Static frozen brushes (zero GC allocations)
        private static readonly SolidColorBrush BrushActivePillBg = Freeze(new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)));
        private static readonly SolidColorBrush BrushIdlePillBg = Freeze(new SolidColorBrush(Color.FromRgb(0x12, 0x0B, 0x24)));
        private static readonly SolidColorBrush BrushActivePillText = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
        private static readonly SolidColorBrush BrushIdlePillText = Freeze(new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)));

        private static readonly SolidColorBrush BrushGreen = Freeze(new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)));
        private static readonly SolidColorBrush BrushEmerald = Freeze(new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)));
        private static readonly SolidColorBrush BrushSky = Freeze(new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)));
        private static readonly SolidColorBrush BrushPurple = Freeze(new SolidColorBrush(Color.FromRgb(0xC0, 0x84, 0xFC)));
        private static readonly SolidColorBrush BrushViolet = Freeze(new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)));
        private static readonly SolidColorBrush BrushPurpleDark = Freeze(new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7)));
        private static readonly SolidColorBrush BrushBorderIdle = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x1A, 0x5E)));
        private static readonly SolidColorBrush BrushMuted = Freeze(new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)));
        private static readonly SolidColorBrush BrushLightMuted = Freeze(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)));
        private static readonly SolidColorBrush BrushRed = Freeze(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)));
        private static readonly SolidColorBrush BrushRedBg = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xEF, 0x44, 0x44)));
        private static readonly SolidColorBrush BrushIdleBg = Freeze(new SolidColorBrush(Color.FromRgb(0x12, 0x0B, 0x24)));

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        // Cursor tracking for dot interpolation
        private IntPtr _hwnd;
        private float _prevCursorX = -1;
        private float _prevCursorY = -1;
        private int _renderFrameCount;

        public TesterWindow()
        {
            InitializeComponent();
            _rawInput.ClickRecorded += OnClickRecorded;
            _rawInput.ButtonStateChanged += OnButtonStateChanged;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.Normal;
            }
            catch { /* Ignore */ }

            _hwnd = new WindowInteropHelper(this).Handle;
            _rawInput.Initialize(_hwnd);
            _rawInput.IsHzTesting = true;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _rawInput.IsHzTesting = true;

            // 60 FPS ultra-smooth timer
            _uiTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _uiTimer.Tick += OnRenderFrame;
            _uiTimer.Start();
        }

        private void OnRenderFrame(object? sender, EventArgs e)
        {
            // Update text at ~15 FPS to eliminate unnecessary text layout invalidation
            if ((_renderFrameCount++ & 3) == 0)
            {
                _rawInput.SampleHz(out int currentHz, out int peakHz, out int avgHz);
                UpdateHzDisplay(currentHz, peakHz, avgHz);
            }

            // Generate motion dots and refresh GPU canvas
            GenerateDots();
        }

        private void GenerateDots()
        {
            long motionCount = _rawInput.ConsumeMotionCount();
            if (motionCount <= 0)
            {
                trailCanvas.InvalidateVisual();
                return;
            }

            if (!RawInputReceiver.GetCursorPos(out POINT screenPt)) return;

            POINT clientPt = screenPt;
            if (_hwnd != IntPtr.Zero)
                RawInputReceiver.ScreenToClient(_hwnd, ref clientPt);

            float curX = clientPt.X;
            float curY = clientPt.Y;

            if (_prevCursorX < 0)
            {
                _prevCursorX = curX;
                _prevCursorY = curY;
                return;
            }

            float dx = curX - _prevCursorX;
            float dy = curY - _prevCursorY;

            // If mouse has stopped moving, don't generate dots at the stop point
            if (Math.Abs(dx) < 0.5f && Math.Abs(dy) < 0.5f)
            {
                trailCanvas.InvalidateVisual();
                return;
            }

            // Smooth interpolation along the cursor vector (capped at 35 per frame for butter-smooth visual density)
            int dotsToAdd = (int)Math.Min(motionCount, 35);
            long nowTicks = Stopwatch.GetTimestamp();

            for (int i = 0; i < dotsToAdd; i++)
            {
                float t = (i + 1) / (float)dotsToAdd;
                float dotX = _prevCursorX + dx * t;
                float dotY = _prevCursorY + dy * t;
                long dotTicks = nowTicks - (long)((1.0 - t) * 16.0 * Stopwatch.Frequency / 1000.0);
                trailCanvas.AddDot(dotX, dotY, dotTicks);
            }

            _prevCursorX = curX;
            _prevCursorY = curY;
            trailCanvas.InvalidateVisual();
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _uiTimer?.Stop();
            _uiTimer = null;
            _rawInput.Dispose();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.R) ResetAll();
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e) => Close();
        private void BtnReset_Click(object sender, RoutedEventArgs e) => ResetAll();

        private void BtnResetClicks_Click(object sender, RoutedEventArgs e)
        {
            _rawInput.ResetClicks();
            txtClickCount.Text = "0";
            txtLastInterval.Text = "-- ms";
            txtDoubleClickCount.Text = "0";
            txtClickFeedback.Text = "Ready to test clicks";
            txtClickFeedback.Foreground = BrushLightMuted;
            statusBanner.Background = BrushIdleBg;
        }

        private void ResetAll()
        {
            _rawInput.ResetHz();
            _rawInput.ResetClicks();
            _prevCursorX = -1;
            _prevCursorY = -1;

            trailCanvas.ClearDots();

            txtCurrentHz.Text = "0 Hz";
            txtPeakHz.Text = "0 Hz";
            txtAvgHz.Text = "0 Hz";
            txtTargetStatus.Text = "IDLE";
            txtRateTier.Text = "MOVE MOUSE TO TEST";
            rateTierBadge.BorderBrush = BrushBorderIdle;

            txtClickCount.Text = "0";
            txtLastInterval.Text = "-- ms";
            txtDoubleClickCount.Text = "0";
            txtClickFeedback.Text = "Ready to test clicks";
            txtClickFeedback.Foreground = BrushLightMuted;
            statusBanner.Background = BrushIdleBg;
        }

        private void UpdateHzDisplay(int currentHz, int peakHz, int avgHz)
        {
            txtCurrentHz.Text = $"{currentHz} Hz";
            txtPeakHz.Text = $"{peakHz} Hz";
            txtAvgHz.Text = $"{avgHz} Hz";

            if (currentHz >= 9000)
            {
                txtRateTier.Text = "10000 Hz (10K)";
                txtRateTier.Foreground = BrushEmerald;
                rateTierBadge.BorderBrush = BrushEmerald;
            }
            else if (currentHz >= 6800)
            {
                txtRateTier.Text = "8000 Hz (8K)";
                txtRateTier.Foreground = BrushGreen;
                rateTierBadge.BorderBrush = BrushGreen;
            }
            else if (currentHz >= 3400)
            {
                txtRateTier.Text = "4000 Hz (4K)";
                txtRateTier.Foreground = BrushSky;
                rateTierBadge.BorderBrush = BrushSky;
            }
            else if (currentHz >= 1700)
            {
                txtRateTier.Text = "2000 Hz (2K)";
                txtRateTier.Foreground = BrushPurple;
                rateTierBadge.BorderBrush = BrushPurpleDark;
            }
            else if (currentHz >= 800)
            {
                txtRateTier.Text = "1000 Hz (1K)";
                txtRateTier.Foreground = BrushPurpleDark;
                rateTierBadge.BorderBrush = BrushViolet;
            }
            else if (currentHz > 0)
            {
                txtRateTier.Text = $"{currentHz} Hz";
                txtRateTier.Foreground = BrushLightMuted;
                rateTierBadge.BorderBrush = BrushBorderIdle;
            }
            else
            {
                txtRateTier.Text = "MOVE MOUSE TO TEST";
                txtRateTier.Foreground = BrushMuted;
                rateTierBadge.BorderBrush = BrushBorderIdle;
            }

            if (peakHz >= 9000)
            {
                txtTargetStatus.Text = "10K ULTRA";
                txtTargetStatus.Foreground = BrushEmerald;
            }
            else if (peakHz >= 6800)
            {
                txtTargetStatus.Text = "8K ULTRA";
                txtTargetStatus.Foreground = BrushGreen;
            }
            else if (peakHz >= 3400)
            {
                txtTargetStatus.Text = "4K";
                txtTargetStatus.Foreground = BrushSky;
            }
            else if (peakHz >= 1700)
            {
                txtTargetStatus.Text = "2K";
                txtTargetStatus.Foreground = BrushPurple;
            }
            else if (peakHz >= 800)
            {
                txtTargetStatus.Text = "1K";
                txtTargetStatus.Foreground = BrushPurpleDark;
            }
            else if (peakHz > 0)
            {
                txtTargetStatus.Text = "ACTIVE";
                txtTargetStatus.Foreground = BrushLightMuted;
            }
        }

        private void OnClickRecorded(string btnName, double intervalMs, bool isFastOrDouble)
        {
            Dispatcher.BeginInvoke(() =>
            {
                txtClickCount.Text = _rawInput.ClickCount.ToString();
                txtDoubleClickCount.Text = _rawInput.DoubleClickCount.ToString();

                if (intervalMs > 0)
                {
                    txtLastInterval.Text = $"{intervalMs:F1} ms";
                    if (isFastOrDouble)
                    {
                        txtClickFeedback.Text = $"⚠️ Double-click detected ({btnName} • {intervalMs:F1}ms)";
                        txtClickFeedback.Foreground = BrushRed;
                        statusBanner.Background = BrushRedBg;
                    }
                    else
                    {
                        txtClickFeedback.Text = $"Click ({btnName} • {intervalMs:F1}ms)";
                        txtClickFeedback.Foreground = BrushPurple;
                        statusBanner.Background = BrushIdleBg;
                    }
                }
                else
                {
                    txtLastInterval.Text = "-- ms";
                    txtClickFeedback.Text = $"Click ({btnName})";
                    txtClickFeedback.Foreground = BrushPurple;
                    statusBanner.Background = BrushIdleBg;
                }
            });
        }

        private void OnButtonStateChanged(string btnName, bool isPressed)
        {
            Dispatcher.BeginInvoke(() =>
            {
                Border? targetPill = btnName switch
                {
                    "Left" => pillLeft,
                    "Middle" => pillMiddle,
                    "Right" => pillRight,
                    "Side1" => pillSide1,
                    "Side2" => pillSide2,
                    _ => null
                };

                if (targetPill != null)
                {
                    targetPill.Background = isPressed ? BrushActivePillBg : BrushIdlePillBg;
                    if (targetPill.Child is TextBlock tb)
                    {
                        tb.Foreground = isPressed ? BrushActivePillText : BrushIdlePillText;
                    }
                }
            });
        }
    }
}
