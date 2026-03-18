using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using PathSnip.Services;

namespace PathSnip
{
    public partial class MainWindow
    {
        private static readonly int[] UiFallbackPathRetryDelaysMs = { 120, 250, 500, 1000, 1500 };
        private static readonly int[] UiFallbackImageRetryDelaysMs = { 120, 300 };
        private static readonly int[] UiFallbackImageAndPathRetryDelaysMs = { 120, 250, 500, 1000, 2000 };

        private void StartClipboardWriteAfterSave(
            System.Windows.Media.Imaging.BitmapSource bitmap,
            string filePath,
            ClipboardMode clipboardMode,
            string pathFormat,
            string markdownHtmlCopyMode,
            bool showNotification,
            string operationId,
            bool isCompositeCapture)
        {
            _ = WriteClipboardAfterSaveAsync(bitmap, filePath, clipboardMode, pathFormat, markdownHtmlCopyMode, showNotification, operationId, isCompositeCapture);
        }

        private async Task WriteClipboardAfterSaveAsync(
            System.Windows.Media.Imaging.BitmapSource bitmap,
            string filePath,
            ClipboardMode clipboardMode,
            string pathFormat,
            string markdownHtmlCopyMode,
            bool showNotification,
            string operationId,
            bool isCompositeCapture)
        {
            var clipboardWatch = Stopwatch.StartNew();

            try
            {
                bool copied;
                string successMessage;
                string failedMessage;
                string stage;

                switch (clipboardMode)
                {
                    case ClipboardMode.ImageOnly:
                        copied = await ClipboardService.TrySetImageAsync(bitmap, operationId);
                        successMessage = "已保存，图片已复制";
                        failedMessage = "复制图片失败（文件已保存，可能被其他程序占用）";
                        stage = "clipboard.image";
                        break;
                    case ClipboardMode.ImageAndPath:
                        var plainPathForImageAndPath = GetPlainPath(filePath);
                        copied = await ClipboardService.TrySetImageAndPathAsync(bitmap, plainPathForImageAndPath, operationId);
                        successMessage = "已保存，图片和路径已复制";
                        failedMessage = "复制图片和路径失败（文件已保存，可能被其他程序占用）";
                        stage = "clipboard.image_path";
                        break;
                    default:
                        var formattedPath = ClipboardService.FormatPath(filePath, pathFormat, markdownHtmlCopyMode);
                        copied = await ClipboardService.TrySetTextAsync(formattedPath, operationId);
                        successMessage = "已保存，路径已复制";
                        failedMessage = "复制路径失败（文件已保存，可能被其他程序占用）";
                        stage = "clipboard.path";
                        break;
                }

                clipboardWatch.Stop();
                if (copied)
                {
                    LogService.LogInfo("capture.clipboard.completed", $"elapsedMs={clipboardWatch.ElapsedMilliseconds}", operationId, stage);

                    if (showNotification)
                    {
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            TrayIcon.ShowBalloonTip("PathSnip", successMessage, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                        }));
                    }

                    return;
                }

                bool uiFallbackCopied = await TryClipboardWriteOnUiThreadWithRetryAsync(
                    clipboardMode,
                    bitmap,
                    filePath,
                    pathFormat,
                    markdownHtmlCopyMode,
                    operationId,
                    stage);
                if (uiFallbackCopied)
                {
                    LogService.LogWarn("capture.clipboard.ui_fallback_recovered", $"elapsedMs={clipboardWatch.ElapsedMilliseconds}", operationId, stage);

                    if (showNotification)
                    {
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            TrayIcon.ShowBalloonTip("PathSnip", successMessage, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                        }));
                    }

