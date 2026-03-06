using System;
using System.Collections.Concurrent;
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
        private static readonly int[] FastRetryDelaysMs = { 50, 120, 250 };
        private static readonly int[] SlowCantOpenRetryDelaysMs = { 1000, 2000, 5000 };
        private const int ClipboardQueueCapacity = 512;

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
            return RunStaClipboardActionAsync(() => SetText(text, operationId), operationId, "text");
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

            try
            {
                if (!ClipboardQueue.TryAdd(new ClipboardWorkItem(action, tcs, operationId, actionName)))
                {
                    LogService.LogWarn("clipboard.queue.full", $"剪贴板队列已满 action={actionName}", operationId, "queue.add");
                    tcs.TrySetResult(false);
                }
            }
            catch (Exception ex)
            {
                LogService.LogException("clipboard.queue.enqueue_failed", ex, $"入队失败 action={actionName}", operationId, "queue.add");
                tcs.TrySetResult(false);
            }

            return tcs.Task;
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
                    var dataObject = new DataObject();
                    dataObject.SetData(DataFormats.Text, text);
                    Clipboard.SetDataObject(dataObject, true);
                });
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
                });
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
                    var dataObject = new DataObject();
                    dataObject.SetData(DataFormats.Bitmap, bitmap);
                    dataObject.SetData(DataFormats.Text, path);
                    Clipboard.SetDataObject(dataObject, true);
                });
        }

        private static void ExecuteClipboardWrite(
            string operationId,
            string successEvent,
            string successMessage,
            string retryEvent,
            string failEvent,
            Action writeAction)
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
                    if (!TryGetRetryDelay(ex, attempt, out int delayMs, out string phase))
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

        private static bool TryGetRetryDelay(Exception ex, int attempt, out int delayMs, out string phase)
        {
            if (attempt < FastRetryDelaysMs.Length)
            {
                delayMs = FastRetryDelaysMs[attempt];
                phase = "fast";
                return true;
            }

            if (IsClipboardCantOpen(ex))
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
