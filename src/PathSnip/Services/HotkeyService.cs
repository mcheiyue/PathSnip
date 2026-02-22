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

            if (!_isRegistered)
            {
                LogService.Log($"热键注册失败: Ctrl+Shift+A 可能已被其他程序占用");
            }
            else
            {
                LogService.Log($"热键注册成功: Ctrl+Shift+A");
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
                LogService.Log("热键已注销");
            }
        }

        public void Dispose()
        {
            Unregister();
            _source?.RemoveHook(HwndHook);
        }
    }
}
