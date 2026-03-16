using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PathSnip.Services
{
    public static class ClipboardService
    {
        private sealed class ClipboardWorkItem
        {
            public ClipboardWorkItem(Action action, TaskCompletionSource<bool> completion, string operationId, string actionName)
            {
                Action = action;
                Completion = completion;
                OperationId = operationId;
                ActionName = actionName;
            }

            public Action Action { get; }
            public TaskCompletionSource<bool> Completion { get; }
            public string OperationId { get; }
            public string ActionName { get; }
        }

        private const int ClipboardCantOpenHResult = unchecked((int)0x800401D0);
        private const int WinFormsTextRetryTimes = 12;
        private const int WinFormsTextRetryDelayMs = 120;
        private const int WinFormsImageAndPathRetryTimes = 15;
        private const int WinFormsImageAndPathRetryDelayMs = 120;
        private static readonly int[] FastRetryDelaysMs = { 50, 120, 250 };
        private static readonly int[] SlowCantOpenRetryDelaysMs = { 1000, 2000, 5000 };
        private const int ClipboardQueueCapacity = 512;

        private static readonly object SetTextCoalesceLock = new object();
        private static string _pendingText;
        private static string _pendingTextOperationId;
        private static int _pendingTextCoalescedCount;
        private static long _pendingTextVersion;
        private static TaskCompletionSource<bool> _pendingTextCompletion;

        private static readonly BlockingCollection<ClipboardWorkItem> ClipboardQueue =
            new BlockingCollection<ClipboardWorkItem>(new ConcurrentQueue<ClipboardWorkItem>(), ClipboardQueueCapacity);

        static ClipboardService()
        {
            var workerThread = new Thread(ProcessClipboardQueue)
            {
                IsBackground = true,
                Name = "ClipboardServiceWorker"
            };

            workerThread.SetApartmentState(ApartmentState.STA);
            workerThread.Start();
        }

        public static Task<bool> TrySetTextAsync(string text, string operationId = null)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                operationId = LogService.CreateOperationId("clip");
            }

            return TrySetTextCoalescedAsync(text, operationId);
        }

        public static Task<bool> TrySetImageAsync(BitmapSource bitmap, string operationId = null)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                operationId = LogService.CreateOperationId("clip");
            }
            var safeBitmap = CloneFrozenBitmap(bitmap);
            return RunStaClipboardActionAsync(() => SetImage(safeBitmap, operationId), operationId, "image");
        }

        public static Task<bool> TrySetImageAndPathAsync(BitmapSource bitmap, string path, string operationId = null)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                operationId = LogService.CreateOperationId("clip");
            }
            var safeBitmap = CloneFrozenBitmap(bitmap);
            return RunStaClipboardActionAsync(() => SetImageAndPath(safeBitmap, path, operationId), operationId, "image+path");
        }

        public static bool TrySetTextOnCurrentThreadOnce(string text, string operationId = null)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                operationId = LogService.CreateOperationId("clip");
            }

            try
            {
                WriteTextWithWinFormsRetry(text);
                LogService.LogInfo(
                    "clipboard.set_text.ui_fallback.success",
                    $"textLength={text?.Length ?? 0} api=winforms retryTimes={WinFormsTextRetryTimes} retryDelayMs={WinFormsTextRetryDelayMs}",
                    operationId,
                    "clipboard.ui_fallback");
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogException("clipboard.set_text.ui_fallback.failed", ex, $"textLength={text?.Length ?? 0}", operationId, "clipboard.ui_fallback");
                return false;
            }
        }

        public static bool TrySetImageOnCurrentThreadOnce(BitmapSource bitmap, string operationId = null)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                operationId = LogService.CreateOperationId("clip");
            }

            try
            {
                var safeBitmap = CloneFrozenBitmap(bitmap);
                var dataObject = new DataObject();
                dataObject.SetData(DataFormats.Bitmap, safeBitmap);
                Clipboard.SetDataObject(dataObject, true);
                LogService.LogInfo("clipboard.set_image.ui_fallback.success", $"pixel={safeBitmap?.PixelWidth ?? 0}x{safeBitmap?.PixelHeight ?? 0}", operationId, "clipboard.ui_fallback");
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogException("clipboard.set_image.ui_fallback.failed", ex, $"pixel={bitmap?.PixelWidth ?? 0}x{bitmap?.PixelHeight ?? 0}", operationId, "clipboard.ui_fallback");
                return false;
            }
        }

        public static bool TrySetImageAndPathOnCurrentThreadOnce(BitmapSource bitmap, string path, string operationId = null)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                operationId = LogService.CreateOperationId("clip");
            }

            try
            {
                var safeBitmap = CloneFrozenBitmap(bitmap);
                WriteImageAndPathWithWinFormsRetry(safeBitmap, path);
                LogService.LogInfo(
                    "clipboard.set_image_path.ui_fallback.success",
                    $"pixel={safeBitmap?.PixelWidth ?? 0}x{safeBitmap?.PixelHeight ?? 0} pathLength={path?.Length ?? 0} api=winforms retryTimes={WinFormsImageAndPathRetryTimes} retryDelayMs={WinFormsImageAndPathRetryDelayMs}",
                    operationId,
                    "clipboard.ui_fallback");
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogException("clipboard.set_image_path.ui_fallback.failed", ex, $"pixel={bitmap?.PixelWidth ?? 0}x{bitmap?.PixelHeight ?? 0} pathLength={path?.Length ?? 0}", operationId, "clipboard.ui_fallback");
                return false;
            }
        }

        private static BitmapSource CloneFrozenBitmap(BitmapSource bitmap)
        {
            if (bitmap == null)
                return null;

            if (bitmap.IsFrozen)
                return bitmap;

            var clone = bitmap.Clone();
            clone.Freeze();
            return clone;
        }

        private static Task<bool> RunStaClipboardActionAsync(Action action, string operationId, string actionName)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            EnqueueClipboardWorkItem(new ClipboardWorkItem(action, tcs, operationId, actionName));

            return tcs.Task;
        }

        private static Task<bool> TrySetTextCoalescedAsync(string text, string operationId)
        {
            TaskCompletionSource<bool> completion;
            bool shouldEnqueue = false;

            lock (SetTextCoalesceLock)
            {
                _pendingText = text;
                _pendingTextOperationId = operationId;
                _pendingTextVersion++;

                if (_pendingTextCompletion == null || _pendingTextCompletion.Task.IsCompleted)
                {
                    _pendingTextCoalescedCount = 0;
                    _pendingTextCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    shouldEnqueue = true;
                }
                else
                {
                    _pendingTextCoalescedCount++;
                }

                completion = _pendingTextCompletion;
            }

            if (shouldEnqueue)
            {
                EnqueueClipboardWorkItem(new ClipboardWorkItem(
                    () => DrainPendingTextWrites(completion),
                    completion,
                    operationId,
                    "text"));
            }

            return completion.Task;
        }

        private static void DrainPendingTextWrites(TaskCompletionSource<bool> completion)
        {
            bool lastResult = false;
            bool wasBusy = false;
            long handledVersion = 0;

            while (true)
            {
                string text;
                string operationId;
                int coalesced;
                long version;

                lock (SetTextCoalesceLock)
                {
                    if (!ReferenceEquals(_pendingTextCompletion, completion))
                    {
                        completion.TrySetResult(false);
                        return;
                    }

                    text = _pendingText;
                    operationId = _pendingTextOperationId;
                    coalesced = _pendingTextCoalescedCount;
                    _pendingTextCoalescedCount = 0;
                    version = _pendingTextVersion;
                }

                if (coalesced > 0)
                {
                    LogService.LogInfo(
                        "clipboard.set_text.coalesced",
                        $"count={coalesced} textLength={text?.Length ?? 0}",
                        operationId,
                        "clipboard.coalesce");
                }

                lastResult = TryWriteTextWithCircuit(text, operationId, ref wasBusy);
                handledVersion = version;

                lock (SetTextCoalesceLock)
                {
                    if (!ReferenceEquals(_pendingTextCompletion, completion))
                    {
                        completion.TrySetResult(false);
                        return;
                    }

                    if (_pendingTextVersion == handledVersion)
                    {
                        _pendingText = null;
                        _pendingTextOperationId = null;
                        _pendingTextCompletion = null;
                        completion.TrySetResult(lastResult);
                        return;
                    }
                }
            }
        }

        private static bool TryWriteTextWithCircuit(string text, string operationId, ref bool wasBusy)
        {
            const int maxTotalMs = 8000;
            var watch = Stopwatch.StartNew();

            int attempt = 0;
            while (watch.ElapsedMilliseconds < maxTotalMs)
            {
                try
                {
                    WriteTextWithWinFormsRetry(text);
                    LogService.LogInfo("clipboard.set_text.success", $"textLength={text?.Length ?? 0}", operationId, "clipboard.write");

                    if (wasBusy)
                    {
                        LogService.LogInfo("clipboard.set_text.circuit_recovered", "clipboard recovered", operationId, "clipboard.circuit");
                        wasBusy = false;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    if (!IsClipboardCantOpen(ex))
                    {
                        LogService.LogException("clipboard.set_text.failed", ex, $"attempt={attempt + 1}", operationId, "clipboard.write");
                        return false;
                    }

                    wasBusy = true;
                    int delayMs = ResolveCantOpenCooldownMs(attempt);
                    LogService.LogWarn(
                        "clipboard.set_text.circuit_open",
                        $"attempt={attempt + 1} delayMs={delayMs} hresult=0x{((uint)ex.HResult):X8} textLength={text?.Length ?? 0} queue={ClipboardQueue.Count}",
                        operationId,
                        "clipboard.circuit");

                    Thread.Sleep(delayMs);
                    attempt++;
                }
            }

            LogService.LogWarn("clipboard.set_text.failed", $"timeoutMs={maxTotalMs} textLength={text?.Length ?? 0}", operationId, "clipboard.write");
            return false;
        }

        private static int ResolveCantOpenCooldownMs(int attempt)
        {
            if (attempt <= 0) return 120;
            if (attempt == 1) return 250;
            if (attempt == 2) return 500;
            if (attempt == 3) return 1000;
            if (attempt == 4) return 1500;
            return 2000;
        }

        private static void EnqueueClipboardWorkItem(ClipboardWorkItem workItem)
        {
            try
            {
                if (!ClipboardQueue.TryAdd(workItem))
                {
                    LogService.LogWarn("clipboard.queue.full", $"剪贴板队列已满 action={workItem.ActionName}", workItem.OperationId, "queue.add");
                    workItem.Completion.TrySetResult(false);

                    if (workItem.ActionName == "text")
                    {
                        lock (SetTextCoalesceLock)
                        {
                            if (ReferenceEquals(_pendingTextCompletion, workItem.Completion))
                            {
                                _pendingText = null;
                                _pendingTextOperationId = null;
                                _pendingTextCompletion = null;
                                _pendingTextCoalescedCount = 0;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogException("clipboard.queue.enqueue_failed", ex, $"入队失败 action={workItem.ActionName}", workItem.OperationId, "queue.add");
                workItem.Completion.TrySetResult(false);
            }
        }

        private static void ProcessClipboardQueue()
        {
            foreach (var workItem in ClipboardQueue.GetConsumingEnumerable())
            {
                try
                {
                    workItem.Action();
                    workItem.Completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    LogService.LogException("clipboard.worker.failed", ex, $"异步剪贴板操作失败 action={workItem.ActionName}", workItem.OperationId, "worker.execute");
                    workItem.Completion.TrySetResult(false);
                }
            }
        }

        private static void SetText(string text, string operationId)
        {
            ExecuteClipboardWrite(
                operationId,
                "clipboard.set_text.success",
                $"textLength={text?.Length ?? 0}",
                "clipboard.set_text.retry",
                "clipboard.set_text.failed",
                () =>
                {
                    WriteTextWithWinFormsRetry(text);
                });
        }

        private static void WriteTextWithWinFormsRetry(string text)
        {
            System.Windows.Forms.Clipboard.SetDataObject(
                text,
                true,
                WinFormsTextRetryTimes,
                WinFormsTextRetryDelayMs);
        }

        private static void SetImage(BitmapSource bitmap, string operationId)
        {
            ExecuteClipboardWrite(
                operationId,
                "clipboard.set_image.success",
                $"pixel={bitmap?.PixelWidth ?? 0}x{bitmap?.PixelHeight ?? 0}",
                "clipboard.set_image.retry",
                "clipboard.set_image.failed",
                () =>
                {
                    var dataObject = new DataObject();
                    dataObject.SetData(DataFormats.Bitmap, bitmap);
                    Clipboard.SetDataObject(dataObject, true);
                },
                allowSlowCantOpenRetry: false);
        }

        private static void SetImageAndPath(BitmapSource bitmap, string path, string operationId)
        {
            ExecuteClipboardWrite(
                operationId,
                "clipboard.set_image_path.success",
                $"pixel={bitmap?.PixelWidth ?? 0}x{bitmap?.PixelHeight ?? 0} pathLength={path?.Length ?? 0}",
                "clipboard.set_image_path.retry",
                "clipboard.set_image_path.failed",
                () =>
                {
                    WriteImageAndPathWithWinFormsRetry(bitmap, path);
                });
        }

        private static void WriteImageAndPathWithWinFormsRetry(BitmapSource bitmap, string path)
        {
            using (var drawingBitmap = ConvertToDrawingBitmap(bitmap))
            {
                var dataObject = new System.Windows.Forms.DataObject();
                dataObject.SetData(System.Windows.Forms.DataFormats.Bitmap, true, drawingBitmap);

                string safePath = path ?? string.Empty;
                dataObject.SetData(System.Windows.Forms.DataFormats.UnicodeText, safePath);
                dataObject.SetData(System.Windows.Forms.DataFormats.Text, safePath);

                System.Windows.Forms.Clipboard.SetDataObject(
                    dataObject,
                    true,
                    WinFormsImageAndPathRetryTimes,
                    WinFormsImageAndPathRetryDelayMs);
            }
        }

        private static System.Drawing.Bitmap ConvertToDrawingBitmap(BitmapSource bitmap)
        {
            var safeBitmap = CloneFrozenBitmap(bitmap);
            if (safeBitmap == null)
            {
                throw new ArgumentNullException(nameof(bitmap));
            }

            using (var memoryStream = new MemoryStream())
            {
                var encoder = new BmpBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(safeBitmap));
                encoder.Save(memoryStream);
                memoryStream.Position = 0;

                using (var tempBitmap = new System.Drawing.Bitmap(memoryStream))
                {
                    return new System.Drawing.Bitmap(tempBitmap);
                }
            }
        }

        private static void ExecuteClipboardWrite(
            string operationId,
            string successEvent,
            string successMessage,
            string retryEvent,
            string failEvent,
            Action writeAction,
            bool allowSlowCantOpenRetry = true)
        {
            int attempt = 0;

            while (true)
            {
                try
                {
                    writeAction();
                    LogService.LogInfo(successEvent, successMessage, operationId, "clipboard.write");
                    return;
                }
                catch (Exception ex)
                {
                    if (!TryGetRetryDelay(ex, attempt, allowSlowCantOpenRetry, out int delayMs, out string phase))
                    {
                        LogService.LogException(failEvent, ex, $"attempt={attempt + 1}", operationId, "clipboard.write");
                        throw;
                    }

                    string retryMessage = $"attempt={attempt + 1} delayMs={delayMs} phase={phase} hresult=0x{((uint)ex.HResult):X8} queue={ClipboardQueue.Count}";
                    LogService.LogWarn(retryEvent, retryMessage, operationId, "clipboard.retry");
                    Thread.Sleep(delayMs);
                    attempt++;
                }
            }
        }

        private static bool TryGetRetryDelay(Exception ex, int attempt, bool allowSlowCantOpenRetry, out int delayMs, out string phase)
        {
            if (attempt < FastRetryDelaysMs.Length)
            {
                delayMs = FastRetryDelaysMs[attempt];
                phase = "fast";
                return true;
            }

            if (allowSlowCantOpenRetry && IsClipboardCantOpen(ex))
            {
                int slowRetryIndex = attempt - FastRetryDelaysMs.Length;
                if (slowRetryIndex < SlowCantOpenRetryDelaysMs.Length)
                {
                    delayMs = SlowCantOpenRetryDelaysMs[slowRetryIndex];
                    phase = "slow";
                    return true;
                }
            }

            delayMs = 0;
            phase = "none";
            return false;
        }

        private static bool IsClipboardCantOpen(Exception ex)
        {
            return ex.HResult == ClipboardCantOpenHResult;
        }

        public static string FormatPath(string path, string format)
        {
            var normalizedPath = path.Replace("\\", "/");
            switch (format)
            {
                case "Markdown":
                    return $"![截图]({normalizedPath})";
                case "HTML":
                    return $"<img src=\"file:///{normalizedPath}\"/>";
                default:
                    return path;
            }
        }
    }
}
