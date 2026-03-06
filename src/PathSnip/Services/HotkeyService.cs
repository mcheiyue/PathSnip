using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PathSnip.Services
{
    public class HotkeyService : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private IntPtr _windowHandle;
        private HwndSource _source;
        private int _hotkeyId = 9000;
        private bool _isRegistered;

        private static string FormatHotkey(ModifierKeys modifiers, Key key)
        {
            var modifierText = modifiers.ToString().Replace(", ", "+");
            return string.IsNullOrWhiteSpace(modifierText) ? key.ToString() : $"{modifierText}+{key}";
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public bool Register(ModifierKeys modifiers, Key key, Action callback)
        {
            var helper = new WindowInteropHelper(Application.Current.MainWindow);
            _windowHandle = helper.EnsureHandle();

            _source = HwndSource.FromHwnd(_windowHandle);
            _source?.AddHook(HwndHook);

            // 将 ModifierKeys 和 Key 转换为系统参数
            uint fsModifiers = 0;
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) fsModifiers |= 0x0001;
            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control) fsModifiers |= 0x0002;
            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) fsModifiers |= 0x0004;
            if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows) fsModifiers |= 0x0008;

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

            _isRegistered = RegisterHotKey(_windowHandle, _hotkeyId, fsModifiers, vk);
            var hotkeyDisplay = FormatHotkey(modifiers, key);

            if (!_isRegistered)
            {
                LogService.LogWarn("hotkey.register.failed", $"热键注册失败: {hotkeyDisplay} 可能已被其他程序占用", stage: "hotkey.register");
            }
            else
            {
                LogService.LogInfo("hotkey.register.success", $"热键注册成功: {hotkeyDisplay}", stage: "hotkey.register");
            }

            // 存储回调（这里简化处理，实际可以用字典存储多个回调）
            _callback = callback;

            return _isRegistered;
        }

        private Action _callback;

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
            {
                _callback?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Unregister()
        {
            if (_isRegistered && _windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, _hotkeyId);
                _isRegistered = false;
                LogService.LogInfo("hotkey.unregister", "热键已注销", stage: "hotkey.unregister");
            }
        }

        public void Dispose()
        {
            Unregister();
            _source?.RemoveHook(HwndHook);
        }
    }
}
