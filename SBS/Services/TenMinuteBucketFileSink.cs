using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using System;
using System.IO;
using System.Text;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>
    /// Пишет лог в файл с 10-минутным бакетом (округление времени вниз: 13:24 → 13:20).
    /// Путь: %LOCALAPPDATA%\SmartRemont\logs\yyyy-MM-dd\HH-mm.log
    /// </summary>
    public sealed class TenMinuteBucketFileSink : ILogEventSink
    {
        static readonly object Sync = new();
        static readonly MessageTemplateTextFormatter Formatter = new(
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

        readonly string _logRoot;

        public TenMinuteBucketFileSink(string logRoot) =>
            _logRoot = logRoot ?? throw new ArgumentNullException(nameof(logRoot));

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null)
                return;

            var bucket = GetBucketTime(logEvent.Timestamp.LocalDateTime);
            var dayDir = Path.Combine(_logRoot, bucket.ToString("yyyy-MM-dd"));
            var filePath = Path.Combine(dayDir, bucket.ToString("HH-mm") + ".log");

            lock (Sync)
            {
                Directory.CreateDirectory(dayDir);
                using var writer = new StreamWriter(filePath, append: true, Encoding.UTF8);
                Formatter.Format(logEvent, writer);
                writer.Flush();
            }
        }

        public static DateTime GetBucketTime(DateTime timestamp)
        {
            var minute = timestamp.Minute - (timestamp.Minute % 10);
            return new DateTime(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, minute, 0, timestamp.Kind);
        }

        public static string GetLogRootDirectory() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartRemont",
                "logs");
    }
}
