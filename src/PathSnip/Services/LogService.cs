using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace PathSnip.Services
{
    public static class LogService
    {
        private enum LogLevel
        {
            Info,
            Warn,
            Error
        }

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
        private static readonly string AppSessionId = Guid.NewGuid().ToString("N").Substring(0, 12);

        private static readonly string LogDirectory;
        private static readonly string FallbackLogPath;
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
            FallbackLogPath = Path.Combine(LogDirectory, "logger-fallback.log");

            LogWorkerThread = new Thread(ProcessLogQueue)
            {
                IsBackground = true,
                Name = "PathSnipLogWorker"
            };
            LogWorkerThread.Start();

            AppDomain.CurrentDomain.ProcessExit += (_, __) => ShutdownLogWorker();
        }

        public static string CreateOperationId(string prefix = "op")
        {
            string normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? "op" : prefix;
            string fullId = string.Format(CultureInfo.InvariantCulture, "{0}-{1}", normalizedPrefix, Guid.NewGuid().ToString("N"));
            return fullId.Length > 15 ? fullId.Substring(0, 15) : fullId;
        }

        public static void LogInfo(string eventName, string message, string operationId = null, string stage = null)
        {
            EnqueueLog(LogLevel.Info, eventName, operationId, stage, message, null);
        }

        public static void LogWarn(string eventName, string message, string operationId = null, string stage = null)
        {
            EnqueueLog(LogLevel.Warn, eventName, operationId, stage, message, null);
        }

        public static void LogException(string eventName, Exception ex, string message = null, string operationId = null, string stage = null)
        {
            EnqueueLog(LogLevel.Error, eventName, operationId, stage, message, ex);
        }

        public static void Log(string message)
        {
            LogInfo("legacy.info", message);
        }

        public static void LogError(string message, Exception ex)
        {
            LogException("legacy.error", ex, message);
        }

        private static void EnqueueLog(LogLevel level, string eventName, string operationId, string stage, string message, Exception ex)
        {
            try
            {
                DateTime now = DateTime.Now;
                string line = FormatStructuredLog(now, level, eventName, operationId, stage, message, ex);
                var item = new LogItem(now, line + Environment.NewLine);

                if (!LogQueue.TryAdd(item))
                {
                    LogQueue.TryTake(out _);
                    LogQueue.TryAdd(item);
                }
            }
            catch (Exception enqueueEx)
            {
                TryWriteFallback($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] level=ERROR event=logger.enqueue_failed msg={EscapeForLog(enqueueEx.ToString())}{Environment.NewLine}");
            }
        }

        private static string FormatStructuredLog(DateTime timestamp, LogLevel level, string eventName, string operationId, string stage, string message, Exception ex)
        {
            int threadId = Environment.CurrentManagedThreadId;
            bool isUiThread = false;
            try
            {
                isUiThread = Application.Current?.Dispatcher?.CheckAccess() == true;
            }
            catch
            {
                isUiThread = false;
            }

            var builder = new StringBuilder();
            builder.Append("ts=").Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            builder.Append(" level=").Append(level.ToString().ToUpperInvariant());
            builder.Append(" session=").Append(AppSessionId);
            builder.Append(" thread=").Append(threadId.ToString(CultureInfo.InvariantCulture));
            builder.Append(" ui=").Append(isUiThread ? "1" : "0");
            builder.Append(" event=").Append(EscapeForLog(eventName));

            if (!string.IsNullOrWhiteSpace(operationId))
            {
                builder.Append(" op=").Append(EscapeForLog(operationId));
            }

            if (!string.IsNullOrWhiteSpace(stage))
            {
                builder.Append(" stage=").Append(EscapeForLog(stage));
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                builder.Append(" msg=").Append(EscapeForLog(message));
            }

            if (ex != null)
            {
                builder.Append(" exType=").Append(EscapeForLog(ex.GetType().FullName));
                builder.Append(" hresult=").Append(ex.HResult.ToString("X8", CultureInfo.InvariantCulture));
                builder.Append(" ex=").Append(EscapeForLog(ex.ToString()));
            }

            return builder.ToString();
        }

        private static string EscapeForLog(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "-";
            }

            return value
                .Replace('"', '\'')
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Trim();
        }

        private static void TryWriteFallback(string line)
        {
            try
            {
                File.AppendAllText(FallbackLogPath, line);
            }
            catch
            {
            }
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
                TryWriteFallback($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] level=ERROR event=logger.worker_failed{Environment.NewLine}");
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
                TryWriteFallback($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] level=ERROR event=logger.shutdown_failed{Environment.NewLine}");
            }
        }
    }
}
