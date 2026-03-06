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

        private static readonly int[] RetryDelaysMs = { 50, 120, 250 };
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
            for (int i = 0; i < RetryDelaysMs.Length; i++)
            {
                try
                {
                    var dataObject = new DataObject();
                    dataObject.SetData(DataFormats.Text, text);
                    Clipboard.SetDataObject(dataObject, true);
                    LogService.LogInfo("clipboard.set_text.success", $"textLength={text?.Length ?? 0}", operationId, "clipboard.write");
                    return;
                }
                catch (Exception ex)
                {
                    if (i == RetryDelaysMs.Length - 1)
                    {
                        LogService.LogException("clipboard.set_text.failed", ex, "复制文本到剪贴板失败", operationId, "clipboard.write");
                        throw;
                    }

                    int delay = RetryDelaysMs[i];
                    LogService.LogWarn("clipboard.set_text.retry", $"attempt={i + 1} delayMs={delay}", operationId, "clipboard.retry");
                    Thread.Sleep(delay);
                }
            }
        }

        private static void SetImage(BitmapSource bitmap, string operationId)
        {
            for (int i = 0; i < RetryDelaysMs.Length; i++)
            {
                try
                {
                    var dataObject = new DataObject();
                    dataObject.SetData(DataFormats.Bitmap, bitmap);
                    Clipboard.SetDataObject(dataObject, true);
                    LogService.LogInfo("clipboard.set_image.success", $"pixel={bitmap?.PixelWidth ?? 0}x{bitmap?.PixelHeight ?? 0}", operationId, "clipboard.write");
                    return;
                }
                catch (Exception ex)
                {
                    if (i == RetryDelaysMs.Length - 1)
                    {
                        LogService.LogException("clipboard.set_image.failed", ex, "复制图片到剪贴板失败", operationId, "clipboard.write");
                        throw;
                    }

                    int delay = RetryDelaysMs[i];
                    LogService.LogWarn("clipboard.set_image.retry", $"attempt={i + 1} delayMs={delay}", operationId, "clipboard.retry");
                    Thread.Sleep(delay);
                }
            }
        }

        private static void SetImageAndPath(BitmapSource bitmap, string path, string operationId)
        {
            for (int i = 0; i < RetryDelaysMs.Length; i++)
            {
                try
                {
                    var dataObject = new DataObject();
                    dataObject.SetData(DataFormats.Bitmap, bitmap);
                    dataObject.SetData(DataFormats.Text, path);

                    Clipboard.SetDataObject(dataObject, true);

                    LogService.LogInfo("clipboard.set_image_path.success", $"pixel={bitmap?.PixelWidth ?? 0}x{bitmap?.PixelHeight ?? 0} pathLength={path?.Length ?? 0}", operationId, "clipboard.write");
                    return;
                }
                catch (Exception ex)
                {
                    if (i == RetryDelaysMs.Length - 1)
                    {
                        LogService.LogException("clipboard.set_image_path.failed", ex, "复制图片和路径到剪贴板失败", operationId, "clipboard.write");
                        throw;
                    }

                    int delay = RetryDelaysMs[i];
                    LogService.LogWarn("clipboard.set_image_path.retry", $"attempt={i + 1} delayMs={delay}", operationId, "clipboard.retry");
                    Thread.Sleep(delay);
                }
            }
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
