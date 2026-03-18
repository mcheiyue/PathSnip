using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PathSnip.Services;

namespace PathSnip
{
    public partial class App : Application
    {
        private const int ClipboardCantOpenHResult = unchecked((int)0x800401D0);

        private MainWindow _mainWindow;
        private HotkeyService _hotkeyService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            RegisterGlobalExceptionHandlers();
            LogService.LogInfo("app.startup", "应用启动", stage: "startup.begin");

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
            var registerSuccess = _hotkeyService.Register(modifiers, key, OnHotkeyPressed);
            _mainWindow.SetHotkeyService(_hotkeyService, registerSuccess, config.HotkeyModifiers, config.HotkeyKey);

            if (!registerSuccess)
            {
                _mainWindow.ShowTrayNotification($"热键 {config.HotkeyModifiers}+{config.HotkeyKey} 注册失败，可能已被占用，请在设置中更换。", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                LogService.LogWarn("app.hotkey.register_failed", $"热键注册失败 ({config.HotkeyModifiers}+{config.HotkeyKey})", stage: "startup.hotkey");
                return;
            }

            LogService.LogInfo("app.startup.completed", $"热键已注册 ({config.HotkeyModifiers}+{config.HotkeyKey})", stage: "startup.hotkey");
        }

        private void RegisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogService.LogException("app.dispatcher_unhandled", e.Exception, "UI线程未处理异常", stage: "global-exception");

            if (e.Exception is ExternalException external && external.HResult == ClipboardCantOpenHResult)
            {
                LogService.LogWarn("app.dispatcher_unhandled.clipboard_busy", "检测到剪贴板占用异常，按非致命处理", stage: "global-exception");
                e.Handled = true;
                return;
            }

            e.Handled = false;
        }

        private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogService.LogException("app.domain_unhandled", ex, $"进程级未处理异常, terminating={e.IsTerminating}", stage: "global-exception");
                return;
            }

            LogService.LogWarn("app.domain_unhandled", $"进程级未处理异常对象不可转换, terminating={e.IsTerminating}", stage: "global-exception");
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogService.LogException("app.task_unobserved", e.Exception, "未观察到的任务异常", stage: "global-exception");
            e.SetObserved();
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
            // 通过Dispatcher调用，确保在UI线程执行
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _mainWindow.StartCapture();
                }
                catch (Exception ex)
                {
                    LogService.LogException("app.hotkey.invoke_failed", ex, "热键触发失败", stage: "hotkey.invoke");
                }
            }), System.Windows.Threading.DispatcherPriority.Send);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_hotkeyService != null)
            {
                _hotkeyService.Dispose();
                _hotkeyService = null;
            }
            LogService.LogInfo("app.exit", "PathSnip 已退出", stage: "shutdown");
            base.OnExit(e);
        }
    }
}
