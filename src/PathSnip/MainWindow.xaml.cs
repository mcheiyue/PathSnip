using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PathSnip.Services;

namespace PathSnip
{
    public partial class MainWindow : Window
    {
        private const int CaptureDebounceWindowMs = 300;
        private CaptureOverlayWindow _captureWindow;
        private HotkeyService _hotkeyService;
        private bool _hasRegisteredHotkey;
        private string _registeredHotkeyModifiers;
        private string _registeredHotkeyKey;
        private bool _isCapturing;
        private int _lastCaptureRequestTick = -1;
        private string _lastSavedPath;

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

        public bool UpdateHotkey(string modifiersStr, string keyStr)
        {
            try
            {
                if (_hotkeyService == null)
                {
                    LogService.LogWarn("hotkey.update.no_service", "HotkeyService is null", stage: "hotkey.update");
                    return false;
                }

                bool canRollback = _hasRegisteredHotkey
                    && !string.IsNullOrWhiteSpace(_registeredHotkeyModifiers)
                    && !string.IsNullOrWhiteSpace(_registeredHotkeyKey);
                var rollbackModifiersStr = _registeredHotkeyModifiers;
                var rollbackKeyStr = _registeredHotkeyKey;

                if (_hasRegisteredHotkey)
                {
                    _hotkeyService.Unregister();
                    _hasRegisteredHotkey = false;
                }

                var modifiers = ParseModifiers(modifiersStr);
                var key = (Key)Enum.Parse(typeof(Key), keyStr);

                var registerSuccess = _hotkeyService.Register(modifiers, key, OnHotkeyPressed);
                if (!registerSuccess)
                {
                    TrayIcon.ShowBalloonTip("PathSnip", $"热键 {modifiersStr}+{keyStr} 注册失败，可能已被占用，请在设置中更换。", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                    LogService.LogWarn("hotkey.update.register_failed", $"热键注册失败: {modifiersStr}+{keyStr}", stage: "hotkey.update");

                    if (canRollback)
                    {
                        var rollbackModifiers = ParseModifiers(rollbackModifiersStr);
                        var rollbackKey = (Key)Enum.Parse(typeof(Key), rollbackKeyStr);
                        var rollbackSuccess = _hotkeyService.Register(rollbackModifiers, rollbackKey, OnHotkeyPressed);
                        if (rollbackSuccess)
                        {
                            _hasRegisteredHotkey = true;
                            _registeredHotkeyModifiers = rollbackModifiersStr;
                            _registeredHotkeyKey = rollbackKeyStr;
                            UpdateMenuHotkeyText(rollbackModifiersStr, rollbackKeyStr);
                            LogService.LogInfo("hotkey.update.rollback_success", $"热键已回滚: {rollbackModifiersStr}+{rollbackKeyStr}", stage: "hotkey.update");
                        }
                        else
                        {
                            TrayIcon.ShowBalloonTip("PathSnip", $"热键 {rollbackModifiersStr}+{rollbackKeyStr} 回滚失败，请在设置中重新设置。", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
                            LogService.LogWarn("hotkey.update.rollback_failed", $"热键回滚失败: {rollbackModifiersStr}+{rollbackKeyStr}", stage: "hotkey.update");
                        }
                    }

                    return false;
                }

                _hasRegisteredHotkey = true;
                _registeredHotkeyModifiers = modifiersStr;
                _registeredHotkeyKey = keyStr;

                // 更新菜单显示的快捷键
                UpdateMenuHotkeyText(modifiersStr, keyStr);

                LogService.LogInfo("hotkey.update.success", $"热键已更新: {modifiersStr}+{keyStr}", stage: "hotkey.update");
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogException("hotkey.update.failed", ex, "更新热键失败", stage: "hotkey.update");
                return false;
            }
        }

        public void UpdateMenuHotkeyText(string modifiersOverride = null, string keyOverride = null)
        {
            var config = ConfigService.Instance;
            var modifiers = string.IsNullOrWhiteSpace(modifiersOverride) ? config.HotkeyModifiers : modifiersOverride;
            var key = string.IsNullOrWhiteSpace(keyOverride) ? config.HotkeyKey : keyOverride;
            var contextMenu = (ContextMenu)Resources["TrayMenu"];
            var captureItem = FindMenuItemByTag(contextMenu, "Capture");
            if (captureItem != null)
            {
                captureItem.Header = $"截图 ({modifiers}+{key})";
            }
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

        public void SetHotkeyService(HotkeyService service, bool isRegistered, string registeredModifiers, string registeredKey)
        {
            _hotkeyService = service;

            _hasRegisteredHotkey = isRegistered;
            if (isRegistered)
            {
                _registeredHotkeyModifiers = registeredModifiers;
                _registeredHotkeyKey = registeredKey;
            }
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

                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    _lastSavedPath = filePath;
                }

                var config = ConfigService.Instance;
                StartClipboardWriteAfterSave(bitmap, filePath, config.ClipboardMode, config.PathFormat, config.MarkdownHtmlCopyMode, config.ShowNotification, operationId, isCompositeCapture: false);

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

                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    _lastSavedPath = filePath;
                }

                var config = ConfigService.Instance;
                StartClipboardWriteAfterSave(bitmap, filePath, config.ClipboardMode, config.PathFormat, config.MarkdownHtmlCopyMode, config.ShowNotification, operationId, isCompositeCapture: true);

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


        private void TrayMenu_Opened(object sender, RoutedEventArgs e)
        {
            try
            {
                RefreshTrayMenuState();
            }
            catch (Exception ex)
            {
                LogService.LogException("tray.menu.opened.failed", ex, "刷新托盘菜单状态失败", stage: "tray.menu");
            }
        }

        private void RefreshTrayMenuState()
        {
            var contextMenu = (ContextMenu)Resources["TrayMenu"];
            var config = ConfigService.Instance;

            UpdateMenuHotkeyText();

            bool hasRecent = TryGetLastSavedPath(out _);
            var recentMenu = FindMenuItemByTag(contextMenu, "Recent");
            if (recentMenu != null)
            {
                recentMenu.IsEnabled = hasRecent;
            }

            SetMenuItemEnabled(contextMenu, "RecentCopyImage", hasRecent);
            SetMenuItemEnabled(contextMenu, "RecentCopyPath", hasRecent);
            SetMenuItemEnabled(contextMenu, "RecentOpenFile", hasRecent);
            SetMenuItemEnabled(contextMenu, "RecentLocate", hasRecent);

            var clipboardModeMenu = FindMenuItemByTag(contextMenu, "ClipboardMode");
            if (clipboardModeMenu != null)
            {
                foreach (var item in clipboardModeMenu.Items)
                {
                    if (item is MenuItem menuItem)
                    {
                        menuItem.IsChecked = string.Equals(menuItem.Tag?.ToString(), config.ClipboardMode.ToString(), StringComparison.Ordinal);
                    }
                }
            }

            var pathFormatMenu = FindMenuItemByTag(contextMenu, "PathFormat");
            if (pathFormatMenu != null)
            {
                pathFormatMenu.IsEnabled = config.ClipboardMode == ClipboardMode.PathOnly;

                var currentPathFormat = string.IsNullOrWhiteSpace(config.PathFormat) ? "Text" : config.PathFormat;
                foreach (var item in pathFormatMenu.Items)
                {
                    if (item is MenuItem menuItem)
                    {
                        menuItem.IsChecked = string.Equals(menuItem.Tag?.ToString(), currentPathFormat, StringComparison.Ordinal);
                    }
                }
            }
        }

        private static void SetMenuItemEnabled(ContextMenu menu, string tag, bool isEnabled)
        {
            var item = FindMenuItemByTag(menu, tag);
            if (item != null)
            {
                item.IsEnabled = isEnabled;
            }
        }

        private static MenuItem FindMenuItemByTag(ItemsControl parent, string tag)
        {
            foreach (var item in parent.Items)
            {
                if (item is MenuItem menuItem)
                {
                    if (string.Equals(menuItem.Tag?.ToString(), tag, StringComparison.Ordinal))
                    {
                        return menuItem;
                    }

                    var nested = FindMenuItemByTag(menuItem, tag);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private bool TryGetLastSavedPath(out string filePath)
        {
            filePath = _lastSavedPath;
            return !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath);
        }

        private static BitmapSource LoadBitmapFromFile(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }

        private async void MenuItem_RecentCopyImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryGetLastSavedPath(out var filePath))
                {
                    return;
                }

                var bitmap = LoadBitmapFromFile(filePath);
                await ClipboardService.TrySetImageAsync(bitmap);
            }
            catch (Exception ex)
            {
                LogService.LogException("tray.recent.copy_image.failed", ex, "复制最近一次图片失败", stage: "tray.recent");
            }
        }

        private async void MenuItem_RecentCopyPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryGetLastSavedPath(out var filePath))
                {
                    return;
                }

