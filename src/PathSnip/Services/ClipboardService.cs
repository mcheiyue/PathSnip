using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PathSnip.Services
{
    public static class ClipboardService
    {
        public static void SetText(string text)
        {
            try
            {
                Clipboard.SetText(text);
                LogService.Log($"路径已复制到剪贴板: {text}");
            }
            catch (Exception ex)
            {
                LogService.LogError("复制到剪贴板失败", ex);
                throw;
            }
        }

        public static void SetImage(BitmapSource bitmap)
        {
            try
            {
                Clipboard.SetImage(bitmap);
                LogService.Log("图片已复制到剪贴板");
            }
            catch (Exception ex)
            {
                LogService.LogError("复制图片到剪贴板失败", ex);
                throw;
            }
        }

        public static void SetImageAndPath(BitmapSource bitmap, string path)
        {
            try
            {
                var dataObject = new DataObject();
                dataObject.SetData(DataFormats.Bitmap, bitmap);
                dataObject.SetData(DataFormats.Text, path);
                Clipboard.SetDataObject(dataObject, true);
                LogService.Log($"图片+路径已复制到剪贴板: {path}");
            }
            catch (Exception ex)
            {
                LogService.LogError("复制到剪贴板失败", ex);
                throw;
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
