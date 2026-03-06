using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace PathSnip.Services
{
    public static class FileService
    {
        public static string Save(BitmapSource bitmap, string operationId = null)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                operationId = LogService.CreateOperationId("file");
            }
            var stopwatch = Stopwatch.StartNew();

            var config = ConfigService.Instance;
            var directory = config.SaveDirectory;

            try
            {
                // 确保目录存在
                Directory.CreateDirectory(directory);

                // 使用模板生成文件名
                string fileName = GenerateFileName(config.FileNameTemplate) + ".png";
                string fullPath = Path.Combine(directory, fileName);

                // 处理文件名冲突
                int counter = 1;
                while (File.Exists(fullPath))
                {
                    fileName = $"{GenerateFileName(config.FileNameTemplate)}_{counter}.png";
                    fullPath = Path.Combine(directory, fileName);
                    counter++;
                }

                using (var fileStream = new FileStream(fullPath, FileMode.Create))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(fileStream);
                }

                stopwatch.Stop();
                LogService.LogInfo("file.save.completed", $"elapsedMs={stopwatch.ElapsedMilliseconds} pixel={bitmap?.PixelWidth ?? 0}x{bitmap?.PixelHeight ?? 0} pathLength={fullPath.Length}", operationId, "file.save");
                return fullPath;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogService.LogException("file.save.failed", ex, $"elapsedMs={stopwatch.ElapsedMilliseconds} pixel={bitmap?.PixelWidth ?? 0}x{bitmap?.PixelHeight ?? 0}", operationId, "file.save");
                throw;
            }
        }

        private static string GenerateFileName(string template)
        {
            var now = DateTime.Now;
            var result = template;

            // 替换日期时间占位符
            result = result.Replace("{yyyy}", now.ToString("yyyy"));
            result = result.Replace("{MM}", now.ToString("MM"));
            result = result.Replace("{dd}", now.ToString("dd"));
            result = result.Replace("{HH}", now.ToString("HH"));
            result = result.Replace("{mm}", now.ToString("mm"));
            result = result.Replace("{ss}", now.ToString("ss"));
            result = result.Replace("{HHmmss}", now.ToString("HHmmss"));

            // 替换 GUID 占位符
            result = result.Replace("{GUID}", Guid.NewGuid().ToString("N").Substring(0, 8));

            // 过滤非法字符（防御性编程）
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                result = result.Replace(c.ToString(), "_");
            }

            return result;
        }
    }
}
