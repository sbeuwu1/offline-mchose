using System;

namespace MchoseHub.Models
{
    public enum LogType
    {
        Info,
        Tx,
        Rx,
        Warn,
        Error
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string TimeStr => Timestamp.ToString("HH:mm:ss.fff");
        public LogType Type { get; set; }
        public string Message { get; set; } = string.Empty;

        public string ColorHex => Type switch
        {
            LogType.Tx => "#a855f7",    // Purple
            LogType.Rx => "#10b981",    // Green
            LogType.Warn => "#fbbf24",  // Amber
            LogType.Error => "#ef4444", // Red
            _ => "#38bdf8"             // Cyan / Info
        };

        public string DisplayText => $"[{TimeStr}] [{Type.ToString().ToUpperInvariant()}] {Message}";
    }
}
