using System;
using Microsoft.Win32;

namespace PathSnip.Services
{
    public static class StartWithWindowsHelper
    {
        private const string AppName = "PathSnip";
        private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false))
                {
                    return key.GetValue(AppName) != null;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("检查开机自启失败", ex);
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true))
                {
                    if (key == null) return;

                    if (enabled)
                    {
                        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        key.SetValue(AppName, $"\"{exePath}\"");
                        LogService.Log("已开启开机自启");
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                        LogService.Log("已关闭开机自启");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("设置开机自启失败", ex);
            }
        }
    }
}
