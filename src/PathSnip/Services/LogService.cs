using System;
using System.IO;

namespace PathSnip.Services
{
    public static class LogService
    {
        private static readonly string LogDirectory;
        private static readonly string LogFilePath;

        static LogService()
        {
            LogDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PathSnip",
                "logs");

            Directory.CreateDirectory(LogDirectory);

            LogFilePath = Path.Combine(LogDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");
        }

        public static void Log(string message)
        {
            try
            {
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, logEntry);
            }
            catch
            {
                // 忽略日志写入失败
            }
        }

        public static void LogError(string message, Exception ex)
        {
            Log($"ERROR: {message} - {ex.Message}\n{ex.StackTrace}");
        }
    }
}
