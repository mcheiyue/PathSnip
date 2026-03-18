using System;
using System.IO;
using System.Windows;
using PathSnip.Services;

namespace PathSnip
{
    public partial class SettingsWindow
    {
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDirectory = SaveDirectoryTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(saveDirectory))
                {
                    System.Windows.MessageBox.Show("保存目录不能为空。", "PathSnip", MessageBoxButton.OK, MessageBoxImage.Warning);
                    SaveDirectoryTextBox.Focus();
                    return;
                }

                var hotkeyText = HotkeyTextBox.Text;
                if (_dirty.Hotkey && (string.IsNullOrWhiteSpace(hotkeyText) || !hotkeyText.Contains("+")))
                {
                    System.Windows.MessageBox.Show("快捷键格式错误。", "PathSnip", MessageBoxButton.OK, MessageBoxImage.Warning);
                    HotkeyTextBox.Focus();
                    return;
                }

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

                var config = ConfigService.Instance;

                bool configChanged = false;
                bool hotkeyUpdateFailed = false;

                if (_dirty.StartWithWindows)
                {
                    var startWithWindowsEnabled = StartWithWindowsCheckBox.IsChecked == true;
                    StartWithWindowsHelper.SetEnabled(startWithWindowsEnabled);
                }

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

                if (_dirty.SaveDirectory)
                {
                    config.SaveDirectory = saveDirectory;
                    configChanged = true;
                    _dirty.SaveDirectory = false;
                }

                if (_dirty.ShowNotification)
                {
                    config.ShowNotification = ShowNotificationCheckBox.IsChecked == true;
                    configChanged = true;
                    _dirty.ShowNotification = false;
                }

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
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "PathSnip", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event Func<string, string, bool> HotkeyChanged;
    }
}
