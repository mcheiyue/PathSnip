using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;
using PathSnip.Services;

namespace PathSnip
{
    public partial class SettingsWindow : Window
    {
        private bool _isInitializing = true;

        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
            _isInitializing = false;
        }

        private void LoadSettings()
        {
            var config = ConfigService.Instance;

            // 快捷键
            HotkeyTextBox.Text = $"{config.HotkeyModifiers}+{config.HotkeyKey}";

            // 保存目录
            SaveDirectoryTextBox.Text = config.SaveDirectory;

            // 开机自启
            StartWithWindowsCheckBox.IsChecked = StartWithWindowsHelper.IsEnabled();

            // 通知
            ShowNotificationCheckBox.IsChecked = config.ShowNotification;
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;

            // 忽略修饰键单独按下
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                return;
            }

            // 构建快捷键字符串
            var modifiers = Keyboard.Modifiers;
            var key = e.Key;

            // 如果只按下了普通键（无修饰键），忽略
            if (modifiers == ModifierKeys.None)
            {
                return;
            }

            var keyStr = GetKeyString(key);
            var modifierStr = GetModifierString(modifiers);

            HotkeyTextBox.Text = $"{modifierStr}+{keyStr}";
            HotkeyHint.Text = "✓ 快捷键已设置";
        }

        private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            HotkeyHint.Text = "按下组合键（如 Ctrl+Shift+A）";
        }

        private string GetKeyString(Key key)
        {
            // 处理数字键和字母键
            if (key >= Key.A && key <= Key.Z)
            {
                return key.ToString();
            }
            if (key >= Key.D0 && key <= Key.D9)
            {
                return ((char)('0' + (key - Key.D0))).ToString();
            }
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                return "Num" + (key - Key.NumPad0);
            }

            return key.ToString();
        }

        private string GetModifierString(ModifierKeys modifiers)
        {
            var parts = new System.Collections.Generic.List<string>();

            if ((modifiers & ModifierKeys.Control) != 0)
                parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Shift) != 0)
                parts.Add("Shift");
            if ((modifiers & ModifierKeys.Alt) != 0)
                parts.Add("Alt");

            return string.Join("+", parts);
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择截图保存目录";
                dialog.SelectedPath = SaveDirectoryTextBox.Text;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SaveDirectoryTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var enabled = StartWithWindowsCheckBox.IsChecked == true;
            StartWithWindowsHelper.SetEnabled(enabled);
        }

        private void ShowNotification_Changed(object sender, RoutedEventArgs e)
        {
            // 实时保存会在Button_Click 中处理 Save
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = ConfigService.Instance;

                // 保存快捷键
                var hotkeyText = HotkeyTextBox.Text;
                var parts = hotkeyText.Split('+');
                if (parts.Length >= 2)
                {
                    var modifiersStr = string.Join("+", parts, 0, parts.Length - 1);
                    var keyStr = parts[parts.Length - 1];

                    config.HotkeyModifiers = modifiersStr;
                    config.HotkeyKey = keyStr;

                    // 通知主窗口更新热键
                    if (HotkeyChanged != null)
                    {
                        HotkeyChanged(modifiersStr, keyStr);
                    }
                }

                // 保存目录
                config.SaveDirectory = SaveDirectoryTextBox.Text;

                // 保存通知设置
                config.ShowNotification = ShowNotificationCheckBox.IsChecked == true;

                config.Save();

                System.Windows.MessageBox.Show("设置已保存", "PathSnip", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "PathSnip", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event Action<string, string> HotkeyChanged;
    }
}
