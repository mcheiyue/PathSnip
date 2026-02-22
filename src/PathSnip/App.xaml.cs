using System;
using System.Windows;
using System.Windows.Input;
using PathSnip.Services;

namespace PathSnip
{
    public partial class App : Application
    {
        private MainWindow _mainWindow;
        private HotkeyService _hotkeyService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 初始化配置
            ConfigService.Instance.EnsureDirectoryExists();

            // 创建主窗口（隐藏）
            _mainWindow = new MainWindow();
            _mainWindow.Show();
            _mainWindow.Hide();

            // 更新菜单快捷键显示
            _mainWindow.UpdateMenuHotkeyText();

            // 从配置读取快捷键并注册
            var config = ConfigService.Instance;
            var modifiers = ParseModifiers(config.HotkeyModifiers);
            var key = (Key)Enum.Parse(typeof(Key), config.HotkeyKey);

            _hotkeyService = new HotkeyService();
            _hotkeyService.Register(modifiers, key, OnHotkeyPressed);
            _mainWindow.SetHotkeyService(_hotkeyService);

            LogService.Log($"PathSnip 启动完成，热键已注册 ({config.HotkeyModifiers}+{config.HotkeyKey})");
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

        private void OnHotkeyPressed()
        {
            // 触发截图
            _mainWindow.StartCapture();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hotkeyService?.Unregister();
            LogService.Log("PathSnip 已退出");
            base.OnExit(e);
        }
    }
}
