using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PathSnip.Services;

namespace PathSnip
{
    public partial class MainWindow : Window
    {
        private const int CaptureDebounceWindowMs = 300;
        private static readonly int[] UiFallbackPathRetryDelaysMs = { 120, 250, 500, 1000, 1500 };
        private static readonly int[] UiFallbackImageRetryDelaysMs = { 120, 300 };
        private static readonly int[] UiFallbackImageAndPathRetryDelaysMs = { 120, 250, 500, 1000, 2000 };
        private CaptureOverlayWindow _captureWindow;
        private HotkeyService _hotkeyService;
        private bool _isCapturing;
        private int _lastCaptureRequestTick = -1;

        public MainWindow()
        {
            InitializeComponent();
        }

        public void StartCapture()
        {
            int nowTick = Environment.TickCount;
            if (_lastCaptureRequestTick >= 0)
            {
                uint elapsedMs = unchecked((uint)(nowTick - _lastCaptureRequestTick));
                if (elapsedMs < CaptureDebounceWindowMs)
                {
                    LogService.LogInfo("capture.start.debounced", $"elapsedMs={elapsedMs} windowMs={CaptureDebounceWindowMs}", stage: "capture.start");
                    return;
                }
            }

            if (_isCapturing)
            {
                LogService.LogInfo("capture.start.ignored_busy", "capture is already in progress", stage: "capture.start");
                return;
            }

            _lastCaptureRequestTick = nowTick;
            _isCapturing = true;

            try
            {
                // 隐藏主窗口
                this.Hide();

                // 使用 Send 优先级确保立即执行
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // 先截取屏幕作为背景
                        var bounds = ScreenCaptureService.GetVirtualScreenBounds();
                        System.Windows.Media.Imaging.BitmapSource background = null;
                        try
                        {
                            background = ScreenCaptureService.Capture(bounds);
                        }
                        catch (Exception ex)
                        {
                            LogService.LogException("capture.background.failed", ex, "截取背景失败", stage: "capture.background");
                        }

                        // 创建并显示框选窗口，传入背景图
                        _captureWindow = new CaptureOverlayWindow(background);
                        _captureWindow.CaptureCompleted += OnCaptureCompleted;
                        _captureWindow.CaptureCompletedWithImage += OnCaptureCompletedWithImage;
                        _captureWindow.CaptureCancelled += OnCaptureCancelled;
                        _captureWindow.Closed += OnCaptureWindowClosed;
                        _captureWindow.Show();
                    }
                    catch (Exception ex)
                    {
                        LogService.LogException("capture.window_create.failed", ex, "创建截图窗口失败", stage: "capture.window");
                        _isCapturing = false;
                    }
                }), System.Windows.Threading.DispatcherPriority.Send);
            }
            catch (Exception ex)
            {
                LogService.LogException("capture.start.failed", ex, "StartCapture失败", stage: "capture.start");
                _isCapturing = false;
            }
        }

        public void UpdateHotkey(string modifiersStr, string keyStr)
        {
            try
            {
                _hotkeyService?.Unregister();

                var modifiers = ParseModifiers(modifiersStr);
                var key = (Key)Enum.Parse(typeof(Key), keyStr);

                var registerSuccess = _hotkeyService.Register(modifiers, key, OnHotkeyPressed);
                if (!registerSuccess)
                {
                    TrayIcon.ShowBalloonTip("PathSnip", $"热键 {modifiersStr}+{keyStr} 注册失败，可能已被占用，请在设置中更换。", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                    LogService.LogWarn("hotkey.update.register_failed", $"热键注册失败: {modifiersStr}+{keyStr}", stage: "hotkey.update");
                    return;
                }

                // 更新菜单显示的快捷键
                UpdateMenuHotkeyText();

                LogService.LogInfo("hotkey.update.success", $"热键已更新: {modifiersStr}+{keyStr}", stage: "hotkey.update");
            }
            catch (Exception ex)
            {
                LogService.LogException("hotkey.update.failed", ex, "更新热键失败", stage: "hotkey.update");
            }
        }

        public void UpdateMenuHotkeyText()
        {
            var config = ConfigService.Instance;
            var contextMenu = (ContextMenu)Resources["TrayMenu"];
            var captureItem = (MenuItem)contextMenu.Items[0];
            captureItem.Header = $"截图 ({config.HotkeyModifiers}+{config.HotkeyKey})";
        }

        private ModifierKeys ParseModifiers(string modifiersStr)
        {
            var modifiers = ModifierKeys.None;
            var parts = modifiersStr.Split('+');

            foreach (var part in parts)
            {
                switch (part.Trim().ToLower())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ModifierKeys.Control;
                        break;
                    case "shift":
                        modifiers |= ModifierKeys.Shift;
                        break;
                    case "alt":
                        modifiers |= ModifierKeys.Alt;
                        break;
                }
            }

            return modifiers;
        }

        public void SetHotkeyService(HotkeyService service)
        {
            _hotkeyService = service;
        }

        public void ShowTrayNotification(string message, Hardcodet.Wpf.TaskbarNotification.BalloonIcon icon)
        {
            TrayIcon.ShowBalloonTip("PathSnip", message, icon);
        }

        private void OnHotkeyPressed()
        {
            StartCapture();
        }

        private void OnCaptureCompleted(Rect region)
        {
            string operationId = LogService.CreateOperationId("cap");
            var stopwatch = Stopwatch.StartNew();
            try
            {
                LogService.LogInfo("capture.region.start", $"region=({region.Left:F2},{region.Top:F2},{region.Width:F2},{region.Height:F2})", operationId, "capture.start");

                // 先隐藏选区窗口，避免蓝色蒙版被截入
                if (_captureWindow != null)
                {
                    _captureWindow.Visibility = Visibility.Hidden;
                }

                // 执行截图
                var bitmap = ScreenCaptureService.Capture(region);

                // 保存文件并获取路径
                var filePath = FileService.Save(bitmap, operationId);
                LogService.LogInfo("capture.file.saved", $"pathLength={filePath?.Length ?? 0}", operationId, "file.save");

                var config = ConfigService.Instance;
                StartClipboardWriteAfterSave(bitmap, filePath, config.ClipboardMode, config.PathFormat, config.ShowNotification, operationId, isCompositeCapture: false);

                stopwatch.Stop();
                LogService.LogInfo("capture.region.completed", $"elapsedMs={stopwatch.ElapsedMilliseconds}", operationId, "capture.done");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogService.LogException("capture.region.failed", ex, $"elapsedMs={stopwatch.ElapsedMilliseconds}", operationId, "capture.error");
                TrayIcon.ShowBalloonTip("PathSnip", $"截图失败: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
            }
            finally
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    _isCapturing = false;  // 重置状态
                    _captureWindow?.Close();
                    _captureWindow = null;
                    // 不恢复主窗口，保持托盘隐藏
                }));
            }
        }

        private void OnCaptureCompletedWithImage(System.Windows.Media.Imaging.BitmapSource bitmap, string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                operationId = LogService.CreateOperationId("save");
            }

            var stopwatch = Stopwatch.StartNew();
            string filePath = null;
            try
            {
                LogService.LogInfo("capture.composite.start", $"pixel={bitmap?.PixelWidth ?? 0}x{bitmap?.PixelHeight ?? 0}", operationId, "capture.start");
                filePath = FileService.Save(bitmap, operationId);
                LogService.LogInfo("capture.file.saved", $"pathLength={filePath?.Length ?? 0}", operationId, "file.save");

                var config = ConfigService.Instance;
                StartClipboardWriteAfterSave(bitmap, filePath, config.ClipboardMode, config.PathFormat, config.ShowNotification, operationId, isCompositeCapture: true);

                stopwatch.Stop();
                LogService.LogInfo("capture.composite.completed", $"elapsedMs={stopwatch.ElapsedMilliseconds}", operationId, "capture.done");
            }
            catch (Exception ex)
            {
                if (filePath == null)
                {
                    var config = ConfigService.Instance;
                    LogService.LogException("capture.composite.save_failed", ex, "保存失败，尝试复制图片兜底", operationId, "capture.recovery");
                    StartClipboardRecoveryAfterSaveFailure(bitmap, config.ShowNotification, operationId);
                }
                else
                {
                    LogService.LogException("capture.composite.failed", ex, $"pathLength={filePath.Length}", operationId, "capture.error");
                    TrayIcon.ShowBalloonTip("PathSnip", $"截图失败: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
                }
            }
            finally
            {
                stopwatch.Stop();
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    _isCapturing = false;  // 重置状态
                    _captureWindow?.Close();
                    _captureWindow = null;
                    // 不恢复主窗口，保持托盘隐藏
                }));
            }
        }

        private void StartClipboardWriteAfterSave(
            System.Windows.Media.Imaging.BitmapSource bitmap,
            string filePath,
            ClipboardMode clipboardMode,
            string pathFormat,
            bool showNotification,
            string operationId,
            bool isCompositeCapture)
        {
            _ = WriteClipboardAfterSaveAsync(bitmap, filePath, clipboardMode, pathFormat, showNotification, operationId, isCompositeCapture);
        }

        private async Task WriteClipboardAfterSaveAsync(
            System.Windows.Media.Imaging.BitmapSource bitmap,
            string filePath,
            ClipboardMode clipboardMode,
            string pathFormat,
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
                        var formattedPathForImageAndPath = ClipboardService.FormatPath(filePath, pathFormat);
                        copied = await ClipboardService.TrySetImageAndPathAsync(bitmap, formattedPathForImageAndPath, operationId);
                        successMessage = "已保存，图片和路径已复制";
                        failedMessage = "复制图片和路径失败（文件已保存，可能被其他程序占用）";
                        stage = "clipboard.image_path";
                        break;
                    default:
                        var formattedPath = ClipboardService.FormatPath(filePath, pathFormat);
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
            string operationId,
            string stage)
        {
            int[] retryDelays = GetUiFallbackRetryDelays(clipboardMode);

            for (int attempt = 0; attempt <= retryDelays.Length; attempt++)
            {
                bool copied = await Dispatcher.InvokeAsync(() =>
                    TryClipboardWriteOnUiThread(clipboardMode, bitmap, filePath, pathFormat, operationId));

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
            string operationId)
        {
            switch (clipboardMode)
            {
                case ClipboardMode.ImageOnly:
                    return ClipboardService.TrySetImageOnCurrentThreadOnce(bitmap, operationId);
                case ClipboardMode.ImageAndPath:
                    return ClipboardService.TrySetImageAndPathOnCurrentThreadOnce(
                        bitmap,
                        ClipboardService.FormatPath(filePath, pathFormat),
                        operationId);
                default:
                    return ClipboardService.TrySetTextOnCurrentThreadOnce(
                        ClipboardService.FormatPath(filePath, pathFormat),
                        operationId);
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

        private void OnCaptureCancelled()
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                _isCapturing = false;  // 重置状态
                _captureWindow?.Close();
                _captureWindow = null;
                // 不恢复主窗口，保持托盘隐藏
                LogService.LogInfo("capture.cancelled", "截图已取消", stage: "capture.cancel");
            }));
        }

        private void OnCaptureWindowClosed(object sender, EventArgs e)
        {
            if (_isCapturing)
            {
                _isCapturing = false;
                LogService.LogWarn("capture.state.reset_by_overlay_closed", "overlay closed before completion callback", stage: "capture.state");
            }

            _captureWindow = null;
        }

        private void MenuItem_Capture_Click(object sender, RoutedEventArgs e)
        {
            StartCapture();
        }

        private void MenuItem_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = ConfigService.Instance.SaveDirectory;
                Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                LogService.LogException("folder.open.failed", ex, "打开目录失败", stage: "menu.open-folder");
            }
        }

        private void MenuItem_Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.HotkeyChanged += UpdateHotkey;
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            TrayIcon.Dispose();
            Application.Current.Shutdown();
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            StartCapture();
        }
    }
}
