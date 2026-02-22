using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace PathSnip.Services
{
    public static class FileService
    {
        public static string Save(BitmapSource bitmap)
        {
            var config = ConfigService.Instance;
            var directory = config.SaveDirectory;

            // 确保目录存在
            Directory.CreateDirectory(directory);

            // 生成文件名
            string fileName = $"{DateTime.Now:yyyy-MM-dd_HHmmss}.png";
            string fullPath = Path.Combine(directory, fileName);

            // 处理文件名冲突
            int counter = 1;
            while (File.Exists(fullPath))
            {
                fileName = $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{counter}.png";
                fullPath = Path.Combine(directory, fileName);
                counter++;
            }

            // 保存文件
            using (var fileStream = new FileStream(fullPath, FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(fileStream);
            }

            return fullPath;
        }
    }
}
