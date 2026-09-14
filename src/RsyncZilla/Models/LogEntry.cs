using System;

namespace RsyncZilla.Models
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Message { get; set; } = string.Empty;
        public bool IsError { get; set; }

        public string FormattedTime => Timestamp.ToString("HH:mm:ss");
        public string ColorBrush => IsError ? "#C62828" : "#2E7D32"; // Dark Red or Dark Green for light theme
    }
}
