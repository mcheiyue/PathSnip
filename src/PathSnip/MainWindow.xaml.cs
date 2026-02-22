using System;
using System.Diagnostics;
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

        public MainWindow()
        {
            InitializeComponent();
        }

        public void StartCapture()
        {
            // 隐藏主窗口（如果显示了）
            this.Hide();

            // 创建并显示框选窗口
            _captureWindow = new CaptureOverlayWindow();
            _captureWindow.CaptureCompleted += OnCaptureCompleted;
            _captureWindow.CaptureCancelled += OnCaptureCancelled;
            _captureWindow.Show();
        }

        public void UpdateHotkey(string modifiersStr, string keyStr)
        {
            try
            {
                _hotkeyService?.Unregister();

                var modifiers = ParseModifiers(modifiersStr);
                var key = (Key)Enum.Parse(typeof(Key), keyStr);

                _hotkeyService.Register(modifiers, key, OnHotkeyPressed);

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

        private void OnHotkeyPressed()
        {
            StartCapture();
        }

        private void OnCaptureCompleted(Rect region)
        {
            try
            {
                // 先隐藏选区窗口，避免蓝色蒙版被截入
                if (_captureWindow != null)
                {
                    _captureWindow.Visibility = Visibility.Hidden;
                    
                    // 强制立即渲染，确保窗口完全隐藏后再截图
                    _captureWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                }

                // 执行截图
                var bitmap = ScreenCaptureService.Capture(region);

                // 保存文件并获取路径
                var filePath = FileService.Save(bitmap);

                // 复制路径到剪贴板
                ClipboardService.SetText(filePath);

                // 根据配置决定是否显示通知
                if (ConfigService.Instance.ShowNotification)
                {
                    TrayIcon.ShowBalloonTip("PathSnip", $"已保存并复制路径", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                }

                LogService.Log($"截图成功: {filePath}");
            }
            catch (Exception ex)
            {
                LogService.Log($"截图失败: {ex.Message}");
                TrayIcon.ShowBalloonTip("PathSnip", $"截图失败: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
            }
            finally
            {
                _captureWindow?.Close();
                _captureWindow = null;
            }
        }

        private void OnCaptureCancelled()
        {
            _captureWindow?.Close();
            _captureWindow = null;
            LogService.Log("截图已取消");
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
                LogService.Log($"打开目录失败: {ex.Message}");
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
