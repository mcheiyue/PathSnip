using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace PathSnip.Services
{
    public static class FileService
    {
        private const int MaxCreateAttempts = 20;
        private const int ErrorCodeFileExists = 80;
        private const int ErrorCodeAlreadyExists = 183;

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
                Directory.CreateDirectory(directory);

                string baseName = GenerateFileName(config.FileNameTemplate);
                string fullPath = SaveWithCreateNewRetry(bitmap, directory, baseName, operationId);

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

        private static string SaveWithCreateNewRetry(BitmapSource bitmap, string directory, string baseName, string operationId)
        {
            for (int attempt = 0; attempt < MaxCreateAttempts; attempt++)
            {
                string suffix = attempt == 0 ? string.Empty : $"_{attempt}";
                string fileName = $"{baseName}{suffix}.png";
                string fullPath = Path.Combine(directory, fileName);

                try
                {
                    using (var fileStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        encoder.Save(fileStream);
                    }

                    if (attempt > 0)
                    {
                        LogService.LogInfo("file.save.collision_resolved", $"attempt={attempt + 1} maxAttempts={MaxCreateAttempts} fileName={fileName}", operationId, "file.save");
                    }

                    return fullPath;
                }
                catch (IOException ex) when (IsFileAlreadyExistsException(ex))
                {
                    LogService.LogWarn("file.save.collision_retry", $"attempt={attempt + 1} maxAttempts={MaxCreateAttempts} fileName={fileName} hresult=0x{ex.HResult:X8}", operationId, "file.save");
                }
            }

            LogService.LogWarn("file.save.create_new_exhausted", $"maxAttempts={MaxCreateAttempts} baseName={baseName}", operationId, "file.save");
            throw new IOException($"Unable to create unique file name after {MaxCreateAttempts} attempts.");
        }

        private static bool IsFileAlreadyExistsException(IOException ex)
        {
            int errorCode = ex.HResult & 0xFFFF;
            return errorCode == ErrorCodeFileExists || errorCode == ErrorCodeAlreadyExists;
        }
    }
}
