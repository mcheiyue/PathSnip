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

        private async void OnCaptureCompleted(Rect region)
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
                string notifyMsg = "已保存";
                
                switch (config.ClipboardMode)
                {
                    case ClipboardMode.ImageOnly:
                        if (!await ClipboardService.TrySetImageAsync(bitmap, operationId))
                        {
                            LogService.LogWarn("capture.clipboard.failed", "复制图片失败", operationId, "clipboard.image");
                            notifyMsg = "已保存，但复制图片失败";
                            break;
                        }
                        notifyMsg = "已保存并复制图片";
                        break;
                    case ClipboardMode.ImageAndPath:
                        var formattedPathForImageAndPath = ClipboardService.FormatPath(filePath, config.PathFormat);
                        if (!await ClipboardService.TrySetImageAndPathAsync(bitmap, formattedPathForImageAndPath, operationId))
                        {
                            LogService.LogWarn("capture.clipboard.failed", "复制图片和路径失败", operationId, "clipboard.image_path");
                            notifyMsg = "已保存，但复制图片和路径失败";
                            break;
                        }
                        notifyMsg = "已保存并复制图片和路径";
                        break;
                    default:
                        var formattedPath = ClipboardService.FormatPath(filePath, config.PathFormat);
                        if (!await ClipboardService.TrySetTextAsync(formattedPath, operationId))
                        {
                            LogService.LogWarn("capture.clipboard.failed", "复制路径失败", operationId, "clipboard.path");
                            notifyMsg = "已保存，但复制路径失败";
                            break;
                        }
                        notifyMsg = "已保存并复制路径";
                        break;
                }

                // 根据配置决定是否显示通知
                if (config.ShowNotification)
                {
                    TrayIcon.ShowBalloonTip("PathSnip", notifyMsg, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                }

                stopwatch.Stop();
                LogService.LogInfo("capture.region.completed", $"elapsedMs={stopwatch.ElapsedMilliseconds} notify={notifyMsg}", operationId, "capture.done");
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

        private async void OnCaptureCompletedWithImage(System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            string operationId = LogService.CreateOperationId("save");
            var stopwatch = Stopwatch.StartNew();
            string filePath = null;
            try
            {
                LogService.LogInfo("capture.composite.start", $"pixel={bitmap?.PixelWidth ?? 0}x{bitmap?.PixelHeight ?? 0}", operationId, "capture.start");
                filePath = FileService.Save(bitmap, operationId);
                LogService.LogInfo("capture.file.saved", $"pathLength={filePath?.Length ?? 0}", operationId, "file.save");

                var config = ConfigService.Instance;
                string notifyMsg = "已保存";
                
                switch (config.ClipboardMode)
                {
                    case ClipboardMode.ImageOnly:
                        if (!await ClipboardService.TrySetImageAsync(bitmap, operationId))
                        {
                            LogService.LogWarn("capture.clipboard.failed", "复制图片失败", operationId, "clipboard.image");
                            notifyMsg = "已保存，但复制图片失败";
                            break;
                        }
                        notifyMsg = "已保存并复制图片";
                        break;
                    case ClipboardMode.ImageAndPath:
                        var formattedPathForImageAndPath = ClipboardService.FormatPath(filePath, config.PathFormat);
                        if (!await ClipboardService.TrySetImageAndPathAsync(bitmap, formattedPathForImageAndPath, operationId))
                        {
                            LogService.LogWarn("capture.clipboard.failed", "复制图片和路径失败", operationId, "clipboard.image_path");
                            notifyMsg = "已保存，但复制图片和路径失败";
                            break;
                        }
                        notifyMsg = "已保存并复制图片和路径";
                        break;
                    default:
                        var formattedPath = ClipboardService.FormatPath(filePath, config.PathFormat);
                        if (!await ClipboardService.TrySetTextAsync(formattedPath, operationId))
                        {
                            LogService.LogWarn("capture.clipboard.failed", "复制路径失败", operationId, "clipboard.path");
                            notifyMsg = "已保存，但复制路径失败";
                            break;
                        }
                        notifyMsg = "已保存并复制路径";
                        break;
                }

                if (config.ShowNotification)
                {
                    TrayIcon.ShowBalloonTip("PathSnip", notifyMsg, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                }

                stopwatch.Stop();
                LogService.LogInfo("capture.composite.completed", $"elapsedMs={stopwatch.ElapsedMilliseconds} notify={notifyMsg}", operationId, "capture.done");
            }
            catch (Exception ex)
            {
                // 保存失败时，仍尝试复制图片
                if (filePath == null)
                {
                    if (await ClipboardService.TrySetImageAsync(bitmap, operationId))
                    {
                        LogService.LogWarn("capture.composite.save_failed_clipboard_ok", "保存失败，但图片已复制到剪贴板", operationId, "capture.recovery");
                        TrayIcon.ShowBalloonTip("PathSnip", "保存失败，图片已复制到剪贴板", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                    }
                    else
                    {
                        LogService.LogException("capture.composite.save_failed_clipboard_failed", ex, "保存失败，且复制图片到剪贴板失败", operationId, "capture.recovery");
                    }
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
                    _captureWindow?.Close();
                    _captureWindow = null;
                    _isCapturing = false;  // 重置状态
                    // 不恢复主窗口，保持托盘隐藏
                }));
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
