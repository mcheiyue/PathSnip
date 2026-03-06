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
        private CaptureOverlayWindow _captureWindow;
        private HotkeyService _hotkeyService;
        private bool _isCapturing;

        public MainWindow()
        {
            InitializeComponent();
        }

        public void StartCapture()
        {
            // 防止重复触发
            if (_isCapturing) return;
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
                            LogService.Log($"截取背景失败: {ex.Message}");
                        }

                        // 创建并显示框选窗口，传入背景图
                        _captureWindow = new CaptureOverlayWindow(background);
                        _captureWindow.CaptureCompleted += OnCaptureCompleted;
                        _captureWindow.CaptureCompletedWithImage += OnCaptureCompletedWithImage;
                        _captureWindow.CaptureCancelled += OnCaptureCancelled;
                        _captureWindow.Show();
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"创建截图窗口失败: {ex.Message}");
                        _isCapturing = false;
                    }
                }), System.Windows.Threading.DispatcherPriority.Send);
            }
            catch (Exception ex)
            {
                LogService.Log($"StartCapture失败: {ex.Message}");
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
                    LogService.Log($"热键注册失败: {modifiersStr}+{keyStr}");
                    return;
                }

                // 更新菜单显示的快捷键
                UpdateMenuHotkeyText();

                LogService.Log($"热键已更新: {modifiersStr}+{keyStr}");
            }
            catch (Exception ex)
            {
                LogService.LogError("更新热键失败", ex);
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

                // 根据配置决定是否显示通知
                if (config.ShowNotification)
                {
                    TrayIcon.ShowBalloonTip("PathSnip", "已保存", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                }

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
                    _captureWindow?.Close();
                    _captureWindow = null;
                    _isCapturing = false;  // 重置状态
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

                if (config.ShowNotification)
                {
                    TrayIcon.ShowBalloonTip("PathSnip", "已保存", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                }

                stopwatch.Stop();
                LogService.LogInfo("capture.composite.completed", $"elapsedMs={stopwatch.ElapsedMilliseconds}", operationId, "capture.done");
            }
            catch (Exception ex)
            {
                LogService.LogException("capture.composite.failed", ex, filePath == null ? "pathLength=0" : $"pathLength={filePath.Length}", operationId, "capture.error");
                TrayIcon.ShowBalloonTip("PathSnip", $"截图失败: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
            }
            finally
            {
                stopwatch.Stop();
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    _captureWindow?.Close();
                    _captureWindow = null;
                    _isCapturing = false;  // 重置状态
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
                string failedMessage;
                string stage;

                switch (clipboardMode)
                {
                    case ClipboardMode.ImageOnly:
                        copied = await ClipboardService.TrySetImageAsync(bitmap, operationId);
                        failedMessage = "已保存，但复制图片失败";
                        stage = "clipboard.image";
                        break;
                    case ClipboardMode.ImageAndPath:
                        var formattedPathForImageAndPath = ClipboardService.FormatPath(filePath, pathFormat);
                        copied = await ClipboardService.TrySetImageAndPathAsync(bitmap, formattedPathForImageAndPath, operationId);
                        failedMessage = "已保存，但复制图片和路径失败";
                        stage = "clipboard.image_path";
                        break;
                    default:
                        var formattedPath = ClipboardService.FormatPath(filePath, pathFormat);
                        copied = await ClipboardService.TrySetTextAsync(formattedPath, operationId);
                        failedMessage = "已保存，但复制路径失败";
                        stage = "clipboard.path";
                        break;
                }

                clipboardWatch.Stop();
                if (copied)
                {
                    LogService.LogInfo("capture.clipboard.completed", $"elapsedMs={clipboardWatch.ElapsedMilliseconds}", operationId, stage);
                    return;
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
            }
        }

        private void OnCaptureCancelled()
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                _captureWindow?.Close();
                _captureWindow = null;
                _isCapturing = false;  // 重置状态
                // 不恢复主窗口，保持托盘隐藏
                LogService.LogInfo("capture.cancelled", "截图已取消", stage: "capture.cancel");
            }));
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