                    return;
                }

                if (clipboardMode == ClipboardMode.ImageAndPath)
                {
                    bool imageOnlyRecovered = await TryClipboardWriteOnUiThreadWithRetryAsync(
                        ClipboardMode.ImageOnly,
                        bitmap,
                        filePath,
                        pathFormat,
                        markdownHtmlCopyMode,
                        operationId,
                        "clipboard.image_path.partial_image");

                    if (imageOnlyRecovered)
                    {
                        LogService.LogWarn(
                            "capture.clipboard.partial_recovered",
                            $"elapsedMs={clipboardWatch.ElapsedMilliseconds} recovered=image_only",
                            operationId,
                            stage);

                        if (showNotification)
                        {
                            _ = Dispatcher.BeginInvoke(new Action(() =>
                            {
                                TrayIcon.ShowBalloonTip(
                                    "PathSnip",
                                    "图片已复制，路径复制失败（文件已保存，可能被其他程序占用）",
                                    Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                            }));
                        }

                        return;
                    }
                }

                LogService.LogWarn("capture.clipboard.failed", $"elapsedMs={clipboardWatch.ElapsedMilliseconds}", operationId, stage);
                if (showNotification)
                {
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        TrayIcon.ShowBalloonTip("PathSnip", failedMessage, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                    }));
                }
            }
            catch (Exception ex)
            {
                clipboardWatch.Stop();
                string stage = isCompositeCapture ? "clipboard.composite" : "clipboard.region";
                LogService.LogException("capture.clipboard.unexpected_error", ex, $"elapsedMs={clipboardWatch.ElapsedMilliseconds}", operationId, stage);

                if (showNotification)
                {
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        TrayIcon.ShowBalloonTip("PathSnip", "写入剪贴板异常（文件已保存）", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                    }));
                }
            }
        }

        private async Task<bool> TryClipboardWriteOnUiThreadWithRetryAsync(
            ClipboardMode clipboardMode,
            System.Windows.Media.Imaging.BitmapSource bitmap,
            string filePath,
            string pathFormat,
            string markdownHtmlCopyMode,
            string operationId,
            string stage)
        {
            int[] retryDelays = GetUiFallbackRetryDelays(clipboardMode);

            for (int attempt = 0; attempt <= retryDelays.Length; attempt++)
            {
                bool copied = await Dispatcher.InvokeAsync(() =>
                    TryClipboardWriteOnUiThread(clipboardMode, bitmap, filePath, pathFormat, markdownHtmlCopyMode, operationId));

                if (copied)
                {
                    return true;
                }

                if (attempt >= retryDelays.Length)
                {
                    break;
                }

                int delayMs = retryDelays[attempt];
                LogService.LogWarn(
                    "capture.clipboard.ui_fallback_retry",
                    $"attempt={attempt + 1} delayMs={delayMs}",
                    operationId,
                    stage);

                await Task.Delay(delayMs);
            }

            return false;
        }

        private static int[] GetUiFallbackRetryDelays(ClipboardMode clipboardMode)
        {
            switch (clipboardMode)
            {
                case ClipboardMode.ImageOnly:
                    return UiFallbackImageRetryDelaysMs;
                case ClipboardMode.ImageAndPath:
                    return UiFallbackImageAndPathRetryDelaysMs;
                default:
                    return UiFallbackPathRetryDelaysMs;
            }
        }

        private static bool TryClipboardWriteOnUiThread(
            ClipboardMode clipboardMode,
            System.Windows.Media.Imaging.BitmapSource bitmap,
            string filePath,
            string pathFormat,
            string markdownHtmlCopyMode,
            string operationId)
        {
            switch (clipboardMode)
            {
                case ClipboardMode.ImageOnly:
                    return ClipboardService.TrySetImageOnCurrentThreadOnce(bitmap, operationId);
                case ClipboardMode.ImageAndPath:
                    return ClipboardService.TrySetImageAndPathOnCurrentThreadOnce(
                        bitmap,
                        GetPlainPath(filePath),
                        operationId);
                default:
                    return ClipboardService.TrySetTextOnCurrentThreadOnce(
                        ClipboardService.FormatPath(filePath, pathFormat, markdownHtmlCopyMode),
                        operationId);
            }
        }

        private static string GetPlainPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path ?? string.Empty;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private void StartClipboardRecoveryAfterSaveFailure(
            System.Windows.Media.Imaging.BitmapSource bitmap,
            bool showNotification,
            string operationId)
        {
            _ = WriteClipboardRecoveryImageAsync(bitmap, showNotification, operationId);
        }

        private async Task WriteClipboardRecoveryImageAsync(
            System.Windows.Media.Imaging.BitmapSource bitmap,
            bool showNotification,
            string operationId)
        {
            var recoveryWatch = Stopwatch.StartNew();
            try
            {
                bool copied = await ClipboardService.TrySetImageAsync(bitmap, operationId);
                recoveryWatch.Stop();

                if (copied)
                {
                    LogService.LogWarn("capture.recovery.clipboard_image_copied", $"elapsedMs={recoveryWatch.ElapsedMilliseconds}", operationId, "capture.recovery");
                    if (showNotification)
                    {
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            TrayIcon.ShowBalloonTip("PathSnip", "保存失败，图片已复制到剪贴板", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                        }));
                    }
                    return;
                }

                LogService.LogWarn("capture.recovery.clipboard_image_failed", $"elapsedMs={recoveryWatch.ElapsedMilliseconds}", operationId, "capture.recovery");
                if (showNotification)
                {
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        TrayIcon.ShowBalloonTip("PathSnip", "保存失败，且复制图片失败", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
                    }));
                }
            }
            catch (Exception ex)
            {
                recoveryWatch.Stop();
                LogService.LogException("capture.recovery.clipboard_image_error", ex, $"elapsedMs={recoveryWatch.ElapsedMilliseconds}", operationId, "capture.recovery");
            }
        }
    }
}
