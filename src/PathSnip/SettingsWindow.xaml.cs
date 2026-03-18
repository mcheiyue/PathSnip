using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;
using PathSnip.Services;

namespace PathSnip
{
    public partial class SettingsWindow : Window
    {
        private bool _isInitializing = true;

        private DirtyFlags _dirty = new DirtyFlags();

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

            // 剪贴板模式
            switch (config.ClipboardMode)
            {
                case ClipboardMode.ImageOnly:
                    ClipboardModeComboBox.SelectedIndex = 1;
                    break;
                case ClipboardMode.ImageAndPath:
                    ClipboardModeComboBox.SelectedIndex = 2;
                    break;
                default:
                    ClipboardModeComboBox.SelectedIndex = 0;
                    break;
            }

            // 路径格式
            switch (config.PathFormat)
            {
                case "Markdown":
                    PathFormatComboBox.SelectedIndex = 1;
                    break;
                case "HTML":
                    PathFormatComboBox.SelectedIndex = 2;
                    break;
                default:
                    PathFormatComboBox.SelectedIndex = 0;
                    break;
            }

            switch (config.MarkdownHtmlCopyMode)
            {
                case "PlainPathOnly":
                    MarkdownHtmlCopyModeComboBox.SelectedIndex = 1;
                    break;
                case "SnippetAndPlainPath":
                    MarkdownHtmlCopyModeComboBox.SelectedIndex = 2;
                    break;
                default:
                    MarkdownHtmlCopyModeComboBox.SelectedIndex = 0;
                    break;
            }

            // 文件名模板
            FileNameTemplateTextBox.Text = config.FileNameTemplate;

            EnableSmartSnapCheckBox.IsChecked = config.EnableSmartSnap;
            EnableElementSnapCheckBox.IsChecked = config.EnableElementSnap;
            HoldAltToBypassSnapCheckBox.IsChecked = config.HoldAltToBypassSnap;

            switch (config.SmartSnapMode)
            {
                case SmartSnapMode.WindowOnly:
                    SmartSnapModeComboBox.SelectedIndex = 1;
                    break;
                case SmartSnapMode.ElementPreferred:
                    SmartSnapModeComboBox.SelectedIndex = 2;
                    break;
                case SmartSnapMode.ManualOnly:
                    SmartSnapModeComboBox.SelectedIndex = 3;
                    break;
                default:
                    SmartSnapModeComboBox.SelectedIndex = 0;
                    break;
            }

            UpdateClipboardSettingsUiState();

            UpdateSmartSnapSettingsUiState();

            _dirty = new DirtyFlags();
        }

