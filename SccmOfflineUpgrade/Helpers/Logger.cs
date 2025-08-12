using System;
using System.IO;

namespace SccmOfflineUpgrade
{
    internal enum LogLevel { INFO, WARN, ERROR, DEBUG }

    internal static class Logger
    {
        public static void Write(string logPath, string message, LogLevel level = LogLevel.INFO)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? ".");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
    }
}
