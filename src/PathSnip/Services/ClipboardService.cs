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
            public ClipboardWorkItem(Action action, TaskCompletionSource<bool> completion)
            {
                Action = action;
                Completion = completion;
            }

            public Action Action { get; }
            public TaskCompletionSource<bool> Completion { get; }
        }

        private static readonly BlockingCollection<ClipboardWorkItem> ClipboardQueue =
            new BlockingCollection<ClipboardWorkItem>(new ConcurrentQueue<ClipboardWorkItem>());

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

        public static Task<bool> TrySetTextAsync(string text)
        {
            return RunStaClipboardActionAsync(() => SetText(text));
        }

        public static Task<bool> TrySetImageAsync(BitmapSource bitmap)
        {
            var safeBitmap = CloneFrozenBitmap(bitmap);
            return RunStaClipboardActionAsync(() => SetImage(safeBitmap));
        }

        public static Task<bool> TrySetImageAndPathAsync(BitmapSource bitmap, string path)
        {
            var safeBitmap = CloneFrozenBitmap(bitmap);
            return RunStaClipboardActionAsync(() => SetImageAndPath(safeBitmap, path));
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

        private static Task<bool> RunStaClipboardActionAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                ClipboardQueue.Add(new ClipboardWorkItem(action, tcs));
            }
            catch (Exception ex)
            {
                LogService.LogError("队列化剪贴板操作失败", ex);
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
                    LogService.LogError("异步剪贴板操作失败", ex);
                    workItem.Completion.TrySetResult(false);
                }
            }
        }

        private static void SetText(string text)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var dataObject = new DataObject();
                    dataObject.SetData(DataFormats.Text, text);
                    Clipboard.SetDataObject(dataObject, false);
                    
                    LogService.Log($"路径已复制到剪贴板: {text}");
                    return;
                }
                catch (Exception ex)
                {
                    if (i == 2)
                    {
                        LogService.LogError("复制到剪贴板失败", ex);
                        throw;
                    }
                    Thread.Sleep(50);
                }
            }
        }

        private static void SetImage(BitmapSource bitmap)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var dataObject = new DataObject();
                    dataObject.SetData(DataFormats.Bitmap, bitmap);
                    Clipboard.SetDataObject(dataObject, false);
                    
                    LogService.Log("图片已复制到剪贴板");
                    return;
                }
                catch (Exception ex)
                {
                    if (i == 2)
                    {
                        LogService.LogError("复制图片到剪贴板失败", ex);
                        throw;
                    }
                    Thread.Sleep(50);
                }
            }
        }

        private static void SetImageAndPath(BitmapSource bitmap, string path)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var dataObject = new DataObject();
                    dataObject.SetData(DataFormats.Bitmap, bitmap);
                    dataObject.SetData(DataFormats.Text, path);
                    
                    Clipboard.SetDataObject(dataObject, false);
                    
                    LogService.Log($"图片+路径已复制到剪贴板: {path}");
                    return;
                }
                catch (Exception ex)
                {
                    if (i == 2)
                    {
                        LogService.LogError("复制到剪贴板失败", ex);
                        throw;
                    }
                    Thread.Sleep(50);
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