        private void ClipboardModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            _dirty.ClipboardMode = true;
            UpdateClipboardSettingsUiState();
        }

        private void PathFormatComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            _dirty.PathFormat = true;
            UpdateClipboardSettingsUiState();
        }

        private void MarkdownHtmlCopyModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            _dirty.MarkdownHtmlCopyMode = true;
        }

        private void UpdateClipboardSettingsUiState()
        {
            if (ClipboardModeComboBox == null
                || PathFormatComboBox == null
                || MarkdownHtmlCopyModeComboBox == null
                || ClipboardPathFormatHintTextBlock == null)
            {
                return;
            }

            var clipboardModeSelectedItem = ClipboardModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
            var clipboardModeTag = clipboardModeSelectedItem?.Tag as string ?? clipboardModeSelectedItem?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(clipboardModeTag))
            {
                clipboardModeTag = "PathOnly";
            }

            bool isPathOnly = string.Equals(clipboardModeTag, "PathOnly", StringComparison.Ordinal);

            string hintText = null;
            bool showHint = false;

            if (!isPathOnly)
            {
                PathFormatComboBox.IsEnabled = false;
                MarkdownHtmlCopyModeComboBox.IsEnabled = false;

                showHint = true;
                if (string.Equals(clipboardModeTag, "ImageOnly", StringComparison.Ordinal))
                {
                    hintText = "仅图片模式不会复制路径；如需调整路径格式，请切换到仅路径";
                }
                else
                {
                    hintText = "图片+路径模式下随图路径固定为纯文本；如需调整路径格式，请切换到仅路径";
                }
            }
            else
            {
                PathFormatComboBox.IsEnabled = true;

                var pathFormatSelectedItem = PathFormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
                var pathFormatTag = pathFormatSelectedItem?.Tag as string ?? pathFormatSelectedItem?.Tag?.ToString();
                if (string.IsNullOrWhiteSpace(pathFormatTag))
                {
                    pathFormatTag = "Text";
                }

                bool isMarkdownOrHtml = string.Equals(pathFormatTag, "Markdown", StringComparison.Ordinal)
                    || string.Equals(pathFormatTag, "HTML", StringComparison.Ordinal);
                MarkdownHtmlCopyModeComboBox.IsEnabled = isMarkdownOrHtml;

                if (!isMarkdownOrHtml)
                {
                    showHint = true;
                    hintText = "“Markdown/HTML 输出”仅对路径格式为 Markdown/HTML 时生效";
                }
            }

            ClipboardPathFormatHintTextBlock.Text = hintText ?? string.Empty;
            ClipboardPathFormatHintTextBlock.Visibility = showHint ? Visibility.Visible : Visibility.Collapsed;
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

            _dirty.Hotkey = true;
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

        private void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            _dirty.StartWithWindows = true;
        }

        private void SaveDirectoryTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            _dirty.SaveDirectory = true;
        }

        private void FileNameTemplateTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            _dirty.FileNameTemplate = true;
        }

        private void ShowNotification_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _dirty.ShowNotification = true;
        }

        private void EnableSmartSnapCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            _dirty.EnableSmartSnap = true;
            UpdateSmartSnapSettingsUiState();
        }

        private void SmartSnapModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            _dirty.SmartSnapMode = true;
        }

        private void EnableElementSnapCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _dirty.EnableElementSnap = true;
        }

        private void HoldAltToBypassSnapCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _dirty.HoldAltToBypassSnap = true;
        }

        private void UpdateSmartSnapSettingsUiState()
        {
            if (EnableSmartSnapCheckBox == null
                || SmartSnapModeComboBox == null
                || EnableElementSnapCheckBox == null
                || HoldAltToBypassSnapCheckBox == null)
            {
                return;
            }

            bool isEnabled = EnableSmartSnapCheckBox.IsChecked == true;
            SmartSnapModeComboBox.IsEnabled = isEnabled;
            EnableElementSnapCheckBox.IsEnabled = isEnabled;
            HoldAltToBypassSnapCheckBox.IsEnabled = isEnabled;
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "确定要重置所有设置为默认值吗？",
                "确认重置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            // 重置为默认值
            HotkeyTextBox.Text = "Ctrl+Shift+A";
            SaveDirectoryTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PathSnip");
            StartWithWindowsCheckBox.IsChecked = false;
            ShowNotificationCheckBox.IsChecked = true;
            ClipboardModeComboBox.SelectedIndex = 0;  // 仅路径
            PathFormatComboBox.SelectedIndex = 0;      // 纯文本
            MarkdownHtmlCopyModeComboBox.SelectedIndex = 0;
            FileNameTemplateTextBox.Text = "{yyyy}-{MM}-{dd}_{HHmmss}";
            EnableSmartSnapCheckBox.IsChecked = true;
            SmartSnapModeComboBox.SelectedIndex = 0;
            EnableElementSnapCheckBox.IsChecked = true;
            HoldAltToBypassSnapCheckBox.IsChecked = true;

            UpdateClipboardSettingsUiState();
            UpdateSmartSnapSettingsUiState();

            System.Windows.MessageBox.Show("设置已重置为默认值，请点击\"保存设置\"生效。", "PathSnip", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // ========== 第一步：验证所有输入（先验证，不修改任何内存） ==========
                
                // 验证目录
                var saveDirectory = SaveDirectoryTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(saveDirectory))
                {
                    System.Windows.MessageBox.Show("保存目录不能为空。", "PathSnip", MessageBoxButton.OK, MessageBoxImage.Warning);
                    SaveDirectoryTextBox.Focus();
                    return;
                }

                // 验证快捷键
                var hotkeyText = HotkeyTextBox.Text;
                if (_dirty.Hotkey && (string.IsNullOrWhiteSpace(hotkeyText) || !hotkeyText.Contains("+")))
                {
                    System.Windows.MessageBox.Show("快捷键格式错误。", "PathSnip", MessageBoxButton.OK, MessageBoxImage.Warning);
                    HotkeyTextBox.Focus();
                    return;
                }

                // 验证文件名模板
                var template = FileNameTemplateTextBox.Text.Trim();
                var invalidChars = Path.GetInvalidFileNameChars();
                bool hasInvalidChar = false;
                foreach (var c in invalidChars)
                {
                    if (template.Contains(c.ToString()))
                    {
                        hasInvalidChar = true;
                        break;
                    }
                }

                if (hasInvalidChar)
                {
                    System.Windows.MessageBox.Show(
                        "文件名模板包含非法字符（如 \\ / : * ? \" < > |），请修改后重试。",
                        "PathSnip",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    FileNameTemplateTextBox.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(template))
                {
                    template = "{yyyy}-{MM}-{dd}_{HHmmss}";
                }

                // ========== 第二步：验证通过后，再修改内存并保存 ==========
                 
                var config = ConfigService.Instance;

                bool configChanged = false;
                bool hotkeyUpdateFailed = false;

                if (_dirty.StartWithWindows)
                {
                    var startWithWindowsEnabled = StartWithWindowsCheckBox.IsChecked == true;
                    StartWithWindowsHelper.SetEnabled(startWithWindowsEnabled);
                }

                // 保存快捷键
                if (_dirty.Hotkey)
                {
                    var parts = hotkeyText.Split('+');
                    if (parts.Length >= 2)
                    {
                        var modifiersStr = string.Join("+", parts, 0, parts.Length - 1);
                        var keyStr = parts[parts.Length - 1];

                        bool updateSuccess = HotkeyChanged?.Invoke(modifiersStr, keyStr) ?? true;
                        if (updateSuccess)
                        {
                            config.HotkeyModifiers = modifiersStr;
                            config.HotkeyKey = keyStr;
                            configChanged = true;
                            _dirty.Hotkey = false;
                        }
                        else
                        {
                            hotkeyUpdateFailed = true;
                        }
                    }
                }

                // 保存目录
                if (_dirty.SaveDirectory)
                {
                    config.SaveDirectory = saveDirectory;
                    configChanged = true;
                    _dirty.SaveDirectory = false;
                }

                // 保存通知设置
                if (_dirty.ShowNotification)
                {
                    config.ShowNotification = ShowNotificationCheckBox.IsChecked == true;
                    configChanged = true;
                    _dirty.ShowNotification = false;
                }

                // 保存剪贴板模式
                if (_dirty.ClipboardMode)
                {
                    var clipboardModeSelectedItem = ClipboardModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
                    var clipboardModeTag = clipboardModeSelectedItem?.Tag as string ?? clipboardModeSelectedItem?.Tag?.ToString();
                    if (string.IsNullOrWhiteSpace(clipboardModeTag))
                    {
                        clipboardModeTag = "PathOnly";
                    }
                    switch (clipboardModeTag)
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

                    configChanged = true;
                    _dirty.ClipboardMode = false;
                }

                // 保存路径格式
                if (_dirty.PathFormat)
                {
                    var pathFormatSelectedItem = PathFormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
                    var pathFormatTag = pathFormatSelectedItem?.Tag as string ?? pathFormatSelectedItem?.Tag?.ToString();
                    if (string.IsNullOrWhiteSpace(pathFormatTag))
                    {
                        pathFormatTag = "Text";
                    }
                    config.PathFormat = pathFormatTag;
                    configChanged = true;
                    _dirty.PathFormat = false;
                }

                if (_dirty.MarkdownHtmlCopyMode)
                {
                    var markdownHtmlCopyModeSelectedItem = MarkdownHtmlCopyModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
                    var markdownHtmlCopyModeTag = markdownHtmlCopyModeSelectedItem?.Tag as string ?? markdownHtmlCopyModeSelectedItem?.Tag?.ToString();
                    if (string.IsNullOrWhiteSpace(markdownHtmlCopyModeTag))
                    {
                        markdownHtmlCopyModeTag = "SnippetOnly";
                    }
                    config.MarkdownHtmlCopyMode = markdownHtmlCopyModeTag;
                    configChanged = true;
                    _dirty.MarkdownHtmlCopyMode = false;
                }

                // 保存文件名模板
                if (_dirty.FileNameTemplate)
                {
                    config.FileNameTemplate = template;
                    configChanged = true;
                    _dirty.FileNameTemplate = false;
                }

                if (_dirty.EnableSmartSnap)
                {
                    config.EnableSmartSnap = EnableSmartSnapCheckBox.IsChecked == true;
                    configChanged = true;
                    _dirty.EnableSmartSnap = false;
                }

                if (_dirty.EnableElementSnap)
                {
                    config.EnableElementSnap = EnableElementSnapCheckBox.IsChecked == true;
                    configChanged = true;
                    _dirty.EnableElementSnap = false;
                }

                if (_dirty.HoldAltToBypassSnap)
                {
                    config.HoldAltToBypassSnap = HoldAltToBypassSnapCheckBox.IsChecked == true;
                    configChanged = true;
                    _dirty.HoldAltToBypassSnap = false;
                }

                if (_dirty.SmartSnapMode)
                {
                    var snapModeSelectedItem = SmartSnapModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
                    var snapModeTag = snapModeSelectedItem?.Tag as string ?? snapModeSelectedItem?.Tag?.ToString();
                    if (string.IsNullOrWhiteSpace(snapModeTag))
                    {
                        snapModeTag = "Auto";
                    }
                    switch (snapModeTag)
                    {
                        case "WindowOnly":
                            config.SmartSnapMode = SmartSnapMode.WindowOnly;
                            break;
                        case "ElementPreferred":
                            config.SmartSnapMode = SmartSnapMode.ElementPreferred;
                            break;
                        case "ManualOnly":
                            config.SmartSnapMode = SmartSnapMode.ManualOnly;
                            break;
                        default:
                            config.SmartSnapMode = SmartSnapMode.Auto;
                            break;
                    }

                    configChanged = true;
                    _dirty.SmartSnapMode = false;
                }

                if (configChanged)
                {
                    config.Save();
                }

                if (hotkeyUpdateFailed)
                {
                    System.Windows.MessageBox.Show("设置已保存，但热键更新失败（可能已被占用），已保留原热键且不会保存无效热键。", "PathSnip", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    System.Windows.MessageBox.Show("设置已保存", "PathSnip", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                // 不关闭窗口，让用户可以继续修改
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "PathSnip", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event Func<string, string, bool> HotkeyChanged;

        private sealed class DirtyFlags
        {
            public bool Hotkey { get; set; }
            public bool SaveDirectory { get; set; }
            public bool StartWithWindows { get; set; }
            public bool ShowNotification { get; set; }
            public bool ClipboardMode { get; set; }
            public bool PathFormat { get; set; }
            public bool MarkdownHtmlCopyMode { get; set; }
            public bool FileNameTemplate { get; set; }
            public bool EnableSmartSnap { get; set; }
            public bool SmartSnapMode { get; set; }
            public bool EnableElementSnap { get; set; }
            public bool HoldAltToBypassSnap { get; set; }
        }
    }
}
