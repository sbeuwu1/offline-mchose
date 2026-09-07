using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MchoseHub.Models
{
    public class MouseState : INotifyPropertyChanged
    {
        public static readonly int[] PollingRates = { 125, 500, 1000, 2000, 4000, 8000 };
        public static readonly string[] StageColors = { "#ff250e", "#0066ff", "#00cc66", "#ffcc00", "#00cccc", "#e040fb" };

        private bool _isConnected;
        private string _productName = "MCHOSE A7 V2 Ultra / Pro";
        private ushort _vendorId = 0x3837;
        private ushort _productId = 0x100B;
        private int _batteryLevel = -1;
        private bool _isCharging;
        private int _gameMode = 2; // 1: Eco/Performance, 2: Gaming, 3: Extreme
        private int _rateIndex = 3; // 2000Hz default
        private int _lodIndex = 1; // 1.0mm default
        private bool _ripple;
        private bool _angleSnap;
        private bool _motionSync;
        private int _rotateVal; // -30 to +30
        private int _debounceMs = 8;
        private int _sleepMinutes = 3;
        private bool _neverSleep;
        private int _numLevels = 5;
        private int _activeDpiIndex;
        private int[] _dpiStages = { 800, 800, 1600, 3200, 6400, 12800 };

        public bool IsConnected
        {
            get => _isConnected;
            set => SetField(ref _isConnected, value);
        }

        public string ProductName
        {
            get => _productName;
            set => SetField(ref _productName, value);
        }

        public ushort VendorId
        {
            get => _vendorId;
            set => SetField(ref _vendorId, value);
        }

        public ushort ProductId
        {
            get => _productId;
            set => SetField(ref _productId, value);
        }

        public string VidPidString => $"VID: 0x{VendorId:X4} • PID: 0x{ProductId:X4}";

        public int BatteryLevel
        {
            get => _batteryLevel;
            set
            {
                if (SetField(ref _batteryLevel, value))
                {
                    OnPropertyChanged(nameof(BatteryText));
                    OnPropertyChanged(nameof(BatteryWidth));
                    OnPropertyChanged(nameof(BatteryColorHex));
                }
            }
        }

        public bool IsCharging
        {
            get => _isCharging;
            set
            {
                if (SetField(ref _isCharging, value))
                {
                    OnPropertyChanged(nameof(BatteryText));
                }
            }
        }

        public string BatteryText => BatteryLevel >= 0 
            ? $"{BatteryLevel}%{(IsCharging ? " ⚡" : "")}" 
            : "--%";

        public double BatteryWidth => BatteryLevel >= 0 ? System.Math.Clamp(BatteryLevel, 5, 100) : 0;

        public string BatteryColorHex => BatteryLevel switch
        {
            <= 20 => "#ef4444", // Red
            <= 40 => "#f59e0b", // Amber
            _ => "#00e5ff"      // Cyan
        };

        public int GameMode
        {
            get => _gameMode;
            set => SetField(ref _gameMode, value);
        }

        public int RateIndex
        {
            get => _rateIndex;
            set
            {
                if (SetField(ref _rateIndex, value))
                {
                    OnPropertyChanged(nameof(ActiveHzText));
                }
            }
        }

        public int ActiveHz => (RateIndex >= 0 && RateIndex < PollingRates.Length) ? PollingRates[RateIndex] : 2000;
        public string ActiveHzText => $"{ActiveHz} Hz";

        public int LodIndex
        {
            get => _lodIndex;
            set => SetField(ref _lodIndex, value);
        }

        public bool Ripple
        {
            get => _ripple;
            set => SetField(ref _ripple, value);
        }

        public bool AngleSnap
        {
            get => _angleSnap;
            set => SetField(ref _angleSnap, value);
        }

        public bool MotionSync
        {
            get => _motionSync;
            set => SetField(ref _motionSync, value);
        }

        public int RotateVal
        {
            get => _rotateVal;
            set => SetField(ref _rotateVal, System.Math.Clamp(value, -30, 30));
        }

        public int DebounceMs
        {
            get => _debounceMs;
            set => SetField(ref _debounceMs, System.Math.Clamp(value, 1, 20));
        }

        public int SleepMinutes
        {
            get => _sleepMinutes;
            set => SetField(ref _sleepMinutes, System.Math.Clamp(value, 1, 60));
        }

        public bool NeverSleep
        {
            get => _neverSleep;
            set => SetField(ref _neverSleep, value);
        }

        public int NumLevels
        {
            get => _numLevels;
            set
            {
                if (SetField(ref _numLevels, System.Math.Clamp(value, 1, 6)))
                {
                    if (ActiveDpiIndex >= _numLevels)
                    {
                        ActiveDpiIndex = _numLevels - 1;
                    }
                }
            }
        }

        public int ActiveDpiIndex
        {
            get => _activeDpiIndex;
            set
            {
                if (SetField(ref _activeDpiIndex, System.Math.Clamp(value, 0, NumLevels - 1)))
                {
                    OnPropertyChanged(nameof(ActiveDpiText));
                }
            }
        }

        public int[] DpiStages
        {
            get => _dpiStages;
            set
            {
                if (SetField(ref _dpiStages, value))
                {
                    OnPropertyChanged(nameof(ActiveDpiText));
                }
            }
        }

        public int ActiveDpiValue => (ActiveDpiIndex >= 0 && ActiveDpiIndex < DpiStages.Length) ? DpiStages[ActiveDpiIndex] : 800;
        public string ActiveDpiText => $"Stage {ActiveDpiIndex + 1}: {ActiveDpiValue} DPI";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
