using System;
using System.IO;
using Newtonsoft.Json;

namespace PathSnip.Services
{
    public enum ClipboardMode
    {
        PathOnly,
        ImageOnly,
        ImageAndPath
    }

    public class AppConfig
    {
        public string HotkeyModifiers { get; set; } = "Ctrl+Shift";
        public string HotkeyKey { get; set; } = "A";
        public string SaveDirectory { get; set; } = string.Empty;
        public bool ShowNotification { get; set; } = true;
        public ClipboardMode ClipboardMode { get; set; } = ClipboardMode.PathOnly;
        public string PathFormat { get; set; } = "Text";
    }

    public class ConfigService
    {
        private static ConfigService _instance;
        public static ConfigService Instance
        {
            get { return _instance ?? (_instance = new ConfigService()); }
        }

        private readonly string _configPath;
        private AppConfig _config;

        public string HotkeyModifiers { get; set; }
        public string HotkeyKey { get; set; }
        public string SaveDirectory { get; set; }
        public bool ShowNotification { get; set; }
        public ClipboardMode ClipboardMode { get; set; }
        public string PathFormat { get; set; }

        private ConfigService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PathSnip");

            _configPath = Path.Combine(appDataPath, "config.json");

            Load();
        }

        public void EnsureDirectoryExists()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
            Directory.CreateDirectory(SaveDirectory);
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                }
                else
                {
                    _config = new AppConfig();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("配置文件加载失败", ex);
                _config = new AppConfig();
            }

            // 设置默认值
            HotkeyModifiers = string.IsNullOrEmpty(_config.HotkeyModifiers) ? "Ctrl+Shift" : _config.HotkeyModifiers;
            HotkeyKey = string.IsNullOrEmpty(_config.HotkeyKey) ? "A" : _config.HotkeyKey;
            SaveDirectory = string.IsNullOrEmpty(_config.SaveDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PathSnip")
                : _config.SaveDirectory;

            ShowNotification = _config.ShowNotification;
            ClipboardMode = _config.ClipboardMode;
            PathFormat = string.IsNullOrEmpty(_config.PathFormat) ? "Text" : _config.PathFormat;
        }

        public void Save()
        {
            try
            {
                _config.HotkeyModifiers = HotkeyModifiers;
                _config.HotkeyKey = HotkeyKey;
                _config.SaveDirectory = SaveDirectory;
                _config.ShowNotification = ShowNotification;
                _config.ClipboardMode = ClipboardMode;
                _config.PathFormat = PathFormat;

                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(_configPath, json);

                LogService.Log("配置已保存");
            }
            catch (Exception ex)
            {
                LogService.LogError("配置保存失败", ex);
            }
        }
    }
}