                var config = ConfigService.Instance;
                var currentPathFormat = string.IsNullOrWhiteSpace(config.PathFormat) ? "Text" : config.PathFormat;
                var formattedPath = ClipboardService.FormatPath(filePath, currentPathFormat, config.MarkdownHtmlCopyMode);
                await ClipboardService.TrySetTextAsync(formattedPath);
            }
            catch (Exception ex)
            {
                LogService.LogException("tray.recent.copy_path.failed", ex, "复制最近一次路径失败", stage: "tray.recent");
            }
        }

        private async void MenuItem_RecentCopyPathWithFormat_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is MenuItem menuItem))
                {
                    return;
                }

                var format = menuItem.Tag?.ToString();
                if (string.IsNullOrWhiteSpace(format))
                {
                    return;
                }

                if (!TryGetLastSavedPath(out var filePath))
                {
                    return;
                }

                string text;
                if (string.Equals(format, "Text", StringComparison.Ordinal))
                {
                    text = GetPlainPath(filePath);
                }
                else if (string.Equals(format, "Markdown", StringComparison.Ordinal) || string.Equals(format, "HTML", StringComparison.Ordinal))
                {
                    text = ClipboardService.FormatPath(filePath, format, "SnippetOnly");
                }
                else
                {
                    text = GetPlainPath(filePath);
                }

                await ClipboardService.TrySetTextAsync(text);
            }
            catch (Exception ex)
            {
                LogService.LogException("tray.recent.copy_path_with_format.failed", ex, "按指定格式复制最近一次路径失败", stage: "tray.recent");
            }
        }

        private void MenuItem_RecentOpenFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryGetLastSavedPath(out var filePath))
                {
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogService.LogException("tray.recent.open_file.failed", ex, "打开最近一次文件失败", stage: "tray.recent");
            }
        }

        private void MenuItem_RecentLocate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryGetLastSavedPath(out var filePath))
                {
                    return;
                }

                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            catch (Exception ex)
            {
                LogService.LogException("tray.recent.locate.failed", ex, "定位最近一次文件失败", stage: "tray.recent");
            }
        }

        private void MenuItem_ClipboardMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is MenuItem menuItem))
                {
                    return;
                }

                var config = ConfigService.Instance;
                switch (menuItem.Tag?.ToString())
                {
                    case "ImageOnly":
                        config.ClipboardMode = ClipboardMode.ImageOnly;
                        break;
                    case "ImageAndPath":
                        config.ClipboardMode = ClipboardMode.ImageAndPath;
                        break;
                    default:
                        config.ClipboardMode = ClipboardMode.PathOnly;
                        break;
                }

                config.Save();
                RefreshTrayMenuState();
            }
            catch (Exception ex)
            {
                LogService.LogException("tray.clipboard_mode.change.failed", ex, "切换剪贴板模式失败", stage: "tray.menu");
            }
        }

        private void MenuItem_PathFormat_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is MenuItem menuItem))
                {
                    return;
                }

                var pathFormat = menuItem.Tag?.ToString();
                if (string.IsNullOrWhiteSpace(pathFormat))
                {
                    return;
                }

                var config = ConfigService.Instance;
                config.PathFormat = pathFormat;
                config.Save();
                RefreshTrayMenuState();
            }
            catch (Exception ex)
            {
                LogService.LogException("tray.path_format.change.failed", ex, "切换路径格式失败", stage: "tray.menu");
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
