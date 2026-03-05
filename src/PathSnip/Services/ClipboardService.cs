using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PathSnip.Services
{
    public static class ClipboardService
    {
        public static Task<bool> TrySetTextAsync(string text)
        {
            return RunStaClipboardActionAsync(() => SetText(text));
        }

        public static Task<bool> TrySetImageAsync(BitmapSource bitmap)
        {
            return RunStaClipboardActionAsync(() => SetImage(bitmap));
        }

        public static Task<bool> TrySetImageAndPathAsync(BitmapSource bitmap, string path)
        {
            return RunStaClipboardActionAsync(() => SetImageAndPath(bitmap, path));
        }

        private static Task<bool> RunStaClipboardActionAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();

            var thread = new Thread(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    LogService.LogError("异步剪贴板操作失败", ex);
                    tcs.TrySetResult(false);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            return tcs.Task;
        }

        public static void SetText(string text)
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
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        public static void SetImage(BitmapSource bitmap)
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
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        public static void SetImageAndPath(BitmapSource bitmap, string path)
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
                    System.Threading.Thread.Sleep(50);
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
