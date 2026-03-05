using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PathSnip.Helpers
{
    /// <summary>
    /// 放大镜与拾色器辅助类
    /// 从内存中的 BitmapSource 提取像素和放大区域
    /// </summary>
    public static class MagnifierHelper
    {
        private static BitmapSource _cachedSource;
        private static BitmapSource _cachedMagnifiedRegion;
        private static int _cachedPixelX;
        private static int _cachedPixelY;
        private static int _cachedCaptureSize;
        private static readonly object _cacheLock = new object();
        /// <summary>
        /// 从 BitmapSource 提取单像素颜色（自动处理 DPI）
        /// </summary>
        /// <param name="source">背景图像 BitmapSource</param>
        /// <param name="logicalX">逻辑坐标 X</param>
        /// <param name="logicalY">逻辑坐标 Y</param>
        /// <param name="dpiScaleX">X 轴 DPI 缩放因子</param>
        /// <param name="dpiScaleY">Y 轴 DPI 缩放因子</param>
        /// <returns>像素颜色</returns>
        public static Color GetPixelColor(BitmapSource source, double logicalX, double logicalY,
            double dpiScaleX, double dpiScaleY)
        {
            if (source == null) return Colors.Transparent;

            // 边界限制（防越界）
            int pixelX = (int)Math.Max(0, Math.Min(logicalX * dpiScaleX, source.PixelWidth - 1));
            int pixelY = (int)Math.Max(0, Math.Min(logicalY * dpiScaleY, source.PixelHeight - 1));

            var rect = new Int32Rect(pixelX, pixelY, 1, 1);
            var pixels = new byte[4];
            
            try
            {
                source.CopyPixels(rect, pixels, 4, 0);
            }
            catch
            {
                return Colors.Transparent;
            }

            // BGRA32 → RGB
            return Color.FromRgb(pixels[2], pixels[1], pixels[0]);
        }

        /// <summary>
        /// 提取放大区域（captureSize × captureSize → 放大到 displaySize × displaySize）
        /// </summary>
        /// <param name="source">背景图像 BitmapSource</param>
        /// <param name="logicalX">逻辑坐标 X（中心点）</param>
        /// <param name="logicalY">逻辑坐标 Y（中心点）</param>
        /// <param name="captureSize">捕获区域大小（物理像素）</param>
        /// <param name="dpiScaleX">X 轴 DPI 缩放因子</param>
        /// <param name="dpiScaleY">Y 轴 DPI 缩放因子</param>
        /// <returns>放大后的图像</returns>
        public static BitmapSource GetMagnifiedRegion(BitmapSource source, double logicalX, double logicalY,
            int captureSize, double dpiScaleX, double dpiScaleY)
        {
            if (source == null) return null;

            int pixelX = (int)(logicalX * dpiScaleX);
            int pixelY = (int)(logicalY * dpiScaleY);

            lock (_cacheLock)
            {
                if (_cachedSource == source &&
                    _cachedPixelX == pixelX &&
                    _cachedPixelY == pixelY &&
                    _cachedCaptureSize == captureSize)
                {
                    return _cachedMagnifiedRegion;
                }
            }

            int cropX = Math.Max(0, pixelX - captureSize / 2);
            int cropY = Math.Max(0, pixelY - captureSize / 2);
            int cropW = Math.Min(captureSize, source.PixelWidth - cropX);
            int cropH = Math.Min(captureSize, source.PixelHeight - cropY);

            if (cropW <= 0 || cropH <= 0) return null;

            try
            {
                var cropped = new CroppedBitmap(source, new Int32Rect(cropX, cropY, cropW, cropH));

                double scale = 4.0;
                var scaled = new TransformedBitmap(cropped, new ScaleTransform(scale, scale));

                int targetSize = captureSize * (int)scale;
                var result = EnsureSize(scaled, targetSize, targetSize);
                result.Freeze();

                lock (_cacheLock)
                {
                    _cachedSource = source;
                    _cachedMagnifiedRegion = result;
                    _cachedPixelX = pixelX;
                    _cachedPixelY = pixelY;
                    _cachedCaptureSize = captureSize;
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 确保图像为目标尺寸，不足部分用黑色补齐
        /// </summary>
        private static BitmapSource EnsureSize(BitmapSource source, int targetWidth, int targetHeight)
        {
            if (source == null) return null;

            int sourceWidth = source.PixelWidth;
            int sourceHeight = source.PixelHeight;

            // 如果尺寸已经满足要求，直接返回
            if (sourceWidth >= targetWidth && sourceHeight >= targetHeight)
            {
                // 裁剪到目标尺寸
                int cropW = Math.Min(targetWidth, sourceWidth);
                int cropH = Math.Min(targetHeight, sourceHeight);
                return new CroppedBitmap(source, new Int32Rect(0, 0, cropW, cropH));
            }

            // 创建目标尺寸的画布
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // 黑色背景
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, targetWidth, targetHeight));

                // 绘制原图（左上角对齐）
                dc.DrawImage(source, new Rect(0, 0, sourceWidth, sourceHeight));
            }

            var renderBitmap = new RenderTargetBitmap(
                targetWidth, targetHeight,
                96, 96,
                PixelFormats.Pbgra32);

            renderBitmap.Render(visual);
            renderBitmap.Freeze();

            return renderBitmap;
        }

        /// <summary>
        /// 将 Color 转换为 RGB 字符串
        /// </summary>
        public static string ToRgbString(Color color)
        {
            return $"RGB({color.R}, {color.G}, {color.B})";
        }

        /// <summary>
        /// 将 Color 转换为 HEX 字符串（不带 # 前缀）
        /// </summary>
        public static string ToHexString(Color color)
        {
            return $"{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        /// <summary>
        /// 将 Color 转换为 HEX 字符串（带 # 前缀）
        /// </summary>
        public static string ToHexStringWithPrefix(Color color)
        {
            return $"#{ToHexString(color)}";
        }
    }
}