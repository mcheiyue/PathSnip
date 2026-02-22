using System;
using System.Windows;

namespace PathSnip.Services
{
    public static class ClipboardService
    {
        public static void SetText(string text)
        {
            try
            {
                Clipboard.SetText(text);
                LogService.Log($"路径已复制到剪贴板: {text}");
            }
            catch (Exception ex)
            {
                LogService.LogError("复制到剪贴板失败", ex);
                throw;
            }
        }
    }
}
