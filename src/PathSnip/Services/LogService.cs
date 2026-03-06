using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace PathSnip.Services
{
    public static class LogService
    {
        private sealed class LogItem
        {
            public LogItem(DateTime timestamp, string text)
            {
                Timestamp = timestamp;
                Text = text;
            }

            public DateTime Timestamp { get; }
            public string Text { get; }
        }

        private const int LogQueueCapacity = 2000;
        private const int BatchSize = 20;
        private const int FlushIntervalMs = 100;
        private static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(2);

        private static readonly string LogDirectory;
        private static readonly BlockingCollection<LogItem> LogQueue =
            new BlockingCollection<LogItem>(new ConcurrentQueue<LogItem>(), LogQueueCapacity);
        private static readonly Thread LogWorkerThread;

        static LogService()
        {
            LogDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PathSnip",
                "logs");

            Directory.CreateDirectory(LogDirectory);

            LogWorkerThread = new Thread(ProcessLogQueue)
            {
                IsBackground = true,
                Name = "PathSnipLogWorker"
            };
            LogWorkerThread.Start();

            AppDomain.CurrentDomain.ProcessExit += (_, __) => ShutdownLogWorker();
        }

        public static void Log(string message)
        {
            try
            {
                DateTime now = DateTime.Now;
                var item = new LogItem(now, $"[{now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");

                if (!LogQueue.TryAdd(item))
                {
                    LogQueue.TryTake(out _);
                    LogQueue.TryAdd(item);
                }
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

        private static string GetLogFilePath(DateTime timestamp)
        {
            return Path.Combine(LogDirectory, $"{timestamp:yyyy-MM-dd}.log");
        }

        private static void ProcessLogQueue()
        {
            var batch = new List<LogItem>(BatchSize);
            DateTime lastFlushAt = DateTime.UtcNow;

            try
            {
                while (!LogQueue.IsCompleted)
                {
                    if (LogQueue.TryTake(out LogItem item, FlushIntervalMs))
                    {
                        batch.Add(item);
                    }

                    bool shouldFlushBySize = batch.Count >= BatchSize;
                    bool shouldFlushByTime = batch.Count > 0 &&
                        (DateTime.UtcNow - lastFlushAt).TotalMilliseconds >= FlushIntervalMs;

                    if (shouldFlushBySize || shouldFlushByTime)
                    {
                        WriteBatch(batch);
                        batch.Clear();
                        lastFlushAt = DateTime.UtcNow;
                    }
                }

                while (LogQueue.TryTake(out LogItem remainingItem))
                {
                    batch.Add(remainingItem);
                    if (batch.Count >= BatchSize)
                    {
                        WriteBatch(batch);
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    WriteBatch(batch);
                }
            }
            catch
            {
            }
        }

        private static void WriteBatch(List<LogItem> batch)
        {
            if (batch.Count == 0)
            {
                return;
            }

            string currentPath = null;
            var builder = new StringBuilder();

            foreach (var item in batch)
            {
                string itemPath = GetLogFilePath(item.Timestamp);
                if (currentPath == null)
                {
                    currentPath = itemPath;
                }

                if (!string.Equals(currentPath, itemPath, StringComparison.Ordinal))
                {
                    File.AppendAllText(currentPath, builder.ToString());
                    builder.Clear();
                    currentPath = itemPath;
                }

                builder.Append(item.Text);
            }

            if (builder.Length > 0 && currentPath != null)
            {
                File.AppendAllText(currentPath, builder.ToString());
            }
        }

        private static void ShutdownLogWorker()
        {
            try
            {
                LogQueue.CompleteAdding();
                LogWorkerThread.Join(ShutdownFlushTimeout);
            }
            catch
            {
            }
        }
    }
}
